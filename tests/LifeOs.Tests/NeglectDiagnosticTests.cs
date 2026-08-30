using System.Text.Json;
using Dapper;
using LifeOs.Infrastructure.Diagnostics;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// Integration tests for the neglect diagnostic (M4.2): a cadenced subject with
/// no concerning activity within its window is flagged; a recently-concerned one
/// is not; and Season awareness parks out-of-focus subjects so their dormancy is
/// not mistaken for neglect. Runs the real embedded diagnostic through the M4.1
/// runner. Assertions key off each test's own subjects (unique urns / focus
/// tags) so they are robust to the shared container.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class NeglectDiagnosticTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private DiagnosticRunner Runner => new(postgres.ConnectionString);

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        return connection;
    }

    // Insert a subject with attributes and an explicit creation time so staleness
    // can be exercised deterministically.
    private async Task<Guid> InsertSubjectAsync(
        NpgsqlConnection connection, string type, string ns, object attributes, DateTimeOffset createdAt)
    {
        var attrs = JsonSerializer.Serialize(attributes);
        return await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            """
            INSERT INTO bsk.subject (urn, type, title, attributes, created_at)
            VALUES (@urn, @type, @title, @attrs::jsonb, @createdAt)
            RETURNING id;
            """,
            new { urn = $"urn:bsk:{type.ToLowerInvariant()}:{ns}", type, title = $"subject {ns}", attrs, createdAt },
            cancellationToken: Ct));
    }

    private async Task<Guid> InsertConcerningEventAsync(
        NpgsqlConnection connection, Guid subjectId, DateTimeOffset occurredAt)
    {
        var eventId = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            """
            INSERT INTO bsk.event (kind, provenance, occurred_at, source_id)
            VALUES ('journal', 'declared', @occurredAt, 'test') RETURNING id;
            """,
            new { occurredAt }, cancellationToken: Ct));

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO bsk.subject_event (event_id, subject_id, relation, provenance)
            VALUES (@eventId, @subjectId, 'concerns', 'declared');
            """,
            new { eventId, subjectId }, cancellationToken: Ct));

        return eventId;
    }

    private async Task<Finding?> FindAsync(Guid subjectId)
    {
        var report = await Runner.RunAsync(only: "neglect", cancellationToken: Ct);
        var neglect = Assert.Single(report.Diagnostics);
        Assert.Equal("neglect", neglect.Name);
        return neglect.Findings.FirstOrDefault(f => f.Subject.Id == subjectId);
    }

    [Fact]
    public async Task Stale_cadenced_subject_with_no_activity_is_flagged()
    {
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");
        var subject = await InsertSubjectAsync(
            connection, "Commitment", ns, new { expected_cadence = "weekly" },
            DateTimeOffset.UtcNow.AddDays(-30));

        var finding = await FindAsync(subject);

        Assert.NotNull(finding);
        Assert.False(string.IsNullOrWhiteSpace(finding!.Summary));

        // Evidence carries the window that was exceeded; no event (never concerned).
        var window = finding.Evidence.EnumerateArray()
            .Single(e => e.GetProperty("kind").GetString() == "window");
        Assert.Equal("weekly", window.GetProperty("cadence").GetString());
        Assert.Equal("created_at", window.GetProperty("since_kind").GetString());
        Assert.True(window.GetProperty("elapsed_days").GetInt32() >= 29);
        Assert.DoesNotContain(finding.Evidence.EnumerateArray(),
            e => e.GetProperty("kind").GetString() == "event");
    }

    [Fact]
    public async Task Recently_concerned_subject_is_not_flagged()
    {
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");
        var subject = await InsertSubjectAsync(
            connection, "Commitment", ns, new { expected_cadence = "weekly" },
            DateTimeOffset.UtcNow.AddDays(-30));
        await InsertConcerningEventAsync(connection, subject, DateTimeOffset.UtcNow.AddDays(-2));

        Assert.Null(await FindAsync(subject));
    }

    [Fact]
    public async Task Subject_past_its_window_is_flagged_with_the_last_event_as_evidence()
    {
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");
        var subject = await InsertSubjectAsync(
            connection, "Project", ns, new { expected_cadence = "weekly" },
            DateTimeOffset.UtcNow.AddDays(-90));
        var stale = await InsertConcerningEventAsync(connection, subject, DateTimeOffset.UtcNow.AddDays(-30));

        var finding = await FindAsync(subject);

        Assert.NotNull(finding);
        var evidenceEvent = finding!.Evidence.EnumerateArray()
            .Single(e => e.GetProperty("kind").GetString() == "event");
        Assert.Equal(stale.ToString(), evidenceEvent.GetProperty("id").GetString());
        Assert.Equal("concerns", evidenceEvent.GetProperty("relation").GetString());

        var window = finding.Evidence.EnumerateArray()
            .Single(e => e.GetProperty("kind").GetString() == "window");
        Assert.Equal("last_concern", window.GetProperty("since_kind").GetString());
    }

    [Fact]
    public async Task Subject_without_a_cadence_is_never_flagged()
    {
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");
        var subject = await InsertSubjectAsync(
            connection, "Task", ns, new { }, DateTimeOffset.UtcNow.AddDays(-365));

        Assert.Null(await FindAsync(subject));
    }

    [Fact]
    public async Task Uninterpretable_cadence_is_skipped_not_crashed()
    {
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");
        var subject = await InsertSubjectAsync(
            connection, "Project", ns, new { expected_cadence = "whenever I feel like it" },
            DateTimeOffset.UtcNow.AddDays(-365));

        // The run completes and simply does not flag the subject with the bad cadence.
        Assert.Null(await FindAsync(subject));
    }

    [Fact]
    public async Task Cadence_words_map_to_windows()
    {
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");

        // Monthly, last concerned 40 days ago -> past window -> flagged.
        var overdue = await InsertSubjectAsync(
            connection, "Project", $"{ns}-a", new { expected_cadence = "monthly" },
            DateTimeOffset.UtcNow.AddDays(-200));
        await InsertConcerningEventAsync(connection, overdue, DateTimeOffset.UtcNow.AddDays(-40));

        // Monthly, last concerned 20 days ago -> within window -> not flagged.
        var fresh = await InsertSubjectAsync(
            connection, "Project", $"{ns}-b", new { expected_cadence = "monthly" },
            DateTimeOffset.UtcNow.AddDays(-200));
        await InsertConcerningEventAsync(connection, fresh, DateTimeOffset.UtcNow.AddDays(-20));

        Assert.NotNull(await FindAsync(overdue));
        Assert.Null(await FindAsync(fresh));
    }

    [Fact]
    public async Task In_focus_subject_is_flagged_while_out_of_focus_is_parked()
    {
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");
        var inFocus = $"focus-in-{ns}";
        var outFocus = $"focus-out-{ns}";

        // An active Season whose focus is the in-focus tag.
        await InsertSubjectAsync(
            connection, "Season", ns,
            new { focus = inFocus, ends = DateTimeOffset.UtcNow.AddMonths(2).ToString("yyyy-MM-dd") },
            DateTimeOffset.UtcNow.AddDays(-10));

        var focused = await InsertSubjectAsync(
            connection, "Commitment", $"{ns}-in", new { expected_cadence = "weekly", focus = inFocus },
            DateTimeOffset.UtcNow.AddDays(-30));
        var parked = await InsertSubjectAsync(
            connection, "Commitment", $"{ns}-out", new { expected_cadence = "weekly", focus = outFocus },
            DateTimeOffset.UtcNow.AddDays(-30));

        // Same staleness for both; only the Season focus differs.
        Assert.NotNull(await FindAsync(focused));
        Assert.Null(await FindAsync(parked));
    }

    [Fact]
    public async Task An_ended_season_does_not_park_a_matching_subject()
    {
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");
        var endedFocus = $"focus-ended-{ns}";

        // An active Season (so parking is in play at all) with an unrelated focus,
        // plus an ended Season whose focus matches the subject below.
        await InsertSubjectAsync(
            connection, "Season", $"{ns}-active",
            new { focus = $"active-{ns}", ends = DateTimeOffset.UtcNow.AddMonths(2).ToString("yyyy-MM-dd") },
            DateTimeOffset.UtcNow.AddDays(-10));
        await InsertSubjectAsync(
            connection, "Season", $"{ns}-ended",
            new { focus = endedFocus, ends = DateTimeOffset.UtcNow.AddDays(-1).ToString("yyyy-MM-dd") },
            DateTimeOffset.UtcNow.AddDays(-100));

        // The subject's focus matches only the ENDED season -> not in any active
        // focus -> parked -> not flagged.
        var subject = await InsertSubjectAsync(
            connection, "Commitment", ns, new { expected_cadence = "weekly", focus = endedFocus },
            DateTimeOffset.UtcNow.AddDays(-30));

        Assert.Null(await FindAsync(subject));
    }

    [Fact]
    public async Task Untagged_subject_is_flagged_even_while_a_season_is_active()
    {
        await using var connection = await OpenAsync();
        var ns = Guid.NewGuid().ToString("N");

        await InsertSubjectAsync(
            connection, "Season", ns,
            new { focus = $"something-{ns}", ends = DateTimeOffset.UtcNow.AddMonths(2).ToString("yyyy-MM-dd") },
            DateTimeOffset.UtcNow.AddDays(-10));

        // No focus tag -> never parked, regardless of the active Season.
        var subject = await InsertSubjectAsync(
            connection, "Commitment", ns, new { expected_cadence = "weekly" },
            DateTimeOffset.UtcNow.AddDays(-30));

        Assert.NotNull(await FindAsync(subject));
    }
}
