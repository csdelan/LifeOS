using LifeOs.Application.Abstractions;
using LifeOs.Domain;

namespace LifeOs.Application.Subjects;

/// <summary>
/// Attaches an existing captured event to a subject with an <c>event → subject</c>
/// edge — the write path behind <c>bsk relate</c>, and how the Inbox files a note or
/// journal against the Project/Person/… it is about. Defaults to <c>concerns</c>; the
/// commitment-oriented <c>evidences</c>/<c>violates</c> are allowed too. Also what
/// finally makes the neglect diagnostic's "last touched" clock real.
/// </summary>
public sealed class RelateService(
    SubjectService subjects, IEventReader events, ISubjectEventRepository edges)
{
    public async Task<RelateResult> RelateAsync(
        Guid eventId, string subjectReference, string relation = SubjectEventRelations.Concerns,
        CancellationToken cancellationToken = default)
    {
        if (!SubjectEventRelations.All.Contains(relation))
        {
            throw new ArgumentException(
                $"Unknown relation '{relation}'. Expected one of: {string.Join(", ", SubjectEventRelations.All)}.",
                nameof(relation));
        }

        var sourceEvent = await events.FindAsync(eventId, cancellationToken)
            ?? throw new InvalidOperationException($"No event found with id '{eventId}'.");

        var subject = await subjects.ResolveAsync(subjectReference, cancellationToken);

        var created = await edges.CreateEdgeAsync(
            eventId, subject.Id, relation, Provenances.Declared, cancellationToken);

        return new RelateResult(sourceEvent.Id, subject, relation, created);
    }
}

/// <summary>The outcome of a relate: the event, the subject, the relation, and whether the edge is new.</summary>
public sealed record RelateResult(Guid EventId, SubjectRef Subject, string Relation, bool Created);
