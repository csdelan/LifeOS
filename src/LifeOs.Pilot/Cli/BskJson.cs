namespace LifeOs.Pilot.Cli;

// What the Pilot reads from bsk --json. The Pilot keeps its own copies, since it doesn't reference
// the kernel, so the tests compile this file too and run each verb: every property here must be in
// what bsk prints (BskJsonTests, code review CR89).

/// <summary>The structured result of <c>bsk new --json</c>.</summary>
public sealed class CreatedSubject
{
    public Guid Id { get; set; }
    public string Urn { get; set; } = "";
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
}

/// <summary>What <c>bsk journal --json</c> says of the entry it added: its event.</summary>
internal sealed class JournalCapture
{
    public Guid Id { get; set; }
}

/// <summary>The subset of <c>bsk timer … --json</c> the Pilot uses.</summary>
internal sealed class TimerResultJson
{
    public long TotalSeconds { get; set; }
    public List<PausedJson> Paused { get; set; } = [];
    public bool Completed { get; set; }
}

/// <summary>A Task whose timer the action paused.</summary>
internal sealed class PausedJson
{
    public string Urn { get; set; } = "";
    public string Title { get; set; } = "";
}
