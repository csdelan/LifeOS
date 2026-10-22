using LifeOs.Application.Abstractions;
using LifeOs.Domain;

namespace LifeOs.Application.Activity;

/// <summary>
/// What a sync covers (activity plan §3.3). By default: the last <see cref="OuraSyncService.RecentDays"/>
/// days through tomorrow, then up to <paramref name="HistoryWindows"/> more windows of history
/// until it's all in. With <paramref name="From"/>: every day from then through tomorrow
/// (<c>bsk oura sync --from</c>), and nothing else.
/// </summary>
public sealed record OuraSyncOptions(DateOnly? From = null, int HistoryWindows = OuraSyncService.HistoryWindowsPerSync);

/// <summary>What a sync did.</summary>
/// <param name="From">The first day of the recent window, or the <c>--from</c> day.</param>
/// <param name="To">The last day synced (tomorrow, in the home zone).</param>
/// <param name="Windows">How many windows were read, history included.</param>
/// <param name="Records">How many records Oura answered for those days.</param>
/// <param name="Written">How many versions were written: new and changed records, and withdrawals.</param>
/// <param name="Voided">How many of those withdraw a workout or sleep period Oura no longer has.</param>
/// <param name="HistoryFrom">The earliest day the history has been read back to.</param>
/// <param name="HistoryDone">The history backfill found where Oura's history starts.</param>
public sealed record OuraSyncResult(
    DateOnly From,
    DateOnly To,
    int Windows,
    int Records,
    int Written,
    int Voided,
    DateOnly? HistoryFrom,
    bool HistoryDone,
    DateTimeOffset SyncedAt);

/// <summary>
/// What the Activity page's sync did (<see cref="OuraSyncService.SyncForPageAsync"/>): the sync's
/// result, or null when it was skipped because LifeOS synced moments ago; and when LifeOS last
/// synced either way.
/// </summary>
public sealed record OuraPageSync(OuraSyncResult? Result, DateTimeOffset? LastSyncedAt)
{
    /// <summary>The sync ran (it wasn't skipped).</summary>
    public bool Ran => Result is not null;
}

