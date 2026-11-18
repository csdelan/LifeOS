using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Capture;
using LifeOs.Application.Reviews;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// Code review CR68: a request sent again with the same request id (an event's external id, which
/// <c>/mcp</c> sends) is answered by the kernel, not the door. Its text and event are one
/// transaction, so retries that race the first call write the text once and leave none that
/// nothing names; each answers the first write, already written; and an id used for another kind
/// of write is refused with nothing written. Each test writes as a source of its own.
/// </summary>
public sealed class RequestRetryTests(ClassDatabase classDb) : IClassFixture<ClassDatabase>
{
    private const int Racers = 8;

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private ServiceProvider Kernel(out string source)
    {
        source = $"mcp:retry-{Guid.NewGuid():N}";
        return classDb.Kernel(sourceId: source);
    }

    [Fact]
    public async Task Captures_racing_with_one_request_id_write_the_text_and_note_once()
    {
        await using var kernel = Kernel(out var source);
        var filing = kernel.GetRequiredService<InboxFilingService>();
        var key = Guid.NewGuid().ToString();
        var text = $"Buy paint {Guid.NewGuid():N}";

        var answers = await Task.WhenAll(Enumerable.Range(0, Racers).Select(_ =>
            Task.Run(() => filing.CaptureAsync(text, externalId: key, cancellationToken: Ct), Ct)));

        Assert.Single(answers.Select(a => a.EventId).Distinct());
        Assert.Single(answers, a => !a.AlreadyWritten);
        Assert.Equal(1, await EventsAsync(source, key));
        await AssertTextStoredOnceAsync(text, answers[0].EventId);
    }

    [Fact]
    public async Task Journal_entries_racing_with_one_request_id_write_the_entry_once()
    {
        await using var kernel = Kernel(out var source);
        var task = await kernel.GetRequiredService<SubjectService>().CreateAsync(
            SubjectTypes.Task, $"Paint the fence {Guid.NewGuid():N}", cancellationToken: Ct);
        var journal = kernel.GetRequiredService<JournalService>();
        var key = Guid.NewGuid().ToString();
        var text = $"First coat {Guid.NewGuid():N}";

        var answers = await Task.WhenAll(Enumerable.Range(0, Racers).Select(_ =>
            Task.Run(() => journal.AppendAsync(task.Urn, text, key, cancellationToken: Ct), Ct)));

        Assert.Single(answers.Select(a => a.EventId).Distinct());
        Assert.All(answers, a => Assert.Equal(task.Id, a.Subject.Id));
        Assert.Single(answers, a => !a.AlreadyWritten);
        Assert.Equal(1, await EventsAsync(source, key));
        await AssertTextStoredOnceAsync(text, answers[0].EventId);
    }

    [Fact]
    public async Task Reflection_links_racing_with_one_request_id_write_the_link_once()
    {
        await using var kernel = Kernel(out var source);
        var today = await kernel.GetRequiredService<IHomeCalendar>().TodayAsync(Ct);
        var review = (await kernel.GetRequiredService<ReviewService>().OpenAsync("daily", today.AddDays(-3), Ct)).Review;
        var reflections = kernel.GetRequiredService<ReflectionService>();
        var key = Guid.NewGuid().ToString();
        var path = $"10 Reflections/{Guid.NewGuid():N}.md";

        var answers = await Task.WhenAll(Enumerable.Range(0, Racers).Select(_ =>
            Task.Run(() => reflections.LinkAsync(review.Id, path, "Chris2.0", "Shipped the shed plans.", key, cancellationToken: Ct), Ct)));

        Assert.Single(answers.Select(a => a.EventId).Distinct());
        Assert.Single(answers, a => a.Written);
        Assert.Equal(Racers - 1, answers.Count(a => a.AlreadyWritten));
        Assert.Equal(1, await EventsAsync(source, key));
    }

