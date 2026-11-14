using Dapper;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// Code review CR70: <c>bsk.v_habit</c> reads a habit's <c>allows_partial</c> as the kernel does
/// (<see cref="AdherenceResults.PartialAllowed"/>), whatever the attribute holds. The view cast it to
/// boolean, so one habit with any other text made every read of it fail (the Habits page,
/// <c>/mcp habits</c>, <c>life_snapshot</c>), and it took "yes", "1" and "t" as true where the
/// kernel refused partial credit.
/// </summary>
public sealed class HabitPartialFlagTests(ClassDatabase classDb) : IClassFixture<ClassDatabase>
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("true", true)]
    [InlineData("\"true\"", true)]
    [InlineData("\" TRUE \"", true)]
    [InlineData("\"True\"", true)]
    [InlineData("false", false)]
    [InlineData("\"false\"", false)]
    [InlineData("\"maybe\"", false)]
    [InlineData("\"\"", false)]
    [InlineData("\"yes\"", false)]
    [InlineData("\"1\"", false)]
    [InlineData("1", false)]
    [InlineData("null", false)]
    [InlineData(null, false)]
    public async Task The_view_and_the_kernel_agree_on_partial_credit(string? flag, bool allowed)
    {
        var habit = await HabitAsync(flag);
        await using var connection = new NpgsqlConnection(classDb.ConnectionString);

        var view = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT allows_partial FROM bsk.v_habit WHERE id = @id;", new { id = habit.Id }, cancellationToken: Ct));
        var stored = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT attributes ->> 'allows_partial' FROM bsk.subject WHERE id = @id;", new { id = habit.Id }, cancellationToken: Ct));

        Assert.Equal(allowed, view);
        Assert.Equal(allowed, AdherenceResults.PartialAllowed(stored));
    }

    [Fact]
    public async Task A_habit_with_a_flag_that_is_no_boolean_leaves_every_habit_readable()
    {
        await HabitAsync("\"maybe\"");
        await using var connection = new NpgsqlConnection(classDb.ConnectionString);

        var habits = await connection.QueryAsync<bool>(new CommandDefinition(
            "SELECT allows_partial FROM bsk.v_habit;", cancellationToken: Ct));

        Assert.NotEmpty(habits);
    }

    /// <summary>A habit whose <c>allows_partial</c> is the JSON <paramref name="flag"/>, or has none.</summary>
    private async Task<SubjectRef> HabitAsync(string? flag)
    {
        await using var kernel = classDb.Kernel();
        var attributes = flag is null ? "{}" : $$"""{"{{AdherenceResults.AllowsPartialKey}}": {{flag}}}""";
        return await kernel.GetRequiredService<SubjectService>().CreateAsync(
            SubjectTypes.Habit, $"Stretch {Guid.NewGuid():N}", attributes, cancellationToken: Ct);
    }
}
