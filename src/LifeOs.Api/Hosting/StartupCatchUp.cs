using LifeOs.Application.Rewards;
using LifeOs.Application.Subjects;

namespace LifeOs.Api.Hosting;

/// <summary>
/// Catches up the work that falls due as days pass, as the API starts and before it answers
/// anything (code review CR57, Chris's choice A): the repetitions repeating Tasks are owed, then
/// the food days that closed themselves, the activity days already synced and any rank-up reached.
/// Fly stops the machine when it's idle, so the first request of most days starts it, and an agent
/// reading <c>/mcp</c>, which never opens the pages that catch up, sees today's chores and
/// yesterday's XP. The pages still catch up when they open, for a machine that stayed up past
/// midnight. It doesn't sync Oura or heal every item (CR46): those stay with the Activity page and
/// <c>bsk rewards reconcile</c>. Both catch-ups are safe to repeat.
/// <para>
/// It runs in <see cref="StartingAsync"/>, which the host calls before it starts any hosted
/// service, the web server among them. A catch-up that fails, or takes longer than
/// <see cref="Limit"/>, is logged and the API starts anyway. <c>CatchUp:OnStart</c> false turns it off.
/// </para>
/// </summary>
internal sealed class StartupCatchUp(
    TaskSeriesService series, SettlementService settlement, ILogger<StartupCatchUp> logger) : IHostedLifecycleService
{
    public const string ConfigurationKey = "CatchUp:OnStart";

    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(20);

    /// <summary>Whether <c>CatchUp:OnStart</c> asks for it: on unless it's false. Any other value fails startup.</summary>
    public static bool Enabled(string? value)
        => string.IsNullOrWhiteSpace(value)
            || (bool.TryParse(value.Trim(), out var on)
                ? on
                : throw new InvalidOperationException($"{ConfigurationKey} is true or false, not '{value}'."));

    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(Limit);

        await RunAsync("repeating Tasks", async ct =>
        {
            var result = await series.SettleAsync(ct);
            return $"{result.Made.Count} repetitions made for {result.Series} repeating Tasks";
        }, limit.Token, cancellationToken);
        await RunAsync("rewards", async ct =>
        {
            var earned = await settlement.SettleAsync(cancellationToken: ct);
            return $"{earned.Awards.Count} entries written";
        }, limit.Token, cancellationToken);
    }

    // Each catch-up on its own, so one that fails doesn't keep the other from running.
    private async Task RunAsync(
        string what, Func<CancellationToken, Task<string>> catchUp, CancellationToken limit, CancellationToken stopping)
    {
        try
        {
            logger.LogInformation("Caught up {What} at start: {Result}.", what, await catchUp(limit));
        }
        catch (Exception e) when (!stopping.IsCancellationRequested)
        {
            logger.LogWarning(e, "Couldn't catch up {What} at start; the pages will when they open.", what);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
