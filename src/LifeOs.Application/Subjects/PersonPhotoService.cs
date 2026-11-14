using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Photos;
using LifeOs.Domain;

namespace LifeOs.Application.Subjects;

/// <summary>
/// A Person's photo (GEN-15), human or AI agent: the one writer of <c>attributes.photo</c>, behind
/// <c>bsk person photo</c> and <c>/api/people/{id}/photo</c>. The photo is an artifact with bytes,
/// stored through <see cref="IArtifactBlobStore"/> (Supabase Storage when the host has it, else the
/// database) by a <c>declared</c> <c>observation</c> event whose payload names the Person
/// (<c>{person_photo: {person}}</c>); the attribute then holds the artifact's id. The event has no
/// subject and no triage flag, so it shows nowhere but on the Person, as a food entry's photo does.
/// <para>
/// A new photo replaces the one before; removing one clears the attribute, and the bytes stay in
/// the record. What a photo can be is <see cref="PhotoChecks"/>'s, shared with food and the home's
/// spaces, and a host that must not keep photos (<paramref name="enabled"/> false) refuses every
/// upload in words. Removing still works there.
/// </para>
/// </summary>
public sealed class PersonPhotoService(
    IArtifactBlobStore blobs, SubjectService subjects, ISubjectRepository repository, IClock clock,
    EventSource eventSource, bool enabled = true)
{
    /// <summary>The attribute that holds the photo's artifact id.</summary>
    public const string AttributeKey = "photo";

    /// <summary>The payload key of the event that stores a photo's bytes.</summary>
    public const string PayloadKey = "person_photo";

    /// <summary>True when this host takes photos.</summary>
    public bool Enabled => enabled;

    /// <summary>
    /// Makes <paramref name="bytes"/> the photo of the Person named by <paramref name="reference"/>,
    /// in place of any it had. Refused, with nothing stored, for anything but a Person or bytes
    /// that aren't a photo. The same bytes again reuse the artifact already stored.
    /// </summary>
    public async Task<PersonPhoto> SetAsync(
        string reference, byte[] bytes, string? filename = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (!enabled)
        {
            throw new InvalidOperationException(PhotoChecks.NotSetUp);
        }

        var person = await ResolvePersonAsync(reference, cancellationToken);
        var contentType = PhotoChecks.Check(bytes);

        var now = clock.UtcNow;
        var stored = await blobs.PutAsync(new NewBinaryCapture(
            Kind: EventKinds.Observation,
            Provenance: Provenances.Declared,
            OccurredAt: now,
            RecordedAt: now,
            SourceId: eventSource.Id,
            Sha256: Convert.ToHexStringLower(SHA256.HashData(bytes)),
            Bytes: bytes,
            ContentType: contentType,
            ByteSize: bytes.Length,
            Filename: PhotoChecks.CleanName(filename),
            PayloadJson: JsonSerializer.Serialize(new Dictionary<string, object>
            {
                [PayloadKey] = new { person = person.Id },
            })), cancellationToken);

        var patch = new JsonObject { [AttributeKey] = stored.ArtifactId.ToString() }.ToJsonString();
        if (!await repository.UpdateAttributesAsync(person.Id, patch, [], cancellationToken))
        {
            // The Person resolved a moment ago; a missing row now is a real fault.
            throw new SubjectNotFoundException(person.Urn);
        }

        return new PersonPhoto(person, stored.ArtifactId);
    }

    /// <summary>
    /// Removes the photo of the Person named by <paramref name="reference"/>; its bytes stay in the
    /// record. A Person with no photo is left as it is.
    /// </summary>
    public async Task<PersonPhoto> RemoveAsync(string reference, CancellationToken cancellationToken = default)
    {
        var person = await ResolvePersonAsync(reference, cancellationToken);
        if (await repository.GetAttributeValueAsync(person.Id, AttributeKey, cancellationToken) is not null
            && !await repository.UpdateAttributesAsync(person.Id, "{}", [AttributeKey], cancellationToken))
        {
            throw new SubjectNotFoundException(person.Urn);
        }

        return new PersonPhoto(person, null);
    }

    /// <summary>The photo's artifact id as the attribute holds it, or null when it holds none (or anything else).</summary>
    public static Guid? ArtifactOf(string? attribute)
        => Guid.TryParse(attribute, out var id) ? id : null;

    private async Task<SubjectRef> ResolvePersonAsync(string reference, CancellationToken cancellationToken)
    {
        var subject = await subjects.ResolveAsync(reference, cancellationToken);
        if (subject.Type != SubjectTypes.Person)
        {
            throw new InvalidOperationException(
                $"'{subject.Title}' is a {subject.Type}; only People and AI agents have a photo.");
        }

        return subject;
    }
}

/// <summary>A Person and the artifact id of its photo, or null when it has none.</summary>
public sealed record PersonPhoto(SubjectRef Person, Guid? ArtifactId);
