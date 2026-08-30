namespace LifeOs.Application.Abstractions;

/// <summary>
/// Thrown when creating a subject would violate the uniqueness guarantee for a
/// reuse-by-title type (e.g. two Problems with the same title). The repository
/// translates the store's unique-violation into this so the application layer
/// can react without depending on any database-specific exception.
/// </summary>
public sealed class DuplicateSubjectException(string subjectType, string title, Exception? innerException = null)
    : Exception($"A {subjectType} subject titled '{title}' already exists.", innerException)
{
    public string SubjectType { get; } = subjectType;

    public string Title { get; } = title;
}
