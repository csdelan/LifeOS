namespace LifeOs.Pilot.Shell;

/// <summary>
/// Cross-view navigation: open a destination (and optionally select a subject)
/// without the views knowing about <see cref="MainForm"/>.
/// </summary>
internal sealed class Navigator
{
    public event Action<string, Guid?>? Navigated;

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
