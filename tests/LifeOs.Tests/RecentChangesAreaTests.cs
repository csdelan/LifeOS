using System.Text.Json;
using Dapper;
using LifeOs.Api.Mcp;
using LifeOs.Api.Read;
using LifeOs.Application.Capture;
using LifeOs.Application.Home;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// Agent-access plan §4.3: <c>recent_changes</c> leaves a switched-off area's events out by
/// <see cref="McpAreas"/>, and fails closed. An event no area claims shows while an area is off
/// only when its payload is the core's, so a new feature's events can't reach an agent that
/// has its area off (code review CR28). Each test runs in a database of its own.
/// </summary>
public sealed class RecentChangesAreaTests(PostgresFixture postgres)
{
    private static readonly IReadOnlySet<string> NoneOff = new HashSet<string>();
    private static readonly IReadOnlySet<string> AllOff = McpOptions.AllDomains.ToHashSet(StringComparer.OrdinalIgnoreCase);

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_cores_own_writes_show_with_every_area_switched_off()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var since = DateTimeOffset.UtcNow.AddMinutes(-1);
        var today = await db.TodayAsync();

        var task = (await db.Get<SubjectCreationService>().CreateAsync(SubjectTypes.Task, "Paint the fence", "{}", cancellationToken: Ct)).Subject;
        await db.Get<AttributeService>().SetAsync(task.Urn, [new AttributeAssignment("context", "home")], Ct);
        await db.Get<TimerService>().ApplyAsync(task.Urn, TimerActions.Start, Ct);
        await db.Get<StatusService>().ChangeStatusAsync(task.Urn, "Completed", Ct);
        await db.Get<TriageService>().FlagAsync(task.Urn, Ct);
        await db.Get<ArchiveService>().ArchiveAsync(task.Urn, Ct);
        var habit = await db.HabitAsync(today, 3, 10);
        await db.Get<AdherenceService>().RecordAsync(habit.Urn, AdherenceResults.Followed, today, "an easy one", Ct);
        var capture = db.Get<CaptureService>();
        await capture.CaptureNoteAsync("A note", Ct);
        await capture.CaptureJournalAsync("A journal entry", Ct);
        await capture.CaptureVoiceTranscriptAsync("A dictated thought", Ct);
        var problem = (await db.Get<SubjectCreationService>().CreateAsync(SubjectTypes.Problem, "The gate sticks", "{}", cancellationToken: Ct)).Subject;
        await capture.CaptureIdeaSessionAsync(problem.Id, "The gate sticks", ["Plane the edge"], Ct);
        await db.Get<ActivityService>().LogAsync(new ActivityInput("Walked the dog"), Ct);

        var reader = TestReaders.Changes(db.ConnectionString);
        var everything = (await reader.GetChangesAsync(since, null, NoneOff, 500, cancellationToken: Ct));
        var core = (await reader.GetChangesAsync(since, null, AllOff, 500, cancellationToken: Ct));

        Assert.Equal(
            ["activity", "archive_change", "attribute_change", "idea_session", "journal", "note", "state_change", "triage", "voice"],
            everything.Select(r => r.Kind).Distinct().Order(StringComparer.Ordinal));
        Assert.Equal(
            everything.Select(r => $"{r.Kind} {r.Id}").Order(StringComparer.Ordinal),
            core.Select(r => $"{r.Kind} {r.Id}").Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Each_area_keeps_its_own_events_and_an_event_no_area_claims_waits_for_every_area()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var since = DateTimeOffset.UtcNow.AddMinutes(-1);

        // One made-up event per payload key and source each area claims: fixtures, not real data.
        var byArea = new Dictionary<string, List<Guid>>(StringComparer.Ordinal);
        await using (var owner = new NpgsqlConnection(db.ConnectionString))
        {
            await owner.OpenAsync(Ct);
            foreach (var area in McpAreas.All)
            {
                var ids = byArea[area.Name] = [];
                foreach (var key in area.PayloadKeys)
                {
                    var payload = JsonSerializer.Serialize(new Dictionary<string, object> { [key] = new { fixture = true } });
                    ids.Add(await InsertAsync(owner, "observation", "test", payload));
                }

                foreach (var source in area.Sources)
                {
                    ids.Add(await InsertAsync(owner, "observation", source, """{"fixture": true}"""));
                }
            }
        }

        // An item of an area's type takes its events with it, even the core's own kinds.
        var space = await db.Get<SpaceService>().AddAsync("Garage", 3, 3, Ct);
        await db.Get<ArchiveService>().ArchiveAsync(space.Urn, Ct);

        // A feature nobody has placed yet.
        Guid unplaced;
        await using (var owner = new NpgsqlConnection(db.ConnectionString))
        {
            await owner.OpenAsync(Ct);
            unplaced = await InsertAsync(owner, "measurement", "api", """{"brand_new_feature": {"fixture": true}}""");
        }

        var reader = TestReaders.Changes(db.ConnectionString);
        var everything = Ids((await reader.GetChangesAsync(since, null, NoneOff, 500, cancellationToken: Ct)));
        Assert.Contains(unplaced, everything);
        Assert.All(byArea.Values.SelectMany(ids => ids), id => Assert.Contains(id, everything));

        foreach (var area in McpAreas.All)
        {
            var shown = Ids((await reader.GetChangesAsync(since, null, new HashSet<string> { area.Name }, 500, cancellationToken: Ct)));
            Assert.All(byArea[area.Name], id => Assert.DoesNotContain(id, shown));
            Assert.All(byArea.Where(a => a.Key != area.Name).SelectMany(a => a.Value), id => Assert.Contains(id, shown));
            Assert.DoesNotContain(unplaced, shown);
        }

        var homeOff = (await reader.GetChangesAsync(since, null, new HashSet<string> { McpDomains.Home }, 500, cancellationToken: Ct));
        Assert.DoesNotContain(homeOff, r => r.SubjectId == space.Id);
        Assert.Contains((await reader.GetChangesAsync(since, null, NoneOff, 500, cancellationToken: Ct)), r => r.SubjectId == space.Id && r.Kind == "archive_change");

        // The rewards area claims a whole kind: the ledger.
        await db.OpenWalletAsync(1);
        var ledger = new[] { "ledger" };
        Assert.NotEmpty((await reader.GetChangesAsync(since, ledger, NoneOff, 500, cancellationToken: Ct)));
        Assert.Empty((await reader.GetChangesAsync(since, ledger, new HashSet<string> { McpDomains.Rewards }, 500, cancellationToken: Ct)));
        Assert.NotEmpty((await reader.GetChangesAsync(since, ledger, new HashSet<string> { McpDomains.Health }, 500, cancellationToken: Ct)));
    }

    private static HashSet<Guid> Ids(IEnumerable<ChangeRow> rows) => rows.Select(r => r.Id).ToHashSet();

    private async Task<Guid> InsertAsync(NpgsqlConnection owner, string kind, string source, string payload)
        => await owner.ExecuteScalarAsync<Guid>(new CommandDefinition(
            """
            INSERT INTO bsk.event (kind, provenance, occurred_at, recorded_at, source_id, payload)
            VALUES (@kind, 'observed', now(), clock_timestamp(), @source, @payload::jsonb)
            RETURNING id;
            """,
            new { kind, source, payload }, cancellationToken: Ct));
}