/// <summary>
/// Reads Oura's five collections into LifeOS (ACTIVITY-1..4, D14, activity plan §3.2, §3.3).
/// <list type="bullet">
/// <item><b>The window:</b> the last 14 days through tomorrow, for every collection, which
/// catches a ring that synced late and edits made in the Oura app. Each request asks for two
/// days more on each side and LifeOS filters on <c>day</c> itself, since Oura's own filter
/// differs by collection.</item>
/// <item><b>History:</b> after the window, the sync reads back <see cref="HistoryWindowDays"/>
/// days at a time, <see cref="HistoryWindowsPerSync"/> windows per sync, until a window holds
/// nothing (or it reaches <see cref="HistoryFloor"/>). Where it got to is kept on the
/// connection, so the next sync carries on.</item>
/// <item><b>Versions:</b> a record gets a new version only when its trimmed content differs
/// from the newest LifeOS holds. A workout or sleep period LifeOS holds on a day in the window
/// that Oura no longer answers gets a voided version; if it comes back, a new version restores
/// it. Daily records are never withdrawn.</item>
/// <item><b>One at a time:</b> each window is read and written under a lock every sync
/// shares, so a second sync waits, then finds nothing new.</item>
/// <item><b>Tokens:</b> a token Oura refuses (401) is refreshed once and the request tried
/// again; refused again, the connection needs reconnecting.</item>
/// </list>
/// The outcome is kept on the connection: when it last synced, or why it failed.
/// </summary>
public sealed class OuraSyncService(
    OuraConnectionService connection,
    IOuraClient? client,
    IActivityRepository activity,
    IIntegrationRepository integrations,
    IHomeCalendar calendar,
    IClock clock,
    string service = Integrations.Oura)
{
    /// <summary>How many days before today every sync reads again.</summary>
    public const int RecentDays = 14;

    /// <summary>How many days more each request asks for, on each side of its window.</summary>
    public const int PadDays = 2;

    /// <summary>The days in one window of history (and of a <c>--from</c> sync).</summary>
    public const int HistoryWindowDays = 180;

    /// <summary>How many windows of history a sync reads, unless told otherwise.</summary>
    public const int HistoryWindowsPerSync = 4;

    /// <summary>The earliest day a sync reads: before the first Oura Ring with an API.</summary>
    public static readonly DateOnly HistoryFloor = new(2015, 1, 1);

    /// <summary>How recent a sync lets the page's sync on opening be skipped (activity plan §3.3).</summary>
    public static readonly TimeSpan PageSyncInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The Activity page's sync. On opening the page it's skipped when Oura is connected and
    /// last synced under <see cref="PageSyncInterval"/> ago; with <paramref name="force"/>
    /// (Sync now) it always runs. A sync that runs is <see cref="SyncAsync"/> with its
    /// defaults, and throws as it does.
    /// </summary>
    public async Task<OuraPageSync> SyncForPageAsync(bool force, CancellationToken cancellationToken = default)
    {
        if (!force
            && await integrations.GetAsync(service, cancellationToken) is { Connected: true, NeedsReconnect: false, LastSyncedAt: { } last })
        {
            var age = clock.UtcNow - last;
            if (age >= TimeSpan.Zero && age < PageSyncInterval)
            {
                return new OuraPageSync(null, last);
            }
        }

        var result = await SyncAsync(cancellationToken: cancellationToken);
        return new OuraPageSync(result, result.SyncedAt);
    }

    /// <summary>
    /// Finishes connecting (<see cref="OuraConnectionService.ConnectAsync"/>) and runs the first
    /// sync. A sync that fails leaves the connection made: its error is kept on the connection
    /// for the page to show. Answers the new status.
    /// </summary>
    public async Task<OuraStatus> ConnectAsync(string code, string state, CancellationToken cancellationToken = default)
    {
        await connection.ConnectAsync(code, state, cancellationToken);
        try
        {
            await SyncAsync(cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is OuraConnectionException or OuraUnavailableException)
        {
            // Kept on the connection as its last sync error; connecting itself succeeded.
        }

        return await connection.GetStatusAsync(cancellationToken);
    }

    /// <summary>
    /// Syncs (see the class summary). Throws <see cref="OuraConnectionException"/> when Oura
    /// isn't set up or connected, needs reconnecting, or refused access, and
    /// <see cref="OuraUnavailableException"/> when Oura is down or limiting requests; either is
    /// also kept on the connection. What earlier windows wrote stays.
    /// </summary>
    public async Task<OuraSyncResult> SyncAsync(OuraSyncOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new OuraSyncOptions();
        if (options.HistoryWindows < 0)
        {
            throw new ArgumentException("A sync reads zero or more windows of history.");
        }

        var oura = client ?? throw new OuraConnectionException(OuraConnectionService.NotSetUp);
        try
        {
            var token = new Token(await connection.GetAccessTokenAsync(cancellationToken));
            var today = await calendar.DateAtAsync(clock.UtcNow, cancellationToken);
            var to = today.AddDays(1);
            var tally = new Tally();

            DateOnly from;
            DateOnly? historyFrom;
            var historyDone = false;
            if (options.From is { } explicitFrom)
            {
                if (explicitFrom < HistoryFloor || explicitFrom > today)
                {
                    throw new ArgumentException(
                        $"Sync from a day between {OuraPayloads.Text(HistoryFloor)} and today ({OuraPayloads.Text(today)}).");
                }

                from = explicitFrom;
                for (var start = from; start <= to; start = start.AddDays(HistoryWindowDays))
                {
                    var end = Min(start.AddDays(HistoryWindowDays - 1), to);
                    await SyncWindowAsync(oura, token, start, end, tally, cancellationToken);
                }

                historyFrom = from;
            }
            else
            {
                from = today.AddDays(-RecentDays);
                await SyncWindowAsync(oura, token, from, to, tally, cancellationToken);

                var stored = await integrations.GetAsync(service, cancellationToken);
                historyDone = stored?.HistoryDone ?? false;
                var reached = Min(stored?.HistoryFrom ?? from, from);
                for (var i = 0; i < options.HistoryWindows && !historyDone; i++)
                {
                    if (reached <= HistoryFloor)
                    {
                        historyDone = true;
                        break;
                    }

                    var end = reached.AddDays(-1);
                    var start = Max(end.AddDays(-(HistoryWindowDays - 1)), HistoryFloor);
                    var found = await SyncWindowAsync(oura, token, start, end, tally, cancellationToken);
                    reached = start;
                    historyDone = found == 0 || start <= HistoryFloor;
                }

                historyFrom = reached;
            }

            var syncedAt = clock.UtcNow;
            await integrations.RecordSyncAsync(service, syncedAt, historyFrom, historyDone, cancellationToken);
            var state = await integrations.GetAsync(service, cancellationToken);
            return new OuraSyncResult(
                from, to, tally.Windows, tally.Records, tally.Written, tally.Voided,
                state?.HistoryFrom ?? historyFrom, state?.HistoryDone ?? historyDone, syncedAt);
        }
        catch (Exception ex) when (ex is OuraConnectionException or OuraUnavailableException)
        {
            await integrations.RecordSyncErrorAsync(service, ex.Message, CancellationToken.None);
            throw;
        }
    }

    // Reads one window of every collection and writes what changed, under the sync lock.
    // Answers how many records Oura has on the window's days.
    private async Task<int> SyncWindowAsync(
        IOuraClient oura, Token token, DateOnly from, DateOnly to, Tally tally, CancellationToken cancellationToken)
    {
        var start = from.AddDays(-PadDays);
        var end = to.AddDays(PadDays);

        await using var sync = await activity.BeginSyncAsync(cancellationToken);
        var days = await FetchAsync(t => oura.GetDailyActivityAsync(t, start, end, cancellationToken), token, cancellationToken);
        var workouts = await FetchAsync(t => oura.GetWorkoutsAsync(t, start, end, cancellationToken), token, cancellationToken);
        var sleepScores = await FetchAsync(t => oura.GetDailySleepAsync(t, start, end, cancellationToken), token, cancellationToken);
        var readiness = await FetchAsync(t => oura.GetDailyReadinessAsync(t, start, end, cancellationToken), token, cancellationToken);
        var periods = await FetchAsync(t => oura.GetSleepPeriodsAsync(t, start, end, cancellationToken), token, cancellationToken);

        var found = 0;
        found += await ApplyAsync(sync, OuraCollections.DailyActivity, days.Select(OuraPayloads.Activity), from, to, tally, cancellationToken);
        found += await ApplyAsync(sync, OuraCollections.Workout, workouts.Select(OuraPayloads.Workout), from, to, tally, cancellationToken);
        found += await ApplyAsync(sync, OuraCollections.DailySleep, sleepScores.Select(OuraPayloads.SleepScore), from, to, tally, cancellationToken);
        found += await ApplyAsync(sync, OuraCollections.DailyReadiness, readiness.Select(OuraPayloads.Readiness), from, to, tally, cancellationToken);
        found += await ApplyAsync(sync, OuraCollections.Sleep, periods.Select(OuraPayloads.SleepPeriod), from, to, tally, cancellationToken);
        await sync.CommitAsync(cancellationToken);

        tally.Windows++;
        tally.Records += found;
        return found;
    }

    private static async Task<int> ApplyAsync(
        IOuraSyncLock sync, OuraCollection collection, IEnumerable<OuraRecord> answered, DateOnly from, DateOnly to,
        Tally tally, CancellationToken cancellationToken)
    {
        var all = answered.ToList();

        // Oura answering a record twice: the later one stands.
        var inWindow = all
            .Where(r => r.Day >= from && r.Day <= to)
            .GroupBy(r => r.Key, StringComparer.Ordinal)
            .Select(g => g.Last())
            .ToList();
        var answeredKeys = all.Select(r => r.Key).ToHashSet(StringComparer.Ordinal);
        var heads = (await sync.GetHeadsAsync(collection, inWindow.Select(r => r.Key).ToList(), from, to, cancellationToken))
            .ToDictionary(h => h.Key, StringComparer.Ordinal);

        var versions = new List<OuraVersion>();
        foreach (var record in inWindow)
        {
            var head = heads.GetValueOrDefault(record.Key);
            if (head is null || head.Hash != record.Hash)
            {
                versions.Add(Version(record, head?.EventId));
            }
        }

        var voided = 0;
        if (collection.Voidable)
        {
            // Held on a day in the window, but not in Oura's answer for it (padding included):
            // deleted in the Oura app.
            var gone = heads.Values
                .Where(h => !h.Voided && h.Day is { } day && day >= from && day <= to && !answeredKeys.Contains(h.Key))
                .OrderBy(h => h.Key, StringComparer.Ordinal);
            foreach (var head in gone)
            {
                versions.Add(Version(OuraPayloads.Voided(collection, head.Key, head.Day!.Value, head.OccurredAt), head.EventId));
                voided++;
            }
        }

        if (versions.Count > 0)
        {
            tally.Written += await sync.AppendAsync(versions, cancellationToken);
            tally.Voided += voided;
        }

        return inWindow.Count;
    }

    private static OuraVersion Version(OuraRecord record, Guid? replaces)
        => new(record.Collection.Kind, record.OccurredAt, OuraPayloads.ExternalId(record, replaces), record.PayloadJson);

    // A token Oura refuses is refreshed once per sync; refused again, only reconnecting helps.
    private async Task<T> FetchAsync<T>(Func<string, Task<T>> fetch, Token token, CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                return await fetch(token.Value);
            }
            catch (OuraAccessDeniedException ex) when (ex.Status == 401)
            {
                if (token.Refreshed)
                {
                    const string reason = "Oura refused LifeOS's access token even after refreshing it.";
                    await connection.MarkReconnectAsync(reason, CancellationToken.None);
                    throw new OuraConnectionException($"{reason} Reconnect Oura on the Activity page.", ex);
                }

                token.Value = await connection.RefreshAccessTokenAsync(cancellationToken);
                token.Refreshed = true;
            }
        }
    }

    private static DateOnly Min(DateOnly a, DateOnly b) => a <= b ? a : b;

    private static DateOnly Max(DateOnly a, DateOnly b) => a >= b ? a : b;

    private sealed class Token(string value)
    {
        public string Value { get; set; } = value;
        public bool Refreshed { get; set; }
    }

    private sealed class Tally
    {
        public int Windows { get; set; }
        public int Records { get; set; }
        public int Written { get; set; }
        public int Voided { get; set; }
    }
}
