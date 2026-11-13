using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// Code review CR7: <c>bsk new</c> and <c>POST /api/subjects</c> make an item through one
/// <see cref="SubjectCreationService"/>, so what follows a create can't differ between them.
/// Before it, a repeating Task made with <c>bsk new</c> waited for the next settle for its first
/// repetition, which the API made at once (D16).
/// </summary>
public sealed class SubjectCreationTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_repeating_Task_gets_its_first_repetition_as_it_is_made()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var today = await db.TodayAsync();

        var made = await db.Get<SubjectCreationService>().CreateAsync(
            SubjectTypes.Task, "Water the plants", """{"recurrence":{"freq":"daily"}}""", cancellationToken: Ct);

        var repetition = Assert.Single(await db.Get<ITaskSeriesRepository>().RepetitionsAsync(made.Subject.Urn, Ct));
        Assert.Equal(today, repetition.Occurrence);
        Assert.False(made.Reused);
        Assert.Null(made.Link);
    }

    [Fact]
    public async Task A_Problem_made_again_is_the_same_one_and_waits_in_the_Inbox()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var creation = db.Get<SubjectCreationService>();

        var first = await creation.CreateAsync(SubjectTypes.Problem, "The garage floods", """{"impact":"high"}""", cancellationToken: Ct);
        var again = await creation.CreateAsync(SubjectTypes.Problem, "The garage floods", "{}", cancellationToken: Ct);

        Assert.False(first.Reused);
        Assert.True(again.Reused);
        Assert.Equal(first.Subject.Id, again.Subject.Id);

        await using var connection = new NpgsqlConnection(db.ConnectionString);
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM bsk.v_inbox WHERE item_id = @id;", new { id = first.Subject.Id }, cancellationToken: Ct)));
    }

    [Fact]
    public async Task An_item_made_under_a_parent_is_linked_to_it()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var creation = db.Get<SubjectCreationService>();
        var project = await creation.CreateAsync(SubjectTypes.Project, "Fix the garage", "{}", cancellationToken: Ct);

        var task = await creation.CreateAsync(
            SubjectTypes.Task, "Buy a sump pump", "{}", parent: project.Subject.Urn, cancellationToken: Ct);

        Assert.NotNull(task.Link);
        Assert.Equal(project.Subject.Id, task.Link.Parent.Id);
        Assert.Equal(task.Subject.Id, task.Link.Child.Id);
    }
}
