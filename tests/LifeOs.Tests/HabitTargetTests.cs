using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using LifeOs.Application.Rewards;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// A Habit's establish target (REWARD-2): <see cref="HabitTargetService"/> (the write path
/// behind <c>bsk habit target</c>) and <c>POST /api/habits/target</c>. What it writes is what
/// <c>bsk.v_habit_establishment</c> reads — numbers, never strings — and it refuses what the
/// view would silently ignore. The database is shared, so each test makes its own habits.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class HabitTargetTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ServiceProvider Kernel()
        => new ServiceCollection().AddLifeOsKernel(postgres.ConnectionString).BuildServiceProvider();

    private static Task<SubjectRef> NewAsync(ServiceProvider kernel, string type, string? attributesJson = null)
        => kernel.GetRequiredService<SubjectService>().CreateAsync(
            type, $"target {type} {Guid.NewGuid():N}", attributesJson ?? "{}", cancellationToken: Ct);

    /// <summary>The target as the view reads it.</summary>
    private async Task<(decimal MinAdherence, int WindowDays)> ViewTargetAsync(SubjectRef habit)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        return await connection.QuerySingleAsync<(decimal, int)>(new CommandDefinition(
            "SELECT min_adherence, window_days FROM bsk.v_habit_establishment WHERE habit_id = @id;",
            new { id = habit.Id }, cancellationToken: Ct));
    }

    private async Task<string?> StoredAsync(SubjectRef habit)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        return await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT attributes->>'establish_target' FROM bsk.subject WHERE id = @id;",
            new { id = habit.Id }, cancellationToken: Ct));
    }

    [Fact]
    public async Task A_percentage_is_stored_as_the_fraction_the_view_reads()
    {
        await using var kernel = Kernel();
        var targets = kernel.GetRequiredService<HabitTargetService>();
        var habit = await NewAsync(kernel, SubjectTypes.Habit);

        var view = await targets.SetAsync(habit.Urn, 75m, 30, Ct);

        Assert.Equal(0.75m, view.MinAdherence);
        Assert.Equal(30, view.WindowDays);
        Assert.True(view.AdherenceIsCustom);
        Assert.True(view.WindowIsCustom);
        Assert.Equal((0.75m, 30), await ViewTargetAsync(habit));
        using var stored = JsonDocument.Parse((await StoredAsync(habit))!);
        Assert.Equal(JsonValueKind.Number, stored.RootElement.GetProperty("min_adherence").ValueKind);
        Assert.Equal(JsonValueKind.Number, stored.RootElement.GetProperty("window_days").ValueKind);
    }

    [Fact]
    public async Task Setting_one_part_keeps_the_other_and_clear_puts_the_default_back()
    {
        await using var kernel = Kernel();
        var targets = kernel.GetRequiredService<HabitTargetService>();
        var habit = await NewAsync(kernel, SubjectTypes.Habit);

        var before = await targets.GetAsync(habit.Urn, Ct);
        Assert.Equal((0.8m, 60, false, false),
            (before.MinAdherence, before.WindowDays, before.AdherenceIsCustom, before.WindowIsCustom));

        var days = await targets.SetAsync(habit.Urn, null, 21, Ct);
        Assert.Equal((0.8m, 21, false, true),
            (days.MinAdherence, days.WindowDays, days.AdherenceIsCustom, days.WindowIsCustom));
        var both = await targets.SetAsync(habit.Urn, 0.9m, null, Ct);
        Assert.Equal((0.9m, 21, true, true),
            (both.MinAdherence, both.WindowDays, both.AdherenceIsCustom, both.WindowIsCustom));
        Assert.Equal((0.9m, 21), await ViewTargetAsync(habit));

        var cleared = await targets.ClearAsync(habit.Urn, Ct);
        Assert.Equal((0.8m, 60, false, false),
            (cleared.MinAdherence, cleared.WindowDays, cleared.AdherenceIsCustom, cleared.WindowIsCustom));
        Assert.Null(await StoredAsync(habit));
        Assert.Equal((0.8m, 60), await ViewTargetAsync(habit));
    }

    [Fact]
    public async Task A_hand_written_target_reads_as_the_view_reads_it_and_a_write_tidies_it()
    {
        await using var kernel = Kernel();
        var targets = kernel.GetRequiredService<HabitTargetService>();
        // 90 as a percentage, and a window written as a string, which the view ignores.
        var habit = await NewAsync(kernel, SubjectTypes.Habit,
            """{"establish_target": {"min_adherence": 90, "window_days": "30"}}""");

        var read = await targets.GetAsync(habit.Urn, Ct);
        Assert.Equal((0.9m, 60, true, false),
            (read.MinAdherence, read.WindowDays, read.AdherenceIsCustom, read.WindowIsCustom));
        Assert.Equal((0.9m, 60), await ViewTargetAsync(habit));

        await targets.SetAsync(habit.Urn, null, 14, Ct);
        using var stored = JsonDocument.Parse((await StoredAsync(habit))!);
        Assert.Equal(0.9m, stored.RootElement.GetProperty("min_adherence").GetDecimal());
        Assert.Equal(14, stored.RootElement.GetProperty("window_days").GetInt32());
    }

    [Fact]
    public async Task What_the_view_would_ignore_is_refused_and_nothing_is_written()
    {
        await using var kernel = Kernel();
        var targets = kernel.GetRequiredService<HabitTargetService>();
        var habit = await NewAsync(kernel, SubjectTypes.Habit);
        var task = await NewAsync(kernel, SubjectTypes.Task);

        Assert.Contains("only a Habit has an establish target",
            (await Assert.ThrowsAsync<InvalidOperationException>(
                () => targets.SetAsync(task.Urn, 0.8m, null, Ct))).Message);
        foreach (var bad in new[] { 0m, -0.5m, 100.5m, 250m })
        {
            Assert.Contains("share of days followed",
                (await Assert.ThrowsAsync<ArgumentException>(
                    () => targets.SetAsync(habit.Urn, bad, null, Ct))).Message);
        }

        foreach (var bad in new[] { 0, -7, EstablishTarget.MaxWindowDays + 1 })
        {
            Assert.Contains("whole number of days from 1 to 3650",
                (await Assert.ThrowsAsync<ArgumentException>(
                    () => targets.SetAsync(habit.Urn, null, bad, Ct))).Message);
        }

        Assert.Contains("Say what to change",
            (await Assert.ThrowsAsync<ArgumentException>(
                () => targets.SetAsync(habit.Urn, null, null, Ct))).Message);
        Assert.Null(await StoredAsync(habit));

        // 1 is the whole of the days, not 1%.
        Assert.Equal(1m, (await targets.SetAsync(habit.Urn, 1m, 1, Ct)).MinAdherence);
    }

    [Fact]
    public async Task The_endpoint_sets_and_clears_the_target_the_habits_read_shows()
    {
        await using var host = await ApiTestHost.StartAsync(postgres.ConnectionString, Ct);
        await using var kernel = Kernel();
        var habit = await NewAsync(kernel, SubjectTypes.Habit);

        var set = await PostAsync(host.Client, new { habit = habit.Id.ToString(), minAdherence = 70, windowDays = 45 });
        Assert.Equal(habit.Urn, set.GetProperty("habit").GetProperty("urn").GetString());
        Assert.Equal(0.7m, set.GetProperty("minAdherence").GetDecimal());
        Assert.Equal(45, set.GetProperty("windowDays").GetInt32());
        Assert.True(set.GetProperty("adherenceIsCustom").GetBoolean());

        var progress = (await HabitAsync(host.Client, habit.Id)).GetProperty("establishment");
        Assert.Equal(0.7m, progress.GetProperty("minAdherence").GetDecimal());
        Assert.Equal(45, progress.GetProperty("windowDays").GetInt32());

        var days = await PostAsync(host.Client, new { habit = habit.Urn, windowDays = 10 });
        Assert.Equal(0.7m, days.GetProperty("minAdherence").GetDecimal());
        Assert.Equal(10, days.GetProperty("windowDays").GetInt32());

        var cleared = await PostAsync(host.Client, new { habit = habit.Urn, clear = true });
        Assert.Equal(0.8m, cleared.GetProperty("minAdherence").GetDecimal());
        Assert.False(cleared.GetProperty("windowIsCustom").GetBoolean());
        progress = (await HabitAsync(host.Client, habit.Id)).GetProperty("establishment");
        Assert.Equal(60, progress.GetProperty("windowDays").GetInt32());
    }

    [Fact]
    public async Task A_target_the_kernel_refuses_is_a_400_with_its_reason()
    {
        await using var host = await ApiTestHost.StartAsync(postgres.ConnectionString, Ct);
        await using var kernel = Kernel();
        var habit = await NewAsync(kernel, SubjectTypes.Habit);
        var goal = await NewAsync(kernel, SubjectTypes.Goal);

        Assert.Contains("habit is required", await RefusedAsync(host.Client, new { minAdherence = 0.8 }));
        Assert.Contains("one of them", await RefusedAsync(host.Client, new { habit = habit.Urn }));
        Assert.Contains("one of them",
            await RefusedAsync(host.Client, new { habit = habit.Urn, windowDays = 30, clear = true }));
        Assert.Contains("share of days followed",
            await RefusedAsync(host.Client, new { habit = habit.Urn, minAdherence = 120 }));
        Assert.Contains("from 1 to 3650", await RefusedAsync(host.Client, new { habit = habit.Urn, windowDays = 0 }));
        Assert.Contains("only a Habit", await RefusedAsync(host.Client, new { habit = goal.Urn, windowDays = 30 }));
    }

    private static async Task<JsonElement> PostAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/habits/target", body, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.IsSuccessStatusCode, $"POST /api/habits/target → {(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement;
    }

    private static async Task<string> RefusedAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/habits/target", body, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return json.GetProperty("error").GetString() ?? "";
    }

    private static async Task<JsonElement> HabitAsync(HttpClient client, Guid id)
        => (await client.GetFromJsonAsync<JsonElement>("/api/habits", Ct)).EnumerateArray()
            .Single(h => h.GetProperty("id").GetGuid() == id);
}
