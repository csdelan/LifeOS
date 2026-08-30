using System.Text.Json.Nodes;
using LifeOs.Domain;

namespace LifeOs.Application.Subjects;

/// <summary>
/// Records a Decision as a closed historical fact carrying a <em>prediction</em>
/// (<c>bsk decide</c>). A Decision may <c>results_in</c> any subject — or nothing:
/// deliberate non-action must be recordable — and may <c>supersede</c> a prior
/// Decision to leave a reversal trail. A Decision carries a prediction
/// (right/wrong); it is deliberately not given an ongoing obligation — that is a
/// Commitment's job.
/// </summary>
public sealed class DecisionService(SubjectService subjects, RelationService relations)
{
    public async Task<DecisionResult> DecideAsync(
        DecisionInput input, CancellationToken cancellationToken = default)
    {
        var attributes = new JsonObject();
        if (input.Alternatives is { Count: > 0 } alternatives)
        {
            attributes["alternatives"] =
                new JsonArray(alternatives.Select(a => (JsonNode?)JsonValue.Create(a)).ToArray());
        }

        AddIfPresent(attributes, "rationale", input.Rationale);
        AddIfPresent(attributes, "expected_outcome", input.ExpectedOutcome);
        AddIfPresent(attributes, "confidence", input.Confidence);
        AddIfPresent(attributes, "next_review_at", input.ReviewAt);

        var decision = await subjects.CreateAsync(
            SubjectTypes.Decision, input.Title, attributes.ToJsonString(), cancellationToken: cancellationToken);

        // A Decision may result in a subject, or in nothing at all.
        Guid? resultsIn = null;
        if (!string.IsNullOrWhiteSpace(input.ResultsIn))
        {
            var target = await subjects.ResolveAsync(input.ResultsIn, cancellationToken);
            await relations.LinkResolvedAsync(decision, RelationKinds.ResultsIn, target, cancellationToken);
            resultsIn = target.Id;
        }

        // A Decision may supersede a prior Decision (reversal trail).
        Guid? supersedes = null;
        if (!string.IsNullOrWhiteSpace(input.Supersedes))
        {
            var prior = await subjects.ResolveAsync(input.Supersedes, cancellationToken);
            await relations.LinkResolvedAsync(decision, RelationKinds.Supersedes, prior, cancellationToken);
            supersedes = prior.Id;
        }

        return new DecisionResult(decision, resultsIn, supersedes);
    }

    private static void AddIfPresent(JsonObject target, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            target[key] = value.Trim();
        }
    }
}

/// <summary>The inputs to a decision: a prediction, not an obligation.</summary>
public sealed record DecisionInput(
    string Title,
    IReadOnlyList<string>? Alternatives = null,
    string? Rationale = null,
    string? ExpectedOutcome = null,
    string? Confidence = null,
    string? ReviewAt = null,
    string? ResultsIn = null,
    string? Supersedes = null);

/// <summary>The outcome of a decision: the new Decision subject and its edges (if any).</summary>
public sealed record DecisionResult(SubjectRef Decision, Guid? ResultsInSubjectId, Guid? SupersedesSubjectId);
