using LifeOs.Application.Journals;

namespace LifeOs.Api.Hosting;

/// <summary>
/// The periodic half of the pointer reconcile (D19): every minute, writes the pointers whose own
/// write didn't land, with who saved them. It needs only <c>bsk</c>, so it runs with no one
/// signed in. Versions with no pointer at all (the server stopped between the two writes) are
/// found by the next save to their journal, or by <c>POST /api/journals/reconcile</c>.
/// </summary>
public sealed class JournalPointerRetry(SuperJournalService journals, ILogger<JournalPointerRetry> logger) : BackgroundService
{
    public static readonly TimeSpan Every = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Every);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                if (journals.PendingCount == 0)
                {
                    continue;
                }

                var written = await journals.RetryPendingAsync(stoppingToken);
                if (written > 0)
                {
                    logger.LogInformation(
                        "Wrote {Written} journal pointer(s) that were waiting; {Left} still wait", written, journals.PendingCount);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }
}
