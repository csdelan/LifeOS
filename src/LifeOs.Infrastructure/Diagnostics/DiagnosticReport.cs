using System.Text.Json;

namespace LifeOs.Infrastructure.Diagnostics;

/// <summary>The outcome of a <c>bsk check</c> run: every diagnostic that ran and its findings.</summary>
/// <param name="Diagnostics">One entry per diagnostic executed, in report order.</param>
public sealed record DiagnosticReport(IReadOnlyList<DiagnosticResult> Diagnostics)
{
    /// <summary>Total findings across every diagnostic.</summary>
    public int FindingCount => Diagnostics.Sum(d => d.Findings.Count);
}

/// <summary>One diagnostic's contribution to the report.</summary>
/// <param name="Name">The diagnostic slug.</param>
/// <param name="Title">The rule it represents (the "why").</param>
/// <param name="Findings">Its findings, in the order the query returned them.</param>
public sealed record DiagnosticResult(string Name, string Title, IReadOnlyList<Finding> Findings);

/// <summary>
/// One thing a diagnostic flagged: the subject it concerns, a specific one-line
/// explanation, and the evidence rows that triggered it. Every field is
/// populated — a finding is never unexplained.
/// </summary>
/// <param name="Subject">The subject the finding is about.</param>
/// <param name="Summary">Specific, one line: why this instance fired.</param>
/// <param name="Evidence">
/// A JSON array of the source rows that triggered the finding (may be empty when
/// the cause is an absence). Passed through untouched by <c>--json</c>.
/// </param>
public sealed record Finding(FindingSubject Subject, string Summary, JsonElement Evidence);

/// <summary>The subject a finding concerns.</summary>
public sealed record FindingSubject(Guid Id, string Urn, string Type, string Title);
