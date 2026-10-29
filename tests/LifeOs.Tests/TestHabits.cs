using LifeOs.Application.Abstractions;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Tests;

/// <summary>
/// Habits for the tests about tracking. A new habit is born Planned and isn't tracked
/// (migration 0049), so a test that records a habit's days or reads its schedule makes one
/// that's tracked:
/// <list type="bullet">
/// <item><see cref="CreateTrackedHabitAsync"/> makes it and starts tracking it now, as Start
/// tracking does.</item>
/// <item><see cref="CreateHabitWithoutStatusAsync"/> makes it with no status, as habits made
/// before Planned existed are: it reads as Building, so a status change a test writes days
/// back is still its newest.</item>
/// </list>
/// </summary>
internal static class TestHabits
{
    public static async Task<SubjectRef> CreateTrackedHabitAsync(
        this IServiceProvider kernel, string title, string attributesJson = "{}",
        CancellationToken cancellationToken = default)
    {
        var habit = await kernel.GetRequiredService<SubjectService>().CreateAsync(
            SubjectTypes.Habit, title, attributesJson, cancellationToken: cancellationToken);
        await kernel.GetRequiredService<StatusService>().ChangeStatusAsync(habit.Urn, "Building", cancellationToken);
        return habit;
    }

    public static async Task<SubjectRef> CreateHabitWithoutStatusAsync(
        this IServiceProvider kernel, string title, string attributesJson = "{}",
        CancellationToken cancellationToken = default)
    {
        var urn = Urns.Build(SubjectTypes.Habit, title);
        var id = await kernel.GetRequiredService<ISubjectRepository>().CreateAsync(
            new NewSubject(urn, SubjectTypes.Habit, title, Recurrence.NormalizeAttributes(attributesJson)),
            cancellationToken);
        return new SubjectRef(id, urn, SubjectTypes.Habit, title);
    }
}
