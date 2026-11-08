namespace LifeOs.Domain;

/// <summary>
/// The lab panels a test can be on (MEASURE-4, measurements plan §4.9), a metric's
/// <c>panel</c> attribute: the starter list's three, and <see cref="Other"/> for a test added from
/// the visit form.
/// </summary>
public static class LabPanels
{
    public const string Cmp = "cmp";
    public const string Lipids = "lipids";
    public const string A1c = "a1c";
    public const string Other = "other";

    /// <summary>Every panel, in the order Lab results lists them.</summary>
    public static readonly IReadOnlyList<string> All = [Cmp, Lipids, A1c, Other];

    /// <summary>A panel's heading: "CMP", "Lipid panel", "A1c", "Other tests".</summary>
    public static string Name(string? panel) => panel switch
    {
        Cmp => "CMP",
        Lipids => "Lipid panel",
        A1c => "A1c",
        _ => "Other tests"
    };

    /// <summary>Where a panel sits in <see cref="All"/>; past the end for a metric on none.</summary>
    public static int Order(string? panel)
    {
        for (var i = 0; i < All.Count; i++)
        {
            if (All[i] == panel)
            {
                return i;
            }
        }

        return All.Count;
    }
}

/// <summary>A lab visit's report: the artifact holding it, and its file name, type and size.</summary>
public sealed record LabReport(Guid ArtifactId, string? Filename, string ContentType, long ByteSize);

/// <summary>
/// A lab visit (MEASURE-4, measurements plan §4.9): the newest version of a <c>lab_visit</c>
/// event, read through <c>bsk.v_lab_visit</c>. <paramref name="CollectedAt"/> is when the sample
/// was taken; <paramref name="CollectedOn"/> and <paramref name="CollectedLocal"/> read it in the
/// home zone. Its results are the readings whose <c>VisitId</c> is its id.
/// </summary>
public sealed record LabVisit(
    Guid Id,
    string? Lab,
    string? Note,
    DateTimeOffset CollectedAt,
    DateOnly CollectedOn,
    DateTime CollectedLocal,
    LabReport? Report,
    DateTimeOffset FirstLoggedAt,
    DateTimeOffset LoggedAt,
    int Versions,
    Guid EventId);

/// <summary>
/// The limits on lab visits (measurements plan §4.9), shared by the service, the CLI and the API
/// so they refuse the same things in the same words.
/// </summary>
public static class LabLimits
{
    /// <summary>The payload key a visit's events carry.</summary>
    public const string VisitKey = "lab_visit";

    public const int MaxLab = 60;

    /// <summary>The largest report accepted: 10 MiB.</summary>
    public const int MaxReportBytes = 10 * 1024 * 1024;

    /// <summary>A lab's name as kept: null when blank, else trimmed, one line, at most 60 characters.</summary>
    public static string? CheckLab(string? lab) => MetricLimits.CheckLine(lab, MaxLab, "A lab's name");

    /// <summary>A visit's note as kept: null when blank, else trimmed, one line, at most 140 characters.</summary>
    public static string? CheckNote(string? note) => MetricLimits.CheckNote(note, "A visit's note");
}
