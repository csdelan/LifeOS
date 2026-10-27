using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Home;
using LifeOs.Application.Photos;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// HOME-4, D18 (environment plan E6, §8, migration 0045): photos of each space, from a baseline.
/// A photo is a versioned <c>observation</c> event read through <c>bsk.v_space_photo</c>; it
/// belongs to a space and one of its shots on a day. A shot's baseline is its first photo until
/// another is picked, and falls back when the picked one is removed. Shots are the space's
/// <c>shots</c> attribute, written only by <see cref="SpaceService"/>. Each test runs in a
/// database of its own (<see cref="DomainTestDatabase"/>).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SpacePhotoTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_photo_round_trips_and_a_shots_first_photo_is_its_baseline_until_another_is_picked()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var spaces = db.Get<SpaceService>();
        var photos = db.Get<SpacePhotoService>();
        var repository = db.Get<ISpaceRepository>();
        var today = await db.TodayAsync();
        var office = await spaces.AddAsync("Office", 5, 5, Ct);

        // A space starts with one shot, named after it.
        Assert.Equal([(SpaceShot.MainId, "Office")], office.Shots.Select(s => (s.Id, office.ShotName(s))));

        var firstBytes = FoodPhotoTests.Jpeg();
        var then = await photos.AddAsync("office", firstBytes, today.AddDays(-30), filename: @"C:\phone\IMG_0001.jpg", cancellationToken: Ct);
        Assert.Equal((office.Id, SpaceShot.MainId, today.AddDays(-30)), (then.SpaceId, then.ShotId, then.Day));
        var blob = (await db.Get<IArtifactBlobStore>().GetAsync(then.ArtifactId, Ct))!;
        Assert.Equal(firstBytes, blob.Bytes);
        Assert.Equal(("image/jpeg", "IMG_0001.jpg"), (blob.ContentType, blob.Filename));

        // Its event: an observation filed at noon on its day, with a concerns edge to the space.
        await using (var connection = await OpenAsync(db))
        {
            var (kind, artifact, edges) = await connection.QuerySingleAsync<(string, Guid, long)>(new CommandDefinition(
                """
                SELECT e.kind, e.artifact_id,
                       (SELECT count(*) FROM bsk.subject_event se
                        WHERE se.event_id = e.id AND se.subject_id = @space AND se.relation = 'concerns')
                FROM bsk.event e WHERE e.id = @id;
                """,
                new { id = then.EventId, space = office.Id }, cancellationToken: Ct));
            Assert.Equal(("observation", then.ArtifactId, 1L), (kind, artifact, edges));
        }

        var nowBytes = FoodPhotoTests.Jpeg();
        var now = await photos.AddAsync("Office", nowBytes, cancellationToken: Ct);
        Assert.Equal(today, now.Day);

        // The same bytes on the same shot aren't added twice.
        Assert.Equal(then.Id, (await photos.AddAsync("Office", firstBytes, cancellationToken: Ct)).Id);
        Assert.Equal([now.Id, then.Id], (await photos.ListAsync("Office", cancellationToken: Ct)).Photos.Select(p => p.Id));

        // Then is the first photo, Now the newest.
        var shot = (await repository.GetSpaceAsync(office.Id, Ct))!.Shots[0];
        var all = await repository.GetPhotosAsync(office.Id, Ct);
        Assert.Equal((then.Id, now.Id), (ShotPhotos.Baseline(shot, all)!.Id, ShotPhotos.Newest(shot, all)!.Id));

        // Picking the newest moves the baseline; removing it falls back to the first.
        await photos.SetBaselineAsync(now.Id.ToString()[..8], Ct);
        shot = (await repository.GetSpaceAsync(office.Id, Ct))!.Shots[0];
        Assert.Equal(now.Id, shot.Baseline);
        Assert.Equal(now.Id, ShotPhotos.Baseline(shot, await repository.GetPhotosAsync(office.Id, Ct))!.Id);
        await photos.RemoveAsync(now.Id.ToString(), Ct);
        all = await repository.GetPhotosAsync(office.Id, Ct);
        Assert.Equal([then.Id], all.Select(p => p.Id));
        Assert.Equal(then.Id, ShotPhotos.Baseline(shot, all)!.Id);
        // The bytes stay in the record.
        Assert.True(await db.Get<IArtifactBlobStore>().ExistsAsync(now.ArtifactId, Ct));

        // A new day writes a version that keeps the photo; the same day again writes nothing.
        var moved = await photos.ChangeAsync(then.Id.ToString(), day: today.AddDays(-29), cancellationToken: Ct);
        Assert.Equal((then.Id, then.ArtifactId, today.AddDays(-29), then.AddedAt), (moved.Id, moved.ArtifactId, moved.Day, moved.AddedAt));
        Assert.NotEqual(then.EventId, moved.EventId);
        Assert.Equal(moved.EventId, (await photos.ChangeAsync(then.Id.ToString(), day: today.AddDays(-29), cancellationToken: Ct)).EventId);

        // A removed photo's bytes added again are a new photo, pointing at the bytes already stored.
        var back = await photos.AddAsync("Office", nowBytes, cancellationToken: Ct);
        Assert.NotEqual(now.Id, back.Id);
        Assert.Equal(now.ArtifactId, back.ArtifactId);
        Assert.Equal(2, (await repository.GetPhotosAsync(office.Id, Ct)).Count);
    }

    [Fact]
    public async Task Shots_are_added_renamed_and_removed_and_photos_move_between_them()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var spaces = db.Get<SpaceService>();
        var photos = db.Get<SpacePhotoService>();
        var kitchen = await spaces.AddAsync("Kitchen", 4, 5, Ct);

        var withCounters = await spaces.AddShotAsync("Kitchen", "Counters", Ct);
        Assert.Equal(["Kitchen", "Counters"], withCounters.Shots.Select(withCounters.ShotName));
        // A shot's name is unique within its space, in any case, the first shot's included.
        Assert.Contains("already has a shot called 'Kitchen'",
            (await Assert.ThrowsAsync<InvalidOperationException>(() => spaces.AddShotAsync("Kitchen", "kitchen", Ct))).Message);
        await Assert.ThrowsAsync<ArgumentException>(() => spaces.AddShotAsync("Kitchen", "  ", Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => spaces.AddShotAsync("Kitchen", new string('x', 41), Ct));

        var renamed = await spaces.RenameShotAsync("Kitchen", SpaceShot.MainId, "Table", Ct);
        Assert.Equal(["Table", "Counters"], renamed.Shots.Select(renamed.ShotName));
        var countersId = renamed.Shots[1].Id;

        // A photo goes to a shot by its name or id; moving it takes it to another.
        var counters = await photos.AddAsync("Kitchen", FoodPhotoTests.Jpeg(), shot: "counters", cancellationToken: Ct);
        Assert.Equal(countersId, counters.ShotId);
        var table = await photos.AddAsync("Kitchen", FoodPhotoTests.Jpeg(), cancellationToken: Ct);
        Assert.Equal(SpaceShot.MainId, table.ShotId);
        var moved = await photos.ChangeAsync(table.Id.ToString(), shot: countersId, cancellationToken: Ct);
        Assert.Equal(countersId, moved.ShotId);
        Assert.Equal(2, (await photos.ListAsync("Kitchen", "Counters", Ct)).Photos.Count);

        // Removing a shot keeps its photos in the record, out of the list.
        var removed = await spaces.RemoveShotAsync("Kitchen", "Counters", Ct);
        Assert.Equal(["Table"], removed.Shots.Select(removed.ShotName));
        Assert.Empty((await photos.ListAsync("Kitchen", cancellationToken: Ct)).Photos);
        Assert.Equal(2, (await db.Get<ISpaceRepository>().GetPhotosAsync(kitchen.Id, Ct)).Count);
        // Such a photo can't be a baseline until it's moved to a shot again.
        await Assert.ThrowsAsync<InvalidOperationException>(() => photos.SetBaselineAsync(moved.Id.ToString(), Ct));
        await photos.ChangeAsync(moved.Id.ToString(), shot: "Table", cancellationToken: Ct);
        Assert.Equal([moved.Id], (await photos.ListAsync("Kitchen", cancellationToken: Ct)).Photos.Select(p => p.Id));

        // A space keeps at least one shot, and has at most six.
        Assert.Contains("only shot",
            (await Assert.ThrowsAsync<InvalidOperationException>(() => spaces.RemoveShotAsync("Kitchen", "Table", Ct))).Message);
        for (var i = 1; i <= 5; i++)
        {
            await spaces.AddShotAsync("Kitchen", $"Spot {i}", Ct);
        }

        Assert.Contains("the most a space can have",
            (await Assert.ThrowsAsync<InvalidOperationException>(() => spaces.AddShotAsync("Kitchen", "Spot 6", Ct))).Message);

        // Only bsk space writes the shots.
        var refused = await Assert.ThrowsAsync<ArgumentException>(() => db.Get<AttributeService>().SetAsync(
            kitchen.Urn, [new AttributeAssignment("shots", "[]")], Ct));
        Assert.Contains("bsk space shot", refused.Message);
        Assert.Equal(6, (await spaces.ResolveAsync("Kitchen", Ct)).Shots.Count);
    }

    [Fact]
    public async Task Bad_photos_are_refused_in_words_with_nothing_stored()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var spaces = db.Get<SpaceService>();
        var photos = db.Get<SpacePhotoService>();
        var today = await db.TodayAsync();
        await spaces.AddAsync("Office", cancellationToken: Ct);

        foreach (var (add, words) in new (Func<Task>, string)[]
                 {
                     (() => photos.AddAsync("Office", FoodPhotoTests.Jpeg(), today.AddDays(1), cancellationToken: Ct), "today or earlier"),
                     (() => photos.AddAsync("Office", "GIF89a and more"u8.ToArray(), cancellationToken: Ct), "isn't a photo"),
                     (() => photos.AddAsync("Office", [], cancellationToken: Ct), "empty"),
                     (() => photos.AddAsync("Office", FoodPhotoTests.Jpeg(PhotoChecks.MaxBytes + 1), cancellationToken: Ct), "at most 5 MB"),
                     (() => photos.AddAsync("Office", FoodPhotoTests.Jpeg(), shot: "Window", cancellationToken: Ct), "no shot called 'Window'")
                 })
        {
            Assert.Contains(words, (await Assert.ThrowsAsync<ArgumentException>(add)).Message);
        }

        await Assert.ThrowsAsync<SubjectNotFoundException>(() => photos.AddAsync("Attic", FoodPhotoTests.Jpeg(), cancellationToken: Ct));
        Assert.Contains("Say what to change",
            (await Assert.ThrowsAsync<ArgumentException>(() => photos.ChangeAsync("abcd1234", cancellationToken: Ct))).Message);
        Assert.Contains("isn't a photo's id",
            (await Assert.ThrowsAsync<ArgumentException>(() => photos.RemoveAsync("xyz", Ct))).Message);

        await spaces.ArchiveAsync("Office", Ct);
        Assert.Contains("archived",
            (await Assert.ThrowsAsync<InvalidOperationException>(
                () => photos.AddAsync("Office", FoodPhotoTests.Jpeg(), cancellationToken: Ct))).Message);
        Assert.Equal(0L, await PhotoEventCountAsync(db));

        // A host without photo storage refuses photos in words.
        await using var refusing = new ServiceCollection()
            .AddLifeOsKernel(db.ConnectionString, homeTimeZone: DomainTestDatabase.Zone, allowPhotosInDatabase: false)
            .BuildServiceProvider();
        var off = refusing.GetRequiredService<SpacePhotoService>();
        Assert.False(off.Enabled);
        Assert.Equal(PhotoChecks.NotSetUp, (await Assert.ThrowsAsync<InvalidOperationException>(
            () => off.AddAsync("Office", FoodPhotoTests.Jpeg(), cancellationToken: Ct))).Message);
        Assert.Equal(0L, await PhotoEventCountAsync(db));
    }

    [Fact]
    public async Task The_views_read_hand_written_photos_and_shots_tolerantly()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var office = await db.Get<SpaceService>().AddAsync("Office", 5, 5, Ct);
        var task = await db.Get<SubjectService>().CreateAsync(SubjectTypes.Task, "Not a space", "{}", cancellationToken: Ct);
        var real = await db.Get<SpacePhotoService>().AddAsync("Office", FoodPhotoTests.Jpeg(), cancellationToken: Ct);
        await using var connection = await OpenAsync(db);

        // A photo of a non-space, on no day, with no photo, or naming bytes not stored drops out;
        // a photo with no shot reads as the first shot.
        var good = Guid.NewGuid();
        await AppendAsync(connection, "observation", new { id = Guid.NewGuid(), space = task.Id, day = "2026-10-01", photo = real.ArtifactId });
        await AppendAsync(connection, "observation", new { id = Guid.NewGuid(), space = office.Id, day = "not a day", photo = real.ArtifactId });
        await AppendAsync(connection, "observation", new { id = Guid.NewGuid(), space = office.Id, day = "2026-10-01" });
        await AppendAsync(connection, "observation", new { id = Guid.NewGuid(), space = office.Id, day = "2026-10-01", photo = Guid.NewGuid() });
        await AppendAsync(connection, "measurement", new { id = Guid.NewGuid(), space = office.Id, day = "2026-10-01", photo = real.ArtifactId });
        await AppendAsync(connection, "observation", new { id = good, space = office.Id, day = "2026-10-01", photo = real.ArtifactId });
        var photos = await db.Get<ISpaceRepository>().GetPhotosAsync(office.Id, Ct);
        Assert.Equal([(good, SpaceShot.MainId), (real.Id, SpaceShot.MainId)], photos.Select(p => (p.Id, p.ShotId)));

        // A hand-edited shots attribute: an entry with no id is skipped, a repeated id keeps its
        // first entry, a blank name reads as the space's and a baseline that isn't an id as none.
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE bsk.subject SET attributes = jsonb_build_object('shots', '[
                {"id": "main", "name": "  "}, {"name": "No id"},
                {"id": "desk", "name": "Desk", "baseline": "not-an-id"}, {"id": "DESK", "name": "Again"}]'::jsonb)
            WHERE id = @id;
            """,
            new { id = office.Id }, cancellationToken: Ct));
        var read = (await db.Get<ISpaceRepository>().GetSpaceAsync(office.Id, Ct))!;
        Assert.Equal([("main", "Office", (Guid?)null), ("desk", "Desk", null)], read.Shots.Select(s => (s.Id, read.ShotName(s), s.Baseline)));

        // Shots that aren't an array read as the one first shot.
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE bsk.subject SET attributes = '{\"shots\": \"Desk\"}' WHERE id = @id;", new { id = office.Id }, cancellationToken: Ct));
        Assert.Equal([SpaceShot.MainId], (await db.Get<ISpaceRepository>().GetSpaceAsync(office.Id, Ct))!.Shots.Select(s => s.Id));
    }

    [Fact]
    public async Task The_endpoints_add_list_move_pick_and_remove_photos_and_manage_shots()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var today = await db.TodayAsync();
        await using var host = await ApiTestHost.StartAsync(db.ConnectionString, Ct, DomainTestDatabase.ApiZone);

        var office = await JsonAsync(await host.Client.PostAsJsonAsync("/api/environment/spaces", new { name = "Office", weight = 5, pace = 5 }, Ct));
        var officeId = office.GetProperty("id").GetString()!;
        // Rated a month ago and today, so each photo's day has a score: 30 then, 95 now.
        await JsonAsync(await host.Client.PostAsJsonAsync("/api/environment/ratings", new
        {
            day = Day(today.AddDays(-30)),
            ratings = new[] { new { space = officeId, tidiness = 2, cleanliness = 2, aesthetics = 3 } }
        }, Ct));
        await JsonAsync(await host.Client.PostAsJsonAsync("/api/environment/ratings", new
        {
            ratings = new[] { new { space = officeId, tidiness = 5, cleanliness = 5, aesthetics = 4 } }
        }, Ct));

        var withDesk = await JsonAsync(await host.Client.PostAsJsonAsync($"/api/environment/spaces/{officeId}/shots", new { name = "Desk" }, Ct));
        Assert.Equal(["Office", "Desk"], withDesk.GetProperty("shots").EnumerateArray().Select(s => s.GetProperty("name").GetString()));
        var deskId = withDesk.GetProperty("shots")[1].GetProperty("id").GetString()!;

        // Then: the desk a month ago, its baseline.
        var bytes = FoodPhotoTests.Jpeg(150_000);
        var then = (await JsonAsync(await UploadAsync(host, officeId, bytes, Day(today.AddDays(-30)), "desk"))).GetProperty("photo");
        Assert.Equal(("Desk", Day(today.AddDays(-30)), true, 30m), (then.GetProperty("shot").GetString(),
            then.GetProperty("day").GetString(), then.GetProperty("baseline").GetBoolean(), then.GetProperty("score").GetDecimal()));
        var content = await host.Client.GetAsync($"/api/artifacts/{then.GetProperty("artifactId").GetString()}/content", Ct);
        Assert.Equal(bytes, await content.Content.ReadAsByteArrayAsync(Ct));

        // Now: the desk today.
        var now = (await JsonAsync(await UploadAsync(host, officeId, FoodPhotoTests.Jpeg(), shot: deskId))).GetProperty("photo");
        Assert.Equal((Day(today), false, 95m), (now.GetProperty("day").GetString(), now.GetProperty("baseline").GetBoolean(),
            now.GetProperty("score").GetDecimal()));
        var thenId = then.GetProperty("id").GetString();
        var nowId = now.GetProperty("id").GetString();

        // The home read shows each shot's Then and Now.
        var home = await host.Client.GetFromJsonAsync<JsonElement>("/api/environment", Ct);
        var shots = home.GetProperty("spaces")[0].GetProperty("shots");
        Assert.Equal((0, JsonValueKind.Null), (shots[0].GetProperty("photos").GetInt32(), shots[0].GetProperty("baseline").ValueKind));
        var desk = shots[1];
        Assert.Equal(("Desk", 2, thenId, nowId, 30m, 95m), (desk.GetProperty("name").GetString(), desk.GetProperty("photos").GetInt32(),
            desk.GetProperty("baseline").GetProperty("id").GetString(), desk.GetProperty("newest").GetProperty("id").GetString(),
            desk.GetProperty("baseline").GetProperty("score").GetDecimal(), desk.GetProperty("newest").GetProperty("score").GetDecimal()));
        Assert.Equal(100m, desk.GetProperty("newest").GetProperty("parts").GetProperty("tidiness").GetProperty("now").GetDecimal());

        // The list, newest first; a shot with none lists none.
        var list = await host.Client.GetFromJsonAsync<JsonElement>($"/api/environment/spaces/{officeId}/photos", Ct);
        Assert.Equal([nowId, thenId], list.GetProperty("photos").EnumerateArray().Select(p => p.GetProperty("id").GetString()));
        var mainOnly = await host.Client.GetFromJsonAsync<JsonElement>($"/api/environment/spaces/Office/photos?shot=Office", Ct);
        Assert.Equal(0, mainOnly.GetProperty("photos").GetArrayLength());

        // Now becomes the baseline; Then moves to the first shot; Now is removed.
        var picked = await JsonAsync(await host.Client.PostAsync($"/api/environment/photos/{nowId}/baseline", null, Ct));
        Assert.True(picked.GetProperty("photo").GetProperty("baseline").GetBoolean());
        Assert.Equal(nowId, picked.GetProperty("shots")[1].GetProperty("baseline").GetProperty("id").GetString());
        var moved = await JsonAsync(await host.Client.PostAsJsonAsync($"/api/environment/photos/{thenId}", new { shot = "Office" }, Ct));
        Assert.Equal((SpaceShot.MainId, 1), (moved.GetProperty("photo").GetProperty("shotId").GetString(),
            moved.GetProperty("shots")[0].GetProperty("photos").GetInt32()));
        var removed = await JsonAsync(await host.Client.PostAsync($"/api/environment/photos/{nowId}/delete", null, Ct));
        Assert.Equal((JsonValueKind.Null, 0), (removed.GetProperty("photo").ValueKind, removed.GetProperty("shots")[1].GetProperty("photos").GetInt32()));

        // The desk shot is renamed, then removed.
        var renamed = await JsonAsync(await host.Client.PostAsJsonAsync($"/api/environment/spaces/{officeId}/shots/{deskId}", new { name = "Window" }, Ct));
        Assert.Equal("Window", renamed.GetProperty("shots")[1].GetProperty("name").GetString());
        var one = await JsonAsync(await host.Client.PostAsync($"/api/environment/spaces/{officeId}/shots/Window/delete", null, Ct));
        Assert.Equal(1, one.GetProperty("shots").GetArrayLength());

        // Refusals are 400s in words.
        foreach (var (response, words) in new (Func<Task<HttpResponseMessage>>, string)[]
                 {
                     (() => UploadAsync(host, officeId, FoodPhotoTests.Jpeg(), field: "file"), "field named photo"),
                     (() => UploadAsync(host, officeId, FoodPhotoTests.Jpeg(), Day(today.AddDays(1))), "today or earlier"),
                     (() => UploadAsync(host, officeId, FoodPhotoTests.Jpeg(), "27/10/2026"), "yyyy-MM-dd"),
                     (() => UploadAsync(host, officeId, FoodPhotoTests.Jpeg(), shot: "Shelf"), "no shot called 'Shelf'"),
                     (() => UploadAsync(host, officeId, "GIF89a and more"u8.ToArray()), "isn't a photo"),
                     (() => host.Client.PostAsync($"/api/environment/spaces/{officeId}/shots/Office/delete", null, Ct), "only shot"),
                     (() => host.Client.PostAsJsonAsync($"/api/environment/photos/{thenId}", new { }, Ct), "Say what to change")
                 })
        {
            var answer = await response();
            Assert.Equal(HttpStatusCode.BadRequest, answer.StatusCode);
            Assert.Contains(words, await answer.Content.ReadAsStringAsync(Ct));
        }

        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync("/api/environment/spaces/Attic/photos", Ct)).StatusCode);
    }

    [Fact]
    public void Then_is_the_picked_photo_while_it_is_the_shots_and_Now_the_newest()
    {
        var space = Guid.NewGuid();
        SpacePhoto Photo(string shot, int day, int added) => new(
            Guid.NewGuid(), space, shot, new DateOnly(2026, 10, day), Guid.NewGuid(),
            new DateTimeOffset(2026, 10, 27, 12, added, 0, TimeSpan.Zero), DateTimeOffset.UnixEpoch, Guid.NewGuid());
        var morning = Photo("main", 20, 1);
        var evening = Photo("main", 20, 2);
        var later = Photo("main", 25, 0);
        var elsewhere = Photo("desk", 1, 0);
        var photos = new[] { later, elsewhere, evening, morning };

        var main = new SpaceShot("main");
        Assert.Equal((morning, later), (ShotPhotos.Baseline(main, photos), ShotPhotos.Newest(main, photos)));
        Assert.Equal(evening, ShotPhotos.Baseline(main with { Baseline = evening.Id }, photos));
        // A picked photo that's gone, or on another shot, falls back to the first.
        Assert.Equal(morning, ShotPhotos.Baseline(main with { Baseline = elsewhere.Id }, photos));
        Assert.Null(ShotPhotos.Baseline(new SpaceShot("window"), photos));

        // The shots JSON round-trips names and baselines only where set.
        var shots = new[] { new SpaceShot("main"), new SpaceShot("desk", "Desk", evening.Id) };
        Assert.Equal(shots, SpaceShotsJson.Parse(SpaceShotsJson.ToJson(shots).ToJsonString()));
        Assert.Equal($$"""[{"id":"main"},{"id":"desk","name":"Desk","baseline":"{{evening.Id}}"}]""", SpaceShotsJson.ToJson(shots).ToJsonString());
        Assert.Empty(SpaceShotsJson.Parse("not json"));
    }

    private Task<HttpResponseMessage> UploadAsync(
        ApiTestHost host, string space, byte[] bytes, string? day = null, string? shot = null, string field = "photo")
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(file, field, "photo.jpg");
        if (day is not null)
        {
            form.Add(new StringContent(day), "day");
        }

        if (shot is not null)
        {
            form.Add(new StringContent(shot), "shot");
        }

        return host.Client.PostAsync($"/api/environment/spaces/{space}/photos", form, Ct);
    }

    private async Task<NpgsqlConnection> OpenAsync(DomainTestDatabase db)
    {
        var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync(Ct);
        return connection;
    }

    private async Task AppendAsync(NpgsqlConnection connection, string kind, object photo)
    {
        var payload = JsonSerializer.Serialize(new Dictionary<string, object> { [SpaceLimits.PhotoKey] = photo });
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO bsk.event (kind, provenance, occurred_at, recorded_at, source_id, payload)
            VALUES (@kind, 'declared', now(), now(), 'cli', @payload::jsonb);
            """,
            new { kind, payload }, cancellationToken: Ct));
    }

    private async Task<long> PhotoEventCountAsync(DomainTestDatabase db)
    {
        await using var connection = await OpenAsync(db);
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM bsk.event WHERE jsonb_exists(payload, 'space_photo');", cancellationToken: Ct));
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
