using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Capture;
using LifeOs.Application.Reviews;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure.DependencyInjection;
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
        return new ServiceCollection()
            .AddLifeOsKernel(classDb.ConnectionString, sourceId: source, homeTimeZone: PostgresFixture.HabitViewZone)
            .BuildServiceProvider();
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
