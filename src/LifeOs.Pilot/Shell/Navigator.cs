namespace LifeOs.Pilot.Shell;

/// <summary>
/// Cross-view navigation: open a destination (and optionally select a subject)
/// without the views knowing about <see cref="MainForm"/>. Also carries the two
/// shell-wide signals: a timer changed (TASKS-2), and a one-line status message.
/// </summary>
internal sealed class Navigator
{
    public event Action<string, Guid?>? Navigated;

    /// <summary>A Task timer was started, paused, resumed, stopped or completed.</summary>
    public event Action? TimersChanged;

    /// <summary>A short confirmation for the shell's status line (the Pilot's toast).</summary>
    public event Action<string>? Notified;

    public void SignalTimersChanged()
        => TimersChanged?.Invoke();

    public void Notify(string message)
        => Notified?.Invoke(message);

    public void Go(string destination, Guid? subjectId = null)
        => Navigated?.Invoke(destination, subjectId);

    public void OpenSubject(string type, Guid id)
        => Go(PilotVocab.DestinationForType(type), id);
}

/// <summary>A primary destination the shell can ask to reload, persist, and leave safely.</summary>
internal interface IPilotView
{
    void Reload();

    bool IsDirty { get; }

    /// <summary>Persist in-progress edits. Returns false when validation fails (stay put).</summary>
    bool TrySaveEdits();

    void DiscardEdits();

    void SaveViewState(ViewStateStore store);

    void RestoreViewState(ViewStateStore store);

    void SelectSubject(Guid id);
}
