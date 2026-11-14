using LifeOs.Application.Subjects;

namespace LifeOs.Contracts;

/// <summary>
/// <c>POST /api/people/{id}/photo</c> and <c>…/photo/delete</c>, and <c>bsk person photo --json</c>:
/// the Person, and its photo's artifact (null once removed). The bytes come back from
/// <c>GET /api/artifacts/{id}/content</c>.
/// </summary>
public sealed class PersonPhotoRead
{
    public CreatedSubject Person { get; set; } = new();
    public Guid? PhotoArtifactId { get; set; }

    public static PersonPhotoRead From(PersonPhoto photo) => new()
    {
        Person = SubjectRows.From(photo.Person),
        PhotoArtifactId = photo.ArtifactId,
    };
}