    [Fact]
    public async Task A_retried_idea_answers_the_idea_and_the_item_it_concerns_and_makes_nothing_more()
    {
        await using var kernel = Kernel(out var source);
        var project = await kernel.GetRequiredService<SubjectService>().CreateAsync(
            SubjectTypes.Project, $"Deck {Guid.NewGuid():N}", cancellationToken: Ct);
        var filing = kernel.GetRequiredService<InboxFilingService>();
        var key = Guid.NewGuid().ToString();
        var text = $"A pergola {Guid.NewGuid():N}";

        var first = await filing.CaptureAsync(text, SubjectTypes.Idea, key, concerning: project.Urn, cancellationToken: Ct);
        var again = await filing.CaptureAsync(text, SubjectTypes.Idea, key, concerning: project.Urn, cancellationToken: Ct);

        Assert.False(first.AlreadyWritten);
        Assert.True(again.AlreadyWritten);
        Assert.Equal(first.EventId, again.EventId);
        Assert.Equal(first.Item!.Id, again.Item!.Id);
        Assert.Equal([project.Id], first.Concerns.Select(c => c.Id));
        Assert.Equal([project.Id], again.Concerns.Select(c => c.Id));
        await using var connection = new NpgsqlConnection(classDb.ConnectionString);
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM bsk.subject WHERE type = 'Idea' AND title = @text;", new { text }, cancellationToken: Ct)));
        Assert.Equal(1, await EventsAsync(source, key));
    }

    [Fact]
    public async Task A_request_id_used_for_another_kind_of_write_is_refused_and_writes_nothing()
    {
        await using var kernel = Kernel(out _);
        var task = await kernel.GetRequiredService<SubjectService>().CreateAsync(
            SubjectTypes.Task, $"Sand the deck {Guid.NewGuid():N}", cancellationToken: Ct);
        var key = Guid.NewGuid().ToString();
        await kernel.GetRequiredService<InboxFilingService>().CaptureAsync($"Buy sandpaper {Guid.NewGuid():N}", externalId: key, cancellationToken: Ct);
        var entry = $"Sanded half {Guid.NewGuid():N}";

        var refused = await Assert.ThrowsAsync<RequestReusedException>(() =>
            kernel.GetRequiredService<JournalService>().AppendAsync(task.Urn, entry, key, cancellationToken: Ct));

        Assert.Equal(RefusalKind.Conflict, refused.Kind);
        Assert.Contains("different write", refused.Message, StringComparison.Ordinal);
        await using var connection = new NpgsqlConnection(classDb.ConnectionString);
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM bsk.artifact WHERE content = @entry;", new { entry }, cancellationToken: Ct)));
    }

    // Agent-access plan §5.4: a create is the one item write a retry could repeat, since each new
    // item gets a fresh URN, so its request id rides on the status the item is born with.
    [Fact]
    public async Task Creates_racing_with_one_request_id_make_one_item_born_at_its_first_status()
    {
        await using var kernel = Kernel(out var source);
        var creation = kernel.GetRequiredService<SubjectCreationService>();
        var key = Guid.NewGuid().ToString();
        var title = $"Stain the deck {Guid.NewGuid():N}";

        var answers = await Task.WhenAll(Enumerable.Range(0, Racers).Select(_ =>
            Task.Run(() => creation.CreateAsync(SubjectTypes.Task, title, "{}", externalId: key, cancellationToken: Ct), Ct)));

        Assert.Single(answers.Select(a => a.Subject.Id).Distinct());
        Assert.Single(answers, a => !a.AlreadyWritten);
        await using var connection = new NpgsqlConnection(classDb.ConnectionString);
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM bsk.subject WHERE title = @title;", new { title }, cancellationToken: Ct)));
        var born = await connection.QuerySingleAsync<(string Kind, string SubjectId, string Status)>(new CommandDefinition(
            "SELECT kind, payload->>'subject_id', payload->>'status' FROM bsk.event WHERE source_id = @source AND external_id = @key;",
            new { source, key }, cancellationToken: Ct));
        Assert.Equal((EventKinds.StateChange, answers[0].Subject.Id.ToString(), "Not started"), born);
    }

