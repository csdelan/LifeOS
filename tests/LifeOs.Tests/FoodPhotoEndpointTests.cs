using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Food;
using LifeOs.Application.Photos;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Tests;

/// <summary>
/// FOOD-5 over HTTP (food-log plan F6, §3.8): <c>POST /api/food/meals/{id}/photo</c> takes a
/// multipart upload, <c>/photo/delete</c> removes it, and <c>GET /api/artifacts/{id}/content</c>
/// serves the bytes to the owner, cached for good and revalidated by hash. Each test has a
/// database of its own (UTC).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class FoodPhotoEndpointTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<Guid> LogAsync(ApiTestHost host, string name = "Salmon salad")
    {
        var response = await host.Client.PostAsJsonAsync("/api/food/meals", new { name, score = 5, calories = 650 }, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("meal").GetProperty("id").GetGuid();
    }

    private Task<HttpResponseMessage> UploadAsync(
        ApiTestHost host, Guid meal, byte[] bytes, string field = "photo", string contentType = "image/jpeg")
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, field, "photo.jpg");
        return host.Client.PostAsync($"/api/food/meals/{meal}/photo", form, Ct);
    }

    private async Task<string> RefusedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("error").GetString()!;
    }

    [Fact]
    public async Task An_uploaded_photo_is_served_back_byte_for_byte_survives_an_edit_and_comes_off()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var host = await ApiTestHost.StartAsync(db.ConnectionString, Ct, DomainTestDatabase.ApiZone);
        var meal = await LogAsync(host);
        var bytes = FoodPhotoTests.Jpeg(150_000);

        var upload = await UploadAsync(host, meal, bytes);
        Assert.True(upload.IsSuccessStatusCode, await upload.Content.ReadAsStringAsync(Ct));
        var set = await upload.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("photo_set", set.GetProperty("action").GetString());
        var photo = set.GetProperty("meal").GetProperty("photoArtifactId").GetGuid();

        // Served to the owner, cached for good, its ETag the hash.
        var content = await host.Client.GetAsync($"/api/artifacts/{photo}/content", Ct);
        Assert.Equal(HttpStatusCode.OK, content.StatusCode);
        Assert.Equal(bytes, await content.Content.ReadAsByteArrayAsync(Ct));
        Assert.Equal("image/jpeg", content.Content.Headers.ContentType!.MediaType);
        var cache = content.Headers.CacheControl!;
        Assert.Equal((true, TimeSpan.FromDays(365)), (cache.Private, cache.MaxAge));
        Assert.Contains(cache.Extensions, e => e.Name == "immutable");
        var etag = content.Headers.ETag!;
        Assert.Equal($"\"{Convert.ToHexStringLower(SHA256.HashData(bytes))}\"", etag.Tag);
        Assert.Null(content.Content.Headers.ContentDisposition);

        // Asked again with its ETag: 304, no bytes.
        using (var again = new HttpRequestMessage(HttpMethod.Get, $"/api/artifacts/{photo}/content"))
        {
            again.Headers.IfNoneMatch.Add(etag);
            var notModified = await host.Client.SendAsync(again, Ct);
            Assert.Equal(HttpStatusCode.NotModified, notModified.StatusCode);
            Assert.Empty(await notModified.Content.ReadAsByteArrayAsync(Ct));
        }

        // The day reads it; an edit keeps it.
        var day = await host.Client.GetFromJsonAsync<JsonElement>("/api/food/day", Ct);
        Assert.Equal(photo, Assert.Single(day.GetProperty("meals").EnumerateArray()).GetProperty("photoArtifactId").GetGuid());
        var edit = await host.Client.PostAsJsonAsync($"/api/food/meals/{meal}", new { name = "Salmon salad", score = 4, calories = 700 }, Ct);
        edit.EnsureSuccessStatusCode();
        Assert.Equal(photo, (await edit.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("meal").GetProperty("photoArtifactId").GetGuid());

        // Remove photo.
        var removed = await host.Client.PostAsync($"/api/food/meals/{meal}/photo/delete", null, Ct);
        removed.EnsureSuccessStatusCode();
        var result = await removed.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("photo_removed", result.GetProperty("action").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("meal").GetProperty("photoArtifactId").ValueKind);
        Assert.Equal("Salmon salad has no photo to remove.",
            await RefusedAsync(await host.Client.PostAsync($"/api/food/meals/{meal}/photo/delete", null, Ct)));
    }

    [Fact]
    public async Task Logging_with_a_stored_photo_shows_it_without_an_upload()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var host = await ApiTestHost.StartAsync(db.ConnectionString, Ct, DomainTestDatabase.ApiZone);
        var meal = await LogAsync(host, "Sausage burrito");
        var photo = (await (await UploadAsync(host, meal, FoodPhotoTests.Jpeg())).Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("meal").GetProperty("photoArtifactId").GetGuid();

        var again = await host.Client.PostAsJsonAsync(
            "/api/food/meals", new { name = "Sausage burrito", score = 5, calories = 650, photoArtifactId = photo }, Ct);

        Assert.True(again.IsSuccessStatusCode, await again.Content.ReadAsStringAsync(Ct));
        var copy = (await again.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("meal");
        Assert.NotEqual(meal, copy.GetProperty("id").GetGuid());
        Assert.Equal(photo, copy.GetProperty("photoArtifactId").GetGuid());
        // Log again lists the newest of the name, with the photo.
        var recent = await host.Client.GetFromJsonAsync<JsonElement>("/api/food/recent", Ct);
        Assert.Equal(photo, Assert.Single(recent.EnumerateArray()).GetProperty("photoArtifactId").GetGuid());

        // An id that isn't a stored photo is refused in words.
        Assert.StartsWith("That photo isn't stored here", await RefusedAsync(await host.Client.PostAsJsonAsync(
            "/api/food/meals", new { name = "Toast", score = 5, calories = 650, photoArtifactId = Guid.NewGuid() }, Ct)));
    }

    [Fact]
    public async Task Oversized_and_non_image_uploads_are_refused_in_words()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var host = await ApiTestHost.StartAsync(db.ConnectionString, Ct, DomainTestDatabase.ApiZone);
        var meal = await LogAsync(host);

        Assert.Contains("at most 5 MB", await RefusedAsync(await UploadAsync(host, meal, FoodPhotoTests.Jpeg(PhotoChecks.MaxBytes + 1))));
        // A PDF claiming to be a JPEG: the bytes decide.
        Assert.Contains("JPEG, PNG or WebP", await RefusedAsync(await UploadAsync(host, meal, "%PDF-1.7 menu"u8.ToArray())));
        Assert.Contains("field named photo", await RefusedAsync(await UploadAsync(host, meal, FoodPhotoTests.Jpeg(), field: "picture")));
        Assert.Contains("No food entry", await RefusedAsync(await UploadAsync(host, Guid.NewGuid(), FoodPhotoTests.Jpeg())));

        var day = await host.Client.GetFromJsonAsync<JsonElement>("/api/food/day", Ct);
        Assert.Equal(JsonValueKind.Null, Assert.Single(day.GetProperty("meals").EnumerateArray()).GetProperty("photoArtifactId").ValueKind);
    }

    [Fact]
    public async Task Content_is_the_owners_only_and_an_unknown_artifact_is_not_found()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var host = await ApiTestHost.StartAsync(db.ConnectionString, Ct, DomainTestDatabase.ApiZone);

        var missing = await host.Client.GetAsync($"/api/artifacts/{Guid.NewGuid()}/content", Ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        using var anonymous = new HttpClient { BaseAddress = host.Client.BaseAddress };
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/artifacts/{Guid.NewGuid()}/content", Ct)).StatusCode);
        var meal = await LogAsync(host);
        var form = new MultipartFormDataContent { { new ByteArrayContent(FoodPhotoTests.Jpeg()), "photo", "p.jpg" } };
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync($"/api/food/meals/{meal}/photo", form, Ct)).StatusCode);
    }

    [Fact]
    public async Task With_object_storage_photos_go_there_and_are_served_from_there()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var storage = new MemoryObjectStorage();
        await using var host = await ApiTestHost.StartAsync(
            db.ConnectionString, Ct, DomainTestDatabase.ApiZone, services => services.AddSingleton<IObjectStorage>(storage));
        var meal = await LogAsync(host);
        var bytes = FoodPhotoTests.Jpeg();

        var upload = await UploadAsync(host, meal, bytes);
        upload.EnsureSuccessStatusCode();
        var photo = (await upload.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("meal").GetProperty("photoArtifactId").GetGuid();

        Assert.Equal(1, storage.Uploads);
        Assert.Equal(bytes, await host.Client.GetByteArrayAsync($"/api/artifacts/{photo}/content", Ct));

        // The store failing reads as a 502 in words, not a 500.
        storage.Failing = true;
        var failed = await host.Client.GetAsync($"/api/artifacts/{photo}/content", Ct);
        Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
        Assert.Contains("down", (await failed.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("error").GetString());
    }
}
