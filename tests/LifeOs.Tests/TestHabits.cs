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
    /// <summary>
    /// Makes a <paramref name="type"/> as every door does, so a Habit is made serving a Goal of its
    /// own, made beside it (code review CR125); any other type is made on its own.
    /// </summary>
    public static async Task<SubjectRef> MakeAsync(
        this Application.Subjects.SubjectService subjects, string type, string title, string attributesJson = "{}",
        CancellationToken cancellationToken = default)
    {
        if (type != SubjectTypes.Habit)
        {
            return await subjects.CreateAsync(type, title, attributesJson, cancellationToken: cancellationToken);
        }

        var goal = await subjects.CreateAsync(SubjectTypes.Goal, $"{title} goal", cancellationToken: cancellationToken);
        return (await subjects.CreateWithParentEdgeAsync(
            type, title, attributesJson, SubjectRelations.Serves, goal, cancellationToken: cancellationToken)).Child;
    }

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
