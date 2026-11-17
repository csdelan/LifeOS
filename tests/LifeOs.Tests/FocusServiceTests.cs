using Dapper;
using LifeOs.Application.Focus;
using LifeOs.Application.Reviews;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// The Primary focus Chris chooses (TODAY-1, D6, focus plan G1): an Active, unarchived Goal or
/// none, kept as note events of which the newest wins, each concerning its Goal and the review it
/// was chosen in. Choosing it in a review turns that review's Keep focus off.
/// </summary>
public sealed class FocusServiceTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<SubjectRef> GoalAsync(DomainTestDatabase db, string title, string? status = "Active")
    {
        var goal = await db.Get<SubjectService>().CreateAsync(
            SubjectTypes.Goal, title, """{"target_date": "2027-06-30"}""", cancellationToken: Ct);
        if (status is not null)
        {
            await db.Get<StatusService>().ChangeStatusAsync(goal.Urn, status, Ct);
        }

        return goal;
    }

    private static async Task<long> FocusEventsAsync(DomainTestDatabase db)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM bsk.event WHERE kind = 'note' AND payload ? 'focus';", cancellationToken: Ct));
    }

    [Fact]
    public async Task The_newest_choice_is_the_focus_and_choosing_it_again_writes_nothing()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var focus = db.Get<FocusService>();
        var shed = await GoalAsync(db, "Ship the shed");
        var run = await GoalAsync(db, "Run a half marathon");

        Assert.Null(await focus.GetAsync(Ct));

        var first = await focus.SetAsync(shed.Urn, cancellationToken: Ct);
        Assert.True(first.Written);
        Assert.Equal((shed.Id, "Ship the shed", true, (string?)null), (first.Focus!.GoalId, first.Focus.GoalTitle, first.Focus.Holds, first.Focus.LapsedAs));
        Assert.Equal(PrimaryFocus.Weekly, first.Focus.Level);

        // By title, as bsk names it; the same Goal again writes nothing.
        Assert.False((await focus.SetAsync("Ship the shed", cancellationToken: Ct)).Written);

        var second = await focus.SetAsync(run.Id.ToString(), cancellationToken: Ct);
        Assert.Equal(run.Id, second.Focus!.GoalId);
        Assert.Equal(run.Id, (await focus.GetAsync(Ct))!.GoalId);

        // No focus is a choice too, and clearing it twice writes once.
        var cleared = await focus.SetAsync(null, cancellationToken: Ct);
        Assert.True(cleared.Written);
        Assert.Null(cleared.Focus!.GoalId);
        Assert.False(cleared.Focus.Lapsed);
        Assert.False((await focus.SetAsync(" ", cancellationToken: Ct)).Written);
        Assert.Equal(3, await FocusEventsAsync(db));
    }

    [Fact]
    public async Task Only_an_active_unarchived_goal_can_be_the_focus()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var focus = db.Get<FocusService>();
        var draft = await GoalAsync(db, "Learn Spanish", status: null);
        var done = await GoalAsync(db, "Paint the fence");
        await db.Get<StatusService>().ChangeStatusAsync(done.Urn, "Completed", Ct);
        var shelved = await GoalAsync(db, "Write a novel");
        await db.Get<ArchiveService>().ArchiveAsync(shelved.Urn, Ct);
        var project = await db.Get<SubjectService>().CreateAsync(SubjectTypes.Project, "Garden beds", cancellationToken: Ct);

        Assert.Contains("is New", (await Assert.ThrowsAsync<ArgumentException>(() => focus.SetAsync(draft.Urn, cancellationToken: Ct))).Message);
        Assert.Contains("is Completed", (await Assert.ThrowsAsync<ArgumentException>(() => focus.SetAsync(done.Urn, cancellationToken: Ct))).Message);
        Assert.Contains("is archived", (await Assert.ThrowsAsync<ArgumentException>(() => focus.SetAsync(shelved.Urn, cancellationToken: Ct))).Message);
        Assert.Contains("is a Project", (await Assert.ThrowsAsync<ArgumentException>(() => focus.SetAsync(project.Urn, cancellationToken: Ct))).Message);
        await Assert.ThrowsAsync<SubjectNotFoundException>(() => focus.SetAsync(Guid.NewGuid().ToString(), cancellationToken: Ct));
        Assert.Equal(0, await FocusEventsAsync(db));
    }

    [Fact]
    public async Task A_focus_whose_goal_ends_lapses_and_says_what_it_is_now()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var focus = db.Get<FocusService>();
        var shed = await GoalAsync(db, "Ship the shed");
        await focus.SetAsync(shed.Urn, cancellationToken: Ct);

        await db.Get<StatusService>().ChangeStatusAsync(shed.Urn, "Completed", Ct);
        var completed = (await focus.GetAsync(Ct))!;
        Assert.Equal((false, true, "Completed"), (completed.Holds, completed.Lapsed, completed.LapsedAs));

        await db.Get<StatusService>().ChangeStatusAsync(shed.Urn, "Active", Ct);
        Assert.True((await focus.GetAsync(Ct))!.Holds);

        await db.Get<ArchiveService>().ArchiveAsync(shed.Urn, Ct);
        Assert.Equal("Archived", (await focus.GetAsync(Ct))!.LapsedAs);
    }

    [Fact]
    public async Task Choosing_in_a_review_ties_the_choice_to_it_and_turns_its_keep_focus_off()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var focus = db.Get<FocusService>();
        var reviews = db.Get<ReviewService>();
        var shed = await GoalAsync(db, "Ship the shed");
        var review = (await reviews.OpenAsync(ReviewKinds.Daily, cancellationToken: Ct)).Review;
        Assert.True(review.KeepFocus);

        var chosen = (await focus.SetAsync(shed.Id.ToString(), review.Id, Ct)).Focus!;

        Assert.Equal((review.Id, review.Title), (chosen.ReviewId, chosen.ReviewTitle));
        Assert.False((await reviews.MustGetAsync(review.Id, Ct)).KeepFocus);
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        var concerns = (await connection.QueryAsync<Guid>(new CommandDefinition(
            "SELECT subject_id FROM bsk.subject_event WHERE event_id = @id AND relation = 'concerns';",
            new { id = chosen.EventId }, cancellationToken: Ct))).ToHashSet();
        Assert.Equal(new HashSet<Guid> { shed.Id, review.Id }, concerns);

        // A review that isn't there is refused before anything is written.
        await Assert.ThrowsAsync<SubjectNotFoundException>(() => focus.SetAsync(null, Guid.NewGuid(), Ct));
        Assert.Equal(1, await FocusEventsAsync(db));
    }

    [Theory]
    [InlineData("Goal", "Active", false, null)]
    [InlineData("Goal", "active ", false, null)]
    [InlineData("Goal", null, false, "'G' is New.")]
    [InlineData("Goal", "Abandoned", false, "'G' is Abandoned.")]
    [InlineData("Goal", "Active", true, "'G' is archived.")]
    [InlineData("Project", "Active", false, "'G' is a Project.")]
    public void Why_a_subject_cant_be_the_focus(string type, string? status, bool archived, string? starts)
    {
        var why = PrimaryFocus.WhyNot("G", type, status, archived);
        if (starts is null)
        {
            Assert.Null(why);
        }
        else
        {
            Assert.StartsWith(starts, why);
        }
    }
}
