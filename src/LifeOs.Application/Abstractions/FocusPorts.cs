using LifeOs.Domain;

namespace LifeOs.Application.Abstractions;

// The Primary focus (TODAY-1, D6, focus plan G1).

/// <summary>
/// The Primary focus through <c>bsk.v_focus</c> (migration 0074). Choosing one goes through
/// <c>FocusService</c>, which writes the event.
/// </summary>
public interface IFocusRepository
{
    /// <summary>The focus last chosen for <paramref name="level"/>, with its Goal as it is now, or null when none ever was.</summary>
    Task<ChosenFocus?> GetAsync(string level, CancellationToken cancellationToken = default);

    /// <summary>The subject's status (null when none was ever set) and whether it's archived.</summary>
    Task<(string? Status, bool Archived)> GetStateAsync(Guid subjectId, CancellationToken cancellationToken = default);
}
