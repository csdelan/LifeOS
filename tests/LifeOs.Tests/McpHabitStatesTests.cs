using System.ComponentModel;
using System.Reflection;
using System.Text.RegularExpressions;
using LifeOs.Api.Mcp.Tools;
using LifeOs.Api.Read;

namespace LifeOs.Tests;

/// <summary>
/// Code review CR132: the <c>habits</c> tool tells an agent the states a day can have. Its
/// description said "followed, partial, missed, unrecorded or absent", but the reader answers
/// <c>not_followed</c> for a missed day and leaves out a day the habit isn't scheduled, so an
/// agent looking for "missed" found nothing. The description names every state the reader answers
/// (<see cref="SubjectReader.HabitDayStates"/>) and none it doesn't.
/// </summary>
public sealed class McpHabitStatesTests
{
    private static string Description()
        => typeof(PlanTools).GetMethod(nameof(PlanTools.Habits))!.GetCustomAttribute<DescriptionAttribute>()!.Description;

    [Fact]
    public void The_habits_tool_names_every_state_a_day_can_have()
    {
        var description = Description();
        var unnamed = SubjectReader.HabitDayStates
            .Where(state => !Regex.IsMatch(description, $@"\b{Regex.Escape(state)}\b"))
            .ToList();

        Assert.Empty(unnamed);
    }

    [Fact]
    public void The_habits_tool_names_no_state_the_reader_never_answers()
    {
        var description = Description();

        Assert.DoesNotMatch(@"\babsent\b", description);
    }
}
