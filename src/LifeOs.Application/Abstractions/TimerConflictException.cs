namespace LifeOs.Application.Abstractions;

/// <summary>
/// Thrown when a timer action was decided against a state that changed before it
/// could be written — typically the same timer driven from two windows at once.
/// Nothing was written; reading the timer again and retrying is always safe.
/// </summary>
public sealed class TimerConflictException(string title, string expectedState, string actualState)
    : Exception(
        $"The timer on '{title}' is now {actualState}, not {expectedState} — it was changed elsewhere. " +
        "Refresh and try again.")
{
    public string ExpectedState { get; } = expectedState;

    public string ActualState { get; } = actualState;
}
