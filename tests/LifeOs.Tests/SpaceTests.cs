using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Home;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// HOME-1 / HOME-2, D18 (environment plan E1, §4.1–§4.3, migration 0044): the home's spaces and
/// their ratings. A space is a <c>Space</c> subject read through <c>bsk.v_space</c>, with a
/// weight and pace only <see cref="SpaceService"/> writes; a rating is a versioned
/// <c>measurement</c> event read through <c>bsk.v_space_rating</c>, newest version per id, voided
/// to delete. Each test runs in a database of its own (<see cref="DomainTestDatabase"/>), since a
/// space's name is unique across the home.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SpaceTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_space_is_added_reweighted_renamed_archived_and_restored()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var service = db.Get<SpaceService>();

        var office = await service.AddAsync("  Office ", 5, 5, Ct);
        Assert.Equal(("Office", 5, 5, false), (office.Name, office.Weight, office.Pace, office.Archived));
        var kitchen = await service.AddAsync("Kitchen", cancellationToken: Ct);
        Assert.Equal((3, 3), (kitchen.Weight, kitchen.Pace));

        // A name is unique among the spaces not archived, in any case.
        var taken = await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddAsync("office", cancellationToken: Ct));
        Assert.Contains("already have a space called 'Office'", taken.Message);

        var changed = await service.SetAsync("kitchen", name: "Kitchen and pantry", weight: 4, pace: 5, cancellationToken: Ct);
        Assert.Equal(("Kitchen and pantry", 4, 5), (changed.Name, changed.Weight, changed.Pace));
        Assert.Equal(changed.Id, (await service.ResolveAsync(changed.Id.ToString(), Ct)).Id);
        Assert.Equal(changed.Id, (await service.ResolveAsync(changed.Urn[^6..], Ct)).Id);

        var archived = await service.ArchiveAsync("Office", Ct);
        Assert.True(archived.Archived);
        Assert.Equal(await db.TodayAsync(), archived.ArchivedOn);
        Assert.Equal(["Kitchen and pantry"], (await service.ListAsync(cancellationToken: Ct)).Select(s => s.Name));
        Assert.Equal(2, (await service.ListAsync(includeArchived: true, Ct)).Count);

        // An archived space's name is free again; restoring it while it's taken is refused.
        var newOffice = await service.AddAsync("Office", 2, 2, Ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RestoreAsync(office.Id.ToString(), Ct));
        await service.SetAsync(newOffice.Id.ToString(), name: "Study", cancellationToken: Ct);
        var restored = await service.RestoreAsync(office.Id.ToString(), Ct);
        Assert.False(restored.Archived);
        Assert.Null(restored.ArchivedOn);
    }

    [Fact]
    public async Task A_bad_name_weight_or_pace_is_refused_and_set_refuses_weight_and_pace()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var service = db.Get<SpaceService>();

        Assert.Contains("weight is a whole number from 1 to 10",
            (await Assert.ThrowsAsync<ArgumentException>(() => service.AddAsync("Office", 11, cancellationToken: Ct))).Message);
        Assert.Contains("pace is a whole number from 1",
            (await Assert.ThrowsAsync<ArgumentException>(() => service.AddAsync("Office", 3, 0, Ct))).Message);
        await Assert.ThrowsAsync<ArgumentException>(() => service.AddAsync("  ", cancellationToken: Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => service.AddAsync("two\nlines", cancellationToken: Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => service.AddAsync(new string('x', 61), cancellationToken: Ct));
        Assert.Empty(await service.ListAsync(true, Ct));

        var office = await service.AddAsync("Office", 5, 5, Ct);
        await Assert.ThrowsAsync<ArgumentException>(() => service.SetAsync("Office", cancellationToken: Ct));
        var refused = await Assert.ThrowsAsync<ArgumentException>(() => db.Get<AttributeService>().SetAsync(
            office.Urn, [new AttributeAssignment("weight", "9")], Ct));
        Assert.Contains("bsk space set", refused.Message);
        Assert.Equal(5, (await service.ResolveAsync("Office", Ct)).Weight);
    }

    [Fact]
    public async Task A_walk_through_rates_many_spaces_and_ratings_are_edited_confirmed_and_deleted()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var spaces = db.Get<SpaceService>();
        var ratings = db.Get<SpaceRatingService>();
        var today = await db.TodayAsync();
        var office = await spaces.AddAsync("Office", 5, 5, Ct);
        var kitchen = await spaces.AddAsync("Kitchen", 4, 5, Ct);

        // One walk-through, two spaces, backdated to yesterday.
        var walk = await ratings.RateAsync(today.AddDays(-1),
        [
            new RatingInput("Office", 4.5m, 4m, 3.5m, "  Desk clear  "),
            new RatingInput(kitchen.Id.ToString(), 3m, 2.5m, 3m)
        ], Ct);
        Assert.Equal(2, walk.Ratings.Count);
        Assert.Empty(walk.Earned.Awards);
        var officeRating = walk.Ratings.Single(r => r.SpaceId == office.Id);
        Assert.Equal((today.AddDays(-1), 4.5m, 4m, 3.5m, "Desk clear"),
            (officeRating.Day, officeRating.Tidiness, officeRating.Cleanliness, officeRating.Aesthetics, officeRating.Note));
        Assert.Equal("4", officeRating.Cleanliness!.Value.ToString(CultureInfo.InvariantCulture));

        // A quick check rates two parts; "same as last time" repeats each part's last rated value.
        var quick = await ratings.RateAsync(null, [new RatingInput("Kitchen", Tidiness: 4m, Cleanliness: 3.5m)], Ct);
        Assert.Equal((today, 4m, 3.5m, (decimal?)null), (quick.Ratings[0].Day, quick.Ratings[0].Tidiness, quick.Ratings[0].Cleanliness, quick.Ratings[0].Aesthetics));
        var same = await ratings.RateAsync(null, [new RatingInput("kitchen", Same: true)], Ct);
        Assert.Equal((4m, 3.5m, 3m), (same.Ratings[0].Tidiness, same.Ratings[0].Cleanliness, same.Ratings[0].Aesthetics));

        // An edit writes a newer version that wins; changing nothing writes nothing.
        var edited = await ratings.EditAsync(officeRating.Id.ToString()[..8], new RatingEdit(Tidiness: 5m, Note: ""), Ct);
        Assert.Equal((5m, 4m, (string?)null), (edited.Ratings[0].Tidiness, edited.Ratings[0].Cleanliness, edited.Ratings[0].Note));
        var events = await RatingEventCountAsync(db);
        await ratings.EditAsync(officeRating.Id.ToString(), new RatingEdit(Tidiness: 5m), Ct);
        Assert.Equal(events, await RatingEventCountAsync(db));

        // The concerns edge points every version at its space.
        Assert.Equal(await RatingEventCountAsync(db), await ConcernsCountAsync(db, office.Id) + await ConcernsCountAsync(db, kitchen.Id));

        var deleted = await ratings.DeleteAsync(officeRating.Id.ToString(), Ct);
        Assert.Empty(deleted.Ratings);
        Assert.Null(await db.Get<ISpaceRepository>().GetRatingAsync(officeRating.Id, Ct));
        var list = await ratings.ListAsync(cancellationToken: Ct);
        Assert.Equal(3, list.Ratings.Count);
        Assert.All(list.Ratings, r => Assert.Equal(kitchen.Id, r.SpaceId));
        Assert.Equal(today, list.Ratings[0].Day);
        var todayOnly = await ratings.ListAsync("Kitchen", 1, Ct);
        Assert.Equal([today, today], todayOnly.Ratings.Select(r => r.Day));
    }

    [Fact]
    public async Task Bad_ratings_are_refused_in_words_with_nothing_written()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var spaces = db.Get<SpaceService>();
        var ratings = db.Get<SpaceRatingService>();
        var today = await db.TodayAsync();
        await spaces.AddAsync("Office", 5, 5, Ct);
        await spaces.AddAsync("Garage", 1, 2, Ct);
        await spaces.ArchiveAsync("Garage", Ct);

        foreach (var (day, input, words) in new (DateOnly? Day, RatingInput[] Input, string Words)[]
                 {
                     (today.AddDays(1), [new RatingInput("Office", 4m, 4m, 4m)], "today or earlier"),
                     (null, [new RatingInput("Office", 4.25m)], "half points"),
                     (null, [new RatingInput("Office", 0.5m)], "half points"),
                     (null, [new RatingInput("Office", Aesthetics: 5.5m)], "Aesthetics is rated 1 to 5"),
                     (null, [new RatingInput("Office")], "Rate at least one part of 'Office'"),
                     (null, [new RatingInput("Office", Same: true)], "nothing to repeat"),
                     (null, [new RatingInput("Office", 4m, Same: true)], "takes no new ones"),
                     (null, [new RatingInput("Garage", 4m)], "archived"),
                     (null, [new RatingInput("Office", 4m), new RatingInput("office", 3m)], "rated twice"),
                     (null, [new RatingInput("Office", 4m, Note: new string('x', 141))], "up to 140"),
                     (null, [], "at least one space")
                 })
        {
            var refused = await Assert.ThrowsAnyAsync<Exception>(() => ratings.RateAsync(day, input, Ct));
            Assert.True(refused is ArgumentException or InvalidOperationException, refused.GetType().Name);
            Assert.Contains(words, refused.Message);
        }

        await Assert.ThrowsAsync<SubjectNotFoundException>(() => ratings.RateAsync(null, [new RatingInput("Attic", 3m)], Ct));
        Assert.Equal(0, await RatingEventCountAsync(db));
    }

    [Fact]
    public async Task The_views_read_hand_written_events_tolerantly()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var office = await db.Get<SpaceService>().AddAsync("Office", 5, 5, Ct);
        var task = await db.Get<SubjectService>().CreateAsync(SubjectTypes.Task, "Not a space", "{}", cancellationToken: Ct);
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync(Ct);

        // A malformed weight or pace reads as 3.
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE bsk.subject SET attributes = '{\"weight\": \"heavy\", \"pace\": 2.5}' WHERE id = @id;",
            new { id = office.Id }, cancellationToken: Ct));
        var read = (await db.Get<ISpaceRepository>().GetSpaceAsync(office.Id, Ct))!;
        Assert.Equal((3, 3), (read.Weight, read.Pace));

        // A part outside 1-5 or not in halves reads as null; a rating of a non-space drops out.
        var good = Guid.NewGuid();
        await AppendAsync(connection, new { id = good, space = office.Id, day = "2026-10-01", tidiness = 4.5, cleanliness = 7, aesthetics = 3.25 });
        await AppendAsync(connection, new { id = Guid.NewGuid(), space = task.Id, day = "2026-10-01", tidiness = 4 });
        await AppendAsync(connection, new { id = Guid.NewGuid(), space = office.Id, day = "not a day", tidiness = 4 });
        var ratings = await db.Get<ISpaceRepository>().GetRatingsAsync(cancellationToken: Ct);
        var rating = Assert.Single(ratings);
        Assert.Equal((good, 4.5m, (decimal?)null, (decimal?)null), (rating.Id, rating.Tidiness, rating.Cleanliness, rating.Aesthetics));
    }

    [Fact]
    public async Task The_endpoints_add_and_set_spaces_and_rate_edit_list_and_delete()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var today = await db.TodayAsync();
        await using var host = await ApiTestHost.StartAsync(db.ConnectionString, Ct, DomainTestDatabase.ApiZone);

        var office = await JsonAsync(await host.Client.PostAsJsonAsync("/api/environment/spaces", new { name = "Office", weight = 5, pace = 5 }, Ct));
        var officeId = office.GetProperty("id").GetString()!;
        Assert.Equal(100m, office.GetProperty("share").GetDecimal());
        var kitchen = await JsonAsync(await host.Client.PostAsJsonAsync("/api/environment/spaces", new { name = "Kitchen" }, Ct));
        var kitchenId = kitchen.GetProperty("id").GetString()!;
        Assert.Equal((3, 3), (kitchen.GetProperty("weight").GetInt32(), kitchen.GetProperty("pace").GetInt32()));
        var reweighted = await JsonAsync(await host.Client.PostAsJsonAsync($"/api/environment/spaces/{kitchenId}", new { weight = 4, pace = 5 }, Ct));
        Assert.Equal(44.4m, reweighted.GetProperty("share").GetDecimal());

        var spaces = await host.Client.GetFromJsonAsync<JsonElement>("/api/environment/spaces", Ct);
        Assert.Equal(["Kitchen", "Office"], spaces.EnumerateArray().Select(s => s.GetProperty("name").GetString()));

        var walk = await JsonAsync(await host.Client.PostAsJsonAsync("/api/environment/ratings", new
        {
            day = Day(today.AddDays(-1)),
            ratings = new object[]
            {
                new { space = officeId, tidiness = 4.5, cleanliness = 4, aesthetics = 3.5, note = "Desk clear" },
                new { space = "Kitchen", tidiness = 3, cleanliness = 2.5, aesthetics = 3 }
            }
        }, Ct));
        Assert.Equal(2, walk.GetProperty("ratings").GetArrayLength());
        Assert.Equal(0, walk.GetProperty("progress").GetArrayLength());
        var first = walk.GetProperty("ratings")[0];
        Assert.Equal(("Office", Day(today.AddDays(-1)), 4.5m), (first.GetProperty("space").GetString(),
            first.GetProperty("day").GetString(), first.GetProperty("tidiness").GetDecimal()));

        var ratingId = first.GetProperty("id").GetString();
        var edited = await JsonAsync(await host.Client.PostAsJsonAsync($"/api/environment/ratings/{ratingId}", new { cleanliness = 5 }, Ct));
        Assert.Equal(5m, edited.GetProperty("ratings")[0].GetProperty("cleanliness").GetDecimal());

        var list = await host.Client.GetFromJsonAsync<JsonElement>($"/api/environment/ratings?space={kitchenId}", Ct);
        Assert.Equal(["Kitchen"], list.GetProperty("ratings").EnumerateArray().Select(r => r.GetProperty("space").GetString()));

        var deleted = await JsonAsync(await host.Client.PostAsync($"/api/environment/ratings/{ratingId}/delete", null, Ct));
        Assert.Equal(0, deleted.GetProperty("ratings").GetArrayLength());
        var after = await host.Client.GetFromJsonAsync<JsonElement>("/api/environment/ratings?days=7", Ct);
        Assert.Single(after.GetProperty("ratings").EnumerateArray());

        foreach (var (body, words) in new (object Body, string Words)[]
                 {
                     (new { day = Day(today.AddDays(1)), ratings = new[] { new { space = "Office", tidiness = 4 } } }, "today or earlier"),
                     (new { ratings = new[] { new { space = "Office", tidiness = 4.2 } } }, "half points"),
                     (new { ratings = new[] { new { space = "Office" } } }, "at least one part"),
                     (new { day = "27/10/2026", ratings = new[] { new { space = "Office", tidiness = 4 } } }, "yyyy-MM-dd")
                 })
        {
            var response = await host.Client.PostAsJsonAsync("/api/environment/ratings", body, Ct);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains(words, await response.Content.ReadAsStringAsync(Ct));
        }

        var duplicate = await host.Client.PostAsJsonAsync("/api/environment/spaces", new { name = "office" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.GetAsync("/api/environment/ratings?days=0", Ct)).StatusCode);
    }

    private async Task AppendAsync(NpgsqlConnection connection, object rating)
    {
        var payload = JsonSerializer.Serialize(new Dictionary<string, object> { [SpaceLimits.RatingKey] = rating });
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO bsk.event (kind, provenance, occurred_at, recorded_at, source_id, payload)
            VALUES ('measurement', 'declared', now(), now(), 'cli', @payload::jsonb);
            """,
            new { payload }, cancellationToken: Ct));
    }

    private async Task<long> RatingEventCountAsync(DomainTestDatabase db)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync(Ct);
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM bsk.event WHERE kind = 'measurement' AND jsonb_exists(payload, 'space_rating');",
            cancellationToken: Ct));
    }

    private async Task<long> ConcernsCountAsync(DomainTestDatabase db, Guid spaceId)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync(Ct);
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM bsk.subject_event WHERE subject_id = @spaceId AND relation = 'concerns';",
            new { spaceId }, cancellationToken: Ct));
    }

    private async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.IsSuccessStatusCode, text);
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
