using System.Text.Json.Nodes;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;

namespace LifeOs.Application.Subjects;

/// <summary>
/// Adds and removes People-association links on an item — the write path behind
/// <c>bsk involve</c>. An association is a soft reference to a Person subject plus a
/// role (attendee / owner / assignee / waiting-for / involves), stored in
/// <c>attributes.people</c>. It is a property, not an alignment edge, and supports
/// several people per item. The whole array is rewritten on each change (append-only
/// does not apply to the mutable attributes bag).
/// </summary>
public sealed class PeopleService(SubjectService subjects, ISubjectRepository repository)
{
    public async Task<PersonAssociationResult> AddAsync(
        string subjectReference, string personReference, string role,
        CancellationToken cancellationToken = default)
    {
        var normalizedRole = NormalizeRole(role);
        var subject = await subjects.ResolveAsync(subjectReference, cancellationToken);
        var person = await subjects.ResolveAsync(personReference, cancellationToken);
        if (person.Type != SubjectTypes.Person)
        {
            throw new InvalidOperationException(
                $"'{person.Urn}' is a {person.Type}, not a Person; only People can be involved.");
        }

        var people = await LoadAsync(subject.Id, cancellationToken);
        var already = people.Any(e => Matches(e, person.Urn, normalizedRole));
        if (!already)
        {
            people.Add(new JsonObject { ["person"] = person.Urn, ["role"] = normalizedRole });
            await SaveAsync(subject.Id, subjectReference, people, cancellationToken);
        }

        return new PersonAssociationResult(subject, person, normalizedRole, Changed: !already);
    }

    public async Task<PersonAssociationResult> RemoveAsync(
        string subjectReference, string personReference, string role,
        CancellationToken cancellationToken = default)
    {
        var normalizedRole = NormalizeRole(role);
        var subject = await subjects.ResolveAsync(subjectReference, cancellationToken);
        var person = await subjects.ResolveAsync(personReference, cancellationToken);

        var people = await LoadAsync(subject.Id, cancellationToken);
        var removed = people.RemoveAll(e => Matches(e, person.Urn, normalizedRole));
        if (removed > 0)
        {
            await SaveAsync(subject.Id, subjectReference, people, cancellationToken);
        }

        return new PersonAssociationResult(subject, person, normalizedRole, Changed: removed > 0);
    }

    private static string NormalizeRole(string role)
    {
        var normalized = (role ?? "").Trim().ToLowerInvariant();
        if (!PersonRoles.All.Contains(normalized))
        {
            throw new ArgumentException(
                $"Unknown role '{role}'. Expected one of: {string.Join(", ", PersonRoles.All)}.", nameof(role));
        }

        return normalized;
    }

    private static bool Matches(JsonNode? entry, string personUrn, string role)
        => entry is JsonObject o
           && string.Equals((string?)o["person"], personUrn, StringComparison.Ordinal)
           && string.Equals((string?)o["role"], role, StringComparison.Ordinal);

    private async Task<List<JsonNode>> LoadAsync(Guid subjectId, CancellationToken cancellationToken)
    {
        var raw = await repository.GetAttributeValueAsync(subjectId, "people", cancellationToken);
        if (string.IsNullOrWhiteSpace(raw) || JsonNode.Parse(raw) is not JsonArray array)
        {
            return [];
        }

        // Detach each element from the parsed array so it can be re-parented on save.
        return array.Select(n => n?.DeepClone()).Where(n => n is not null).Cast<JsonNode>().ToList();
    }

    private async Task SaveAsync(
        Guid subjectId, string reference, List<JsonNode> people, CancellationToken cancellationToken)
    {
        var patch = new JsonObject { ["people"] = new JsonArray([.. people]) };
        var updated = await repository.UpdateAttributesAsync(
            subjectId, patch.ToJsonString(), [], cancellationToken);
        if (!updated)
        {
            throw new SubjectNotFoundException(reference);
        }
    }
}

/// <summary>The outcome of a People-association change: the item, the person, the role, and whether it changed.</summary>
public sealed record PersonAssociationResult(SubjectRef Subject, SubjectRef Person, string Role, bool Changed);
