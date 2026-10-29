using LifeOs.Application.Abstractions;
using LifeOs.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Tests;

/// <summary>
/// Habits for the tests about tracking. A new habit is born Planned and is tracked only from
/// the day it moves to Building (migration 0049), so a test that records a habit's days or
/// reads its schedule from a start date behind today makes a habit with no status, as every
/// habit made before Planned existed is: it reads as Building and is tracked from its start,
/// and a status change a test writes days back is still its newest.
/// </summary>
internal static class TestHabits
{
    public static Task<SubjectRef> CreateTrackedHabitAsync(
        this IServiceProvider kernel, string title, string attributesJson = "{}",
        CancellationToken cancellationToken = default)
        => kernel.GetRequiredService<ISubjectRepository>().CreateTrackedHabitAsync(title, attributesJson, cancellationToken);

    public static async Task<SubjectRef> CreateTrackedHabitAsync(
        this ISubjectRepository subjects, string title, string attributesJson = "{}",
        CancellationToken cancellationToken = default)
    {
        var urn = Urns.Build(SubjectTypes.Habit, title);
        var id = await subjects.CreateAsync(
            new NewSubject(urn, SubjectTypes.Habit, title, Recurrence.NormalizeAttributes(attributesJson)),
            cancellationToken);
        return new SubjectRef(id, urn, SubjectTypes.Habit, title);
    }
}
