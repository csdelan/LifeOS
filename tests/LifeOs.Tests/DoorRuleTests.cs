using System.Text.Json;
using Dapper;
using LifeOs.Api.Read;
using LifeOs.Application.Food;
using LifeOs.Application.Subjects;
using LifeOs.Cli.Commands;
using LifeOs.Domain;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// Code review CR72: rules the doors each worked out for themselves live once, in Domain or
/// Application. A space's share of the Home Score rounded half to even in <c>bsk space --json</c>
/// and half away from zero in <c>/api</c>; a Value's statement was checked only by <c>bsk new</c>
/// and the API's create, so promoting a capture to a Value reached the database's raw check; and
/// both doors filled a food edit's missing fields from the entry themselves.
/// </summary>
public sealed class DoorRuleTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Space SpaceOf(int weight, bool archived = false)
        => new(Guid.NewGuid(), "urn:bsk:space:x", "Room", weight, 3, archived ? DateTimeOffset.UtcNow : null, null, DateTimeOffset.UtcNow);

    [Theory]
    [InlineData(1, 6.3)]
    [InlineData(5, 31.3)]
    [InlineData(3, 18.8)]
    public void A_spaces_share_rounds_half_away_from_zero_in_both_doors(int weight, double share)
    {
        // Spaces weighted 1, 5, 5 and 5: 16 in all, so 1 is 6.25% and 5 is 31.25%.
        var space = SpaceOf(weight);
        var total = SpaceShares.TotalWeight([SpaceOf(1), SpaceOf(5), SpaceOf(5), SpaceOf(5), SpaceOf(9, archived: true)]);

        Assert.Equal(16, total);
        Assert.Equal((decimal)share, SpaceShares.Of(space, total));
        Assert.Equal((decimal)share, EnvironmentRows.From(space, total).Share);
        var bsk = JsonSerializer.SerializeToElement(SpaceCommand.Json(space, total));
        Assert.Equal((decimal)share, bsk.GetProperty("share").GetDecimal());
    }

    [Fact]
    public void An_archived_space_or_a_home_with_no_weight_has_no_share()
    {
        Assert.Null(SpaceShares.Of(SpaceOf(4, archived: true), 10));
        Assert.Null(SpaceShares.Of(SpaceOf(4), 0));
        Assert.Equal(JsonValueKind.Null, JsonSerializer.SerializeToElement(SpaceCommand.Json(SpaceOf(4), 0)).GetProperty("share").ValueKind);
    }

    [Fact]
    public async Task A_Value_without_its_statement_is_refused_in_words_however_it_is_made()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var capture = await db.Get<LifeOs.Application.Capture.CaptureService>().CaptureNoteAsync("I keep my promises", Ct);

        var promoted = await Assert.ThrowsAsync<ArgumentException>(() =>
            db.Get<PromotionService>().PromoteAsync(capture.EventId, SubjectTypes.Value, "Reliable", Ct));
        var made = await Assert.ThrowsAsync<ArgumentException>(() =>
            db.Get<SubjectService>().CreateAsync(SubjectTypes.Value, "Kind", """{"statement": "  "}""", cancellationToken: Ct));

        Assert.Equal(ValueStatements.Missing, promoted.Message);
        Assert.Equal(ValueStatements.Missing, made.Message);
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM bsk.subject WHERE type = 'Value';", cancellationToken: Ct)));
    }

    [Fact]
    public async Task A_Values_statement_cannot_be_cleared()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var value = await db.Get<SubjectService>().CreateAsync(
            SubjectTypes.Value, "Reliable", """{"statement": "I keep my promises."}""", cancellationToken: Ct);

        var cleared = await Assert.ThrowsAsync<ArgumentException>(() =>
            db.Get<AttributeService>().SetAsync(value.Urn, [new AttributeAssignment("statement", "")], Ct));

        Assert.Equal(ValueStatements.Missing, cleared.Message);
    }

    [Fact]
    public async Task A_food_edit_keeps_the_time_and_meal_type_left_out_and_a_change_keeps_the_rest()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var food = db.Get<FoodLogService>();
        var at = DateTimeOffset.UtcNow.AddHours(-30);
        at = at.AddTicks(-(at.Ticks % TimeSpan.TicksPerSecond));
        var meal = (await food.LogAsync(new FoodEntryInput("Oatmeal", 4, 400, at, "breakfast", ProteinG: 12), cancellationToken: Ct)).Meal!;

        // The Log food sheet's whole entry, time and meal type left out: they stay, protein is cleared.
        var edited = (await food.EditAsync(meal.Id.ToString(), new FoodEntryInput("Oatmeal", 4.5m, 420), Ct)).Meal!;
        Assert.Equal((at, "breakfast", 420, (int?)null), (edited.EatenAt, edited.MealType, edited.Calories, edited.ProteinG));

        // bsk food edit's change, calories only: everything else stays.
        var changed = (await food.ChangeAsync(meal.Id.ToString(), new FoodEntryChange(Calories: 450, ProteinG: 10), Ct)).Meal!;
        Assert.Equal(("Oatmeal", 4.5m, 450, at, "breakfast", (int?)10), (changed.Name, changed.Score, changed.Calories, changed.EatenAt, changed.MealType, changed.ProteinG));
    }
}