    [Fact]
    public async Task A_create_under_a_parent_retried_makes_one_child_and_one_edge()
    {
        await using var kernel = Kernel(out var source);
        var project = await kernel.GetRequiredService<SubjectService>().CreateAsync(
            SubjectTypes.Project, $"Deck {Guid.NewGuid():N}", cancellationToken: Ct);
        var creation = kernel.GetRequiredService<SubjectCreationService>();
        var key = Guid.NewGuid().ToString();
        var title = $"Buy stain {Guid.NewGuid():N}";

        var first = await creation.CreateAsync(SubjectTypes.Task, title, "{}", project.Urn, externalId: key, cancellationToken: Ct);
        var again = await creation.CreateAsync(SubjectTypes.Task, title, "{}", project.Urn, externalId: key, cancellationToken: Ct);

        Assert.False(first.AlreadyWritten);
        Assert.Equal(project.Id, first.Link!.Parent.Id);
        Assert.True(again.AlreadyWritten);
        Assert.Equal(first.Subject.Id, again.Subject.Id);
        await using var connection = new NpgsqlConnection(classDb.ConnectionString);
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM bsk.subject WHERE title = @title;", new { title }, cancellationToken: Ct)));
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM bsk.subject_relation WHERE from_subject = @child AND to_subject = @parent;",
            new { child = first.Subject.Id, parent = project.Id }, cancellationToken: Ct)));
        Assert.Equal(1, await EventsAsync(source, key));
    }

    [Fact]
    public async Task A_request_id_used_for_a_capture_is_refused_for_a_create_and_nothing_is_made()
    {
        await using var kernel = Kernel(out _);
        var key = Guid.NewGuid().ToString();
        await kernel.GetRequiredService<InboxFilingService>().CaptureAsync($"Buy stain {Guid.NewGuid():N}", externalId: key, cancellationToken: Ct);
        var title = $"Stain the rail {Guid.NewGuid():N}";

        var refused = await Assert.ThrowsAsync<RequestReusedException>(() =>
            kernel.GetRequiredService<SubjectCreationService>().CreateAsync(SubjectTypes.Task, title, "{}", externalId: key, cancellationToken: Ct));

        Assert.Equal(RefusalKind.Conflict, refused.Kind);
        await using var connection = new NpgsqlConnection(classDb.ConnectionString);
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM bsk.subject WHERE title = @title;", new { title }, cancellationToken: Ct)));
    }

    [Fact]
    public async Task A_type_with_no_status_cant_carry_a_request_id_and_nothing_is_made()
    {
        await using var kernel = Kernel(out _);
        var name = $"Pat {Guid.NewGuid():N}";

        var refused = await Assert.ThrowsAsync<ArgumentException>(() =>
            kernel.GetRequiredService<SubjectCreationService>().CreateAsync(
                SubjectTypes.Person, name, "{}", externalId: Guid.NewGuid().ToString(), cancellationToken: Ct));

        Assert.Contains("has no status", refused.Message, StringComparison.Ordinal);
        await using var connection = new NpgsqlConnection(classDb.ConnectionString);
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM bsk.subject WHERE title = @name;", new { name }, cancellationToken: Ct)));
    }

    [Fact]
    public async Task An_idea_created_again_with_its_request_id_is_flagged_for_the_inbox_once()
    {
        await using var kernel = Kernel(out _);
        var creation = kernel.GetRequiredService<SubjectCreationService>();
        var key = Guid.NewGuid().ToString();
        var title = $"A deck bench {Guid.NewGuid():N}";

        var first = await creation.CreateAsync(SubjectTypes.Idea, title, "{}", externalId: key, cancellationToken: Ct);
        var again = await creation.CreateAsync(SubjectTypes.Idea, title, "{}", externalId: key, cancellationToken: Ct);

        Assert.Equal(first.Subject.Id, again.Subject.Id);
        Assert.False(again.FollowUpsPending);
        await using var connection = new NpgsqlConnection(classDb.ConnectionString);
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM bsk.event WHERE kind = 'triage' AND payload->>'item_id' = @item;",
            new { item = first.Subject.Id.ToString() }, cancellationToken: Ct)));
    }

    private async Task<int> EventsAsync(string source, string key)
    {
        await using var connection = new NpgsqlConnection(classDb.ConnectionString);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM bsk.event WHERE source_id = @source AND external_id = @key;",
            new { source, key }, cancellationToken: Ct));
    }

    /// <summary>The text is stored once, and it's the event's: none is left that nothing names.</summary>
    private async Task AssertTextStoredOnceAsync(string text, Guid eventId)
    {
        await using var connection = new NpgsqlConnection(classDb.ConnectionString);
        var stored = (await connection.QueryAsync<(Guid Id, Guid? EventId)>(new CommandDefinition(
            """
            SELECT a.id, e.id
            FROM bsk.artifact a
            LEFT JOIN bsk.event e ON e.artifact_id = a.id
            WHERE a.content = @text;
            """,
            new { text }, cancellationToken: Ct))).ToList();
        var only = Assert.Single(stored);
        Assert.Equal(eventId, only.EventId);
    }
}
