using LifeOs.Application.Abstractions;

namespace LifeOs.Application.Subjects;

/// <summary>
/// Runs what a write does after its own commit (code review CR113): the reward reconcile, and a
/// repeating Task's follow-ups. The write has landed by then, so a follow-up that fails is told to
/// the <see cref="IFollowUpLog"/> and answered as pending, never thrown: a throw reads as "Nothing
/// was written", and a retry writes it twice. What it left undone, the catch-up the API runs as it
/// starts (repeating Tasks and rewards) and the reconcile sweep heal. Each runs to the end whatever
/// the caller does, as filing an Inbox capture does (code review CR63).
/// </summary>
public sealed class AfterCommit(IFollowUpLog log)
{
    /// <summary>
    /// Runs <paramref name="work"/>, the follow-up named <paramref name="followUp"/> to the write
    /// about <paramref name="about"/>; when it fails, answers <paramref name="fallback"/>, pending.
    /// </summary>
    public async Task<FollowUp<T>> RunAsync<T>(
        string followUp, string about, Func<CancellationToken, Task<T>> work, T fallback)
    {
        ArgumentNullException.ThrowIfNull(work);
        try
        {
            return new FollowUp<T>(await work(CancellationToken.None), Pending: false);
        }
        catch (Exception e)
        {
            log.Failed(followUp, about, e);
            return new FollowUp<T>(fallback, Pending: true);
        }
    }
}

/// <summary>What a follow-up answered, or its fallback when it failed and is left to the catch-up.</summary>
public sealed record FollowUp<T>(T Value, bool Pending);
