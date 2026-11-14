using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Photos;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// GEN-15: a Person, human or AI agent, can have a photo. It's an artifact with bytes, written by
/// an <c>observation</c> event naming the Person, and <c>attributes.photo</c> holds its id, which
/// only the photo's own verb writes. It round-trips byte for byte, shows on the People list and the
/// Person's own read, is replaced by the next one and comes off with Remove; only a Person takes
/// one, and only JPEG, PNG and WebP. Each test has a database of its own (UTC).
/// </summary>
public sealed class PersonPhotoTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<SubjectRef> PersonAsync(DomainTestDatabase db, string name = "Sam", string attributes = "{}")
        => (await db.Get<SubjectCreationService>().CreateAsync(SubjectTypes.Person, name, attributes, cancellationToken: TestContext.Current.CancellationToken)).Subject;

    private async Task<NpgsqlConnection> OpenAsync(DomainTestDatabase db)
    {
        var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync(Ct);
        return connection;
    }

    [Fact]
    public async Task A_photo_round_trips_byte_for_byte_is_replaced_by_the_next_and_comes_off()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var photos = db.Get<PersonPhotoService>();
        var sam = await PersonAsync(db);
        var bytes = FoodPhotoTests.Jpeg();

        var set = await photos.SetAsync(sam.Urn, bytes, @"C:\phone\IMG_0007.jpg", Ct);

        Assert.Equal(sam.Id, set.Person.Id);
        var photo = set.ArtifactId!.Value;
        var blob = (await db.Get<IArtifactBlobStore>().GetAsync(photo, Ct))!;
        Assert.Equal(bytes, blob.Bytes);
        Assert.Equal(("image/jpeg", "IMG_0007.jpg"), (blob.ContentType, blob.Filename));
        Assert.Equal(photo.ToString(), await db.Get<ISubjectRepository>().GetAttributeValueAsync(sam.Id, PersonPhotoService.AttributeKey, Ct));

        // The photo's own event: an observation naming the Person, about no subject and in no inbox.
        await using (var connection = await OpenAsync(db))
        {
            var evt = await connection.QuerySingleAsync<(string Kind, string Payload, bool Inbox)>(new CommandDefinition(
                """
                SELECT e.kind, e.payload::text,
                       EXISTS (SELECT 1 FROM bsk.v_inbox i WHERE i.item_id = e.id)
                FROM bsk.event e WHERE e.artifact_id = @photo;
                """,
                new { photo }, cancellationToken: Ct));
            Assert.Equal("observation", evt.Kind);
            Assert.Equal(sam.Id, JsonDocument.Parse(evt.Payload).RootElement.GetProperty("person_photo").GetProperty("person").GetGuid());
            Assert.False(evt.Inbox);
        }

        // The same bytes again reuse the stored artifact; other bytes replace it.
        Assert.Equal(photo, (await photos.SetAsync(sam.Id.ToString(), bytes, cancellationToken: Ct)).ArtifactId);
        var other = (await photos.SetAsync("Sam", FoodPhotoTests.Jpeg(), cancellationToken: Ct)).ArtifactId!.Value;
        Assert.NotEqual(photo, other);

        // Removing it clears the attribute and leaves the bytes in the record; again is a no-op.
        Assert.Null((await photos.RemoveAsync(sam.Urn, Ct)).ArtifactId);
        Assert.Null(await db.Get<ISubjectRepository>().GetAttributeValueAsync(sam.Id, PersonPhotoService.AttributeKey, Ct));
        Assert.NotNull(await db.Get<IArtifactBlobStore>().GetAsync(other, Ct));
        Assert.Null((await photos.RemoveAsync(sam.Urn, Ct)).ArtifactId);
    }

    [Fact]
    public async Task Only_a_person_takes_a_photo_and_only_a_photo_and_nothing_is_stored_otherwise()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var photos = db.Get<PersonPhotoService>();
        var sam = await PersonAsync(db);
        var task = (await db.Get<SubjectCreationService>().CreateAsync(SubjectTypes.Task, "Paint the fence", "{}", cancellationToken: Ct)).Subject;

        Assert.Equal("'Paint the fence' is a Task; only People and AI agents have a photo.",
            (await Assert.ThrowsAsync<InvalidOperationException>(() => photos.SetAsync(task.Urn, FoodPhotoTests.Jpeg(), cancellationToken: Ct))).Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => photos.RemoveAsync(task.Urn, Ct));
        Assert.Contains("JPEG, PNG or WebP", (await Assert.ThrowsAsync<ArgumentException>(
            () => photos.SetAsync(sam.Urn, "%PDF-1.7 not a photo"u8.ToArray(), "sam.jpg", Ct))).Message);
        Assert.Contains("at most 5 MB", (await Assert.ThrowsAsync<ArgumentException>(
            () => photos.SetAsync(sam.Urn, FoodPhotoTests.Jpeg(PhotoChecks.MaxBytes + 1), cancellationToken: Ct))).Message);
        await Assert.ThrowsAsync<SubjectNotFoundException>(() => photos.SetAsync(Guid.NewGuid().ToString(), FoodPhotoTests.Jpeg(), cancellationToken: Ct));

        await using var connection = await OpenAsync(db);
        Assert.Equal(0, await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM bsk.artifact_blob;", cancellationToken: Ct)));
    }

    [Fact]
    public async Task No_generic_attribute_write_sets_a_persons_photo()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var sam = await PersonAsync(db);
        const string refusal = "A Person's photo is set with `bsk person photo` or on its page, not `bsk set`.";

        Assert.Equal(refusal, (await Assert.ThrowsAsync<ArgumentException>(() => db.Get<AttributeService>().SetAsync(
            sam.Urn, [new AttributeAssignment("photo", Guid.NewGuid().ToString())], Ct))).Message);
        Assert.Equal(refusal, (await Assert.ThrowsAsync<ArgumentException>(
            () => PersonAsync(db, "Alex", $$"""{"photo": "{{Guid.NewGuid()}}"}"""))).Message);
        // Another type's own photo attribute is its business.
        Assert.Null(ReservedAttributes.RefusalFor(SubjectTypes.Task, "photo"));
    }

    [Fact]
    public async Task A_host_without_photo_storage_refuses_a_photo_in_words_and_still_removes_one()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var sam = await PersonAsync(db);
        await db.Get<PersonPhotoService>().SetAsync(sam.Urn, FoodPhotoTests.Jpeg(), cancellationToken: Ct);
        await using var refusing = new ServiceCollection()
            .AddLifeOsKernel(db.ConnectionString, homeTimeZone: DomainTestDatabase.Zone, allowPhotosInDatabase: false)
            .BuildServiceProvider();
        var photos = refusing.GetRequiredService<PersonPhotoService>();

        Assert.False(photos.Enabled);
        Assert.Equal(PhotoChecks.NotSetUp, (await Assert.ThrowsAsync<InvalidOperationException>(
            () => photos.SetAsync(sam.Urn, FoodPhotoTests.Jpeg(), cancellationToken: Ct))).Message);
        Assert.Null((await photos.RemoveAsync(sam.Urn, Ct)).ArtifactId);
    }

    [Fact]
    public async Task Over_http_the_photo_is_uploaded_listed_with_its_person_served_and_removed()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var host = await ApiTestHost.StartAsync(db.ConnectionString, Ct, DomainTestDatabase.ApiZone);
        var sam = await PersonAsync(db);
        var bytes = FoodPhotoTests.Jpeg(150_000);

        var upload = await UploadAsync(host, sam.Id, bytes);
        Assert.True(upload.IsSuccessStatusCode, await upload.Content.ReadAsStringAsync(Ct));
        var set = await upload.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(sam.Urn, set.GetProperty("person").GetProperty("urn").GetString());
        var photo = set.GetProperty("photoArtifactId").GetGuid();

        // The People list and the Person's own read both name it, and its bytes come back.
        var people = await host.Client.GetFromJsonAsync<JsonElement>("/api/people", Ct);
        Assert.Equal(photo, Assert.Single(people.EnumerateArray()).GetProperty("photoArtifactId").GetGuid());
        var detail = await host.Client.GetFromJsonAsync<JsonElement>($"/api/subjects/{sam.Id}", Ct);
        Assert.Equal(photo, detail.GetProperty("photoArtifactId").GetGuid());
        var content = await host.Client.GetAsync($"/api/artifacts/{photo}/content", Ct);
        Assert.Equal(bytes, await content.Content.ReadAsByteArrayAsync(Ct));

        // Refused in words: no file, not a photo, not a Person.
        Assert.Equal("Send the photo as multipart/form-data, in a field named photo.",
            await RefusedAsync(await UploadAsync(host, sam.Id, bytes, field: "file")));
        Assert.Contains("JPEG, PNG or WebP", await RefusedAsync(await UploadAsync(host, sam.Id, "GIF89a"u8.ToArray())));
        var task = (await db.Get<SubjectCreationService>().CreateAsync(SubjectTypes.Task, "Paint the fence", "{}", cancellationToken: Ct)).Subject;
        Assert.Contains("only People and AI agents have a photo", await RefusedAsync(await UploadAsync(host, task.Id, bytes)));

        var removed = await host.Client.PostAsync($"/api/people/{sam.Id}/photo/delete", null, Ct);
        removed.EnsureSuccessStatusCode();
        Assert.Equal(JsonValueKind.Null, (await removed.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("photoArtifactId").ValueKind);
        people = await host.Client.GetFromJsonAsync<JsonElement>("/api/people", Ct);
        Assert.Equal(JsonValueKind.Null, Assert.Single(people.EnumerateArray()).GetProperty("photoArtifactId").ValueKind);
    }

    [Fact]
    public async Task Only_a_persons_read_carries_a_photo_and_a_malformed_one_reads_as_none()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var host = await ApiTestHost.StartAsync(db.ConnectionString, Ct, DomainTestDatabase.ApiZone);
        var sam = await PersonAsync(db);
        // A Task's attributes are its own, so one named photo isn't a Person's photo.
        var task = (await db.Get<SubjectCreationService>().CreateAsync(
            SubjectTypes.Task, "Frame the print", $$"""{"photo": "{{Guid.NewGuid()}}"}""", cancellationToken: Ct)).Subject;
        // Written below the doors, as an old client might have: not an id, so no photo, and no failed read.
        await db.Get<ISubjectRepository>().UpdateAttributesAsync(sam.Id, """{"photo": "not-an-id"}""", [], Ct);

        var taskDetail = await host.Client.GetFromJsonAsync<JsonElement>($"/api/subjects/{task.Id}", Ct);
        Assert.Equal(JsonValueKind.Null, taskDetail.GetProperty("photoArtifactId").ValueKind);
        var samDetail = await host.Client.GetFromJsonAsync<JsonElement>($"/api/subjects/{sam.Id}", Ct);
        Assert.Equal(JsonValueKind.Null, samDetail.GetProperty("photoArtifactId").ValueKind);
        var people = await host.Client.GetFromJsonAsync<JsonElement>("/api/people", Ct);
        Assert.Equal(JsonValueKind.Null, Assert.Single(people.EnumerateArray()).GetProperty("photoArtifactId").ValueKind);
    }

    private Task<HttpResponseMessage> UploadAsync(ApiTestHost host, Guid person, byte[] bytes, string field = "photo")
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(file, field, "sam.jpg");
        return host.Client.PostAsync($"/api/people/{person}/photo", form, Ct);
    }

    private async Task<string> RefusedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("error").GetString()!;
    }
}
