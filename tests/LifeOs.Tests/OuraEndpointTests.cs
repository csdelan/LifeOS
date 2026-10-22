using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using LifeOs.Application.Abstractions;
using LifeOs.Infrastructure;
using LifeOs.Infrastructure.Integrations;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Tests;

/// <summary>
/// The Oura connection over HTTP (ACTIVITY-1, activity plan A1, §3.1): <c>/api/oura/status</c>,
/// <c>/authorize</c>, <c>/connect</c> and <c>/disconnect</c>, with Oura played by
/// <see cref="FakeOuraClient"/>. Each test has a database of its own. No answer ever carries a
/// token.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OuraEndpointTests(PostgresFixture postgres)
{
    private const string Redirect = "https://lifeos.example/activity/connected";

    private static readonly Dictionary<string, string?> WithTokenKey = new(DomainTestDatabase.ApiZone)
    {
        [TokenCipher.KeyKey] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
    };

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Task<ApiTestHost> StartWithOuraAsync(DomainTestDatabase db, FakeOuraClient oura, CancellationToken ct)
        => ApiTestHost.StartAsync(db.ConnectionString, ct, WithTokenKey, services => services.AddSingleton<IOuraClient>(oura));

    private async Task<JsonElement> JsonAsync(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.StatusCode == expected, $"{(int)response.StatusCode}: {body}");
        Assert.DoesNotContain("access-", body, StringComparison.Ordinal);
        Assert.DoesNotContain("refresh-", body, StringComparison.Ordinal);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    [Fact]
    public async Task Without_an_oura_app_the_status_says_not_configured()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var host = await ApiTestHost.StartAsync(db.ConnectionString, Ct, DomainTestDatabase.ApiZone);

        var status = await JsonAsync(await host.Client.GetAsync("/api/oura/status", Ct));
        Assert.Equal("not_configured", status.GetProperty("state").GetString());
        Assert.False(status.GetProperty("configured").GetBoolean());

        var refused = await JsonAsync(
            await host.Client.PostAsJsonAsync("/api/oura/authorize", new { redirectUri = Redirect }, Ct),
            HttpStatusCode.Conflict);
        Assert.Contains("Oura__ClientId", refused.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Connecting_and_disconnecting_over_http()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var oura = new FakeOuraClient(new SystemClock());
        await using var host = await StartWithOuraAsync(db, oura, Ct);

        var before = await JsonAsync(await host.Client.GetAsync("/api/oura/status", Ct));
        Assert.Equal("not_connected", before.GetProperty("state").GetString());

        var authorize = await JsonAsync(
            await host.Client.PostAsJsonAsync("/api/oura/authorize", new { redirectUri = Redirect }, Ct));
        Assert.Contains(oura.AuthorizedState!, authorize.GetProperty("authorizeUrl").GetString());

        var connected = await JsonAsync(await host.Client.PostAsJsonAsync(
            "/api/oura/connect", new { code = "code-1", state = oura.AuthorizedState }, Ct));
        Assert.Equal("connected", connected.GetProperty("state").GetString());
        Assert.Equal(["daily", "workout"], connected.GetProperty("scopes").EnumerateArray().Select(s => s.GetString()));
        Assert.Empty(connected.GetProperty("missingScopes").EnumerateArray());

        var status = await JsonAsync(await host.Client.GetAsync("/api/oura/status", Ct));
        Assert.True(status.GetProperty("connected").GetBoolean());

        var disconnected = await JsonAsync(await host.Client.PostAsync("/api/oura/disconnect", null, Ct));
        Assert.Equal("not_connected", disconnected.GetProperty("state").GetString());
        Assert.Equal(["access-1"], oura.Revoked);
    }

    [Fact]
    public async Task Bad_requests_are_refused_in_words()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var oura = new FakeOuraClient(new SystemClock());
        await using var host = await StartWithOuraAsync(db, oura, Ct);

        var noRedirect = await JsonAsync(
            await host.Client.PostAsJsonAsync("/api/oura/authorize", new { }, Ct), HttpStatusCode.BadRequest);
        Assert.Contains("redirectUri", noRedirect.GetProperty("error").GetString());

        var elsewhere = await JsonAsync(await host.Client.PostAsJsonAsync(
            "/api/oura/authorize", new { redirectUri = "https://evil.example/steal" }, Ct), HttpStatusCode.BadRequest);
        Assert.Contains("/activity/connected", elsewhere.GetProperty("error").GetString());

        await JsonAsync(await host.Client.PostAsJsonAsync("/api/oura/authorize", new { redirectUri = Redirect }, Ct));
        var wrongState = await JsonAsync(await host.Client.PostAsJsonAsync(
            "/api/oura/connect", new { code = "code-1", state = "forged" }, Ct), HttpStatusCode.Conflict);
        Assert.Contains("Connect Oura again", wrongState.GetProperty("error").GetString());

        var noCode = await JsonAsync(await host.Client.PostAsJsonAsync(
            "/api/oura/connect", new { state = oura.AuthorizedState }, Ct), HttpStatusCode.BadRequest);
        Assert.Contains("code", noCode.GetProperty("error").GetString());
    }

    [Fact]
    public async Task A_client_id_without_a_token_key_fails_startup()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var settings = new Dictionary<string, string?>(DomainTestDatabase.ApiZone)
        {
            [OuraOptions.ClientIdKey] = "client-123",
            [OuraOptions.ClientSecretKey] = "secret-456",
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => ApiTestHost.StartAsync(db.ConnectionString, Ct, settings));

        Assert.Contains(TokenCipher.KeyKey, ex.Message);
        Assert.DoesNotContain("secret-456", ex.Message);
    }
}
