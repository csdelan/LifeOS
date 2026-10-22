using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Activity;
using LifeOs.Infrastructure.Integrations;
using LifeOs.Infrastructure.Persistence;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// An Oura client that plays Oura's part: it hands out numbered token pairs, accepts each refresh
/// token once (as Oura does), and can be told to refuse or fail. Refreshes can be held open so a
/// test can race two of them.
/// </summary>
internal sealed class FakeOuraClient(IClock clock) : IOuraClient
{
    private readonly ConcurrentDictionary<string, bool> liveRefreshTokens = new();
    private int issued;

    public string? AuthorizedState { get; private set; }
    public string? AuthorizedChallenge { get; private set; }
    public (string Code, string Verifier, Uri Redirect)? Exchanged { get; private set; }
    public int Refreshes;
    public List<string> Revoked { get; } = [];
    public Exception? FailNextRefresh { get; set; }
    public Exception? FailRevoke { get; set; }
    public TimeSpan RefreshDelay { get; set; }
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromHours(24);

    public Uri AuthorizeUrl(string state, string codeChallenge, Uri redirectUri)
    {
        AuthorizedState = state;
        AuthorizedChallenge = codeChallenge;
        return new Uri($"https://cloud.ouraring.com/oauth/authorize?state={state}");
    }

    public Task<OAuthTokens> ExchangeCodeAsync(
        string code, string codeVerifier, Uri redirectUri, CancellationToken cancellationToken = default)
    {
        if (code == "bad-code")
        {
            throw new OuraGrantException("invalid_grant", "Oura refused the sign-in (invalid_grant).", spentGrant: true);
        }

        Exchanged = (code, codeVerifier, redirectUri);
        return Task.FromResult(Issue(["daily", "workout"]));
    }

    public async Task<OAuthTokens> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref Refreshes);
        if (RefreshDelay > TimeSpan.Zero)
        {
            await Task.Delay(RefreshDelay, cancellationToken);
        }

        if (FailNextRefresh is { } failure)
        {
            FailNextRefresh = null;
            throw failure;
        }

        // Single-use, like Oura's: a spent token is refused.
        if (!liveRefreshTokens.TryRemove(refreshToken, out _))
        {
            throw new OuraGrantException("invalid_grant", "Oura refused the sign-in (invalid_grant).", spentGrant: true);
        }

        return Issue([]);
    }

    public Task RevokeAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        Revoked.Add(accessToken);
        return FailRevoke is { } failure ? Task.FromException(failure) : Task.CompletedTask;
    }

    private OAuthTokens Issue(IReadOnlyList<string> scopes)
    {
        var n = Interlocked.Increment(ref issued);
        var refresh = $"refresh-{n}";
        liveRefreshTokens[refresh] = true;
        return new OAuthTokens($"access-{n}", refresh, clock.UtcNow + Lifetime, scopes);
    }
}

/// <summary>
/// The Oura connection against Postgres (ACTIVITY-1, activity plan A1, §3.1): connecting with a
/// state that's checked once, tokens sealed at rest, a refresh token spent once however many
/// callers race for it, and Oura's refusals and outages each leaving the connection as they
/// should. Each test uses a service name of its own on the shared database.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OuraConnectionTests(PostgresFixture postgres)
{
    private static readonly Uri Redirect = new("https://lifeos.example/activity/connected");
    private readonly string service = $"t{Guid.NewGuid():N}";
    private readonly TimerTestClock clock = new(new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero));
    private readonly TokenCipher cipher = new(RandomNumberGenerator.GetBytes(32));

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private (OuraConnectionService Service, FakeOuraClient Oura) Create(TokenCipher? key = null)
    {
        var oura = new FakeOuraClient(clock);
        var repository = new NpgsqlIntegrationRepository(postgres.ConnectionString, key ?? cipher);
        return (new OuraConnectionService(repository, oura, clock, service), oura);
    }

    private async Task<(OuraConnectionService Service, FakeOuraClient Oura)> ConnectedAsync()
    {
        var (connection, oura) = Create();
        await connection.BeginConnectAsync(Redirect, Ct);
        await connection.ConnectAsync("code-1", oura.AuthorizedState!, Ct);
        return (connection, oura);
    }

    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        return (await connection.ExecuteScalarAsync<T>(new CommandDefinition(
            sql, new { service }, cancellationToken: Ct)))!;
    }

    [Fact]
    public async Task Connecting_stores_sealed_tokens_and_answers_connected()
    {
        var (connection, oura) = Create();

        Assert.Equal(OuraStatus.NotConnected, (await connection.GetStatusAsync(Ct)).State);
        var url = await connection.BeginConnectAsync(Redirect, Ct);
        var status = await connection.ConnectAsync("code-1", oura.AuthorizedState!, Ct);

        Assert.Contains(oura.AuthorizedState!, url.AbsoluteUri);
        Assert.Equal(OuraStatus.ConnectedState, status.State);
        Assert.True(status.Connected);
        Assert.Equal(["daily", "workout"], status.Scopes);
        Assert.Empty(status.MissingScopes);
        Assert.Equal(clock.UtcNow, status.ConnectedAt);

        // PKCE: the verifier sent with the code hashes to the challenge sent to authorize.
        var (code, verifier, redirect) = oura.Exchanged!.Value;
        Assert.Equal("code-1", code);
        Assert.Equal(Redirect, redirect);
        Assert.Equal(oura.AuthorizedChallenge, Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));

        // At rest the tokens are ciphertext, and the connect in progress is gone.
        var stored = await ScalarAsync<byte[]>("SELECT access_token_enc FROM bsk_ops.integration WHERE service = @service");
        Assert.DoesNotContain("access-1", Encoding.UTF8.GetString(stored), StringComparison.Ordinal);
        Assert.Equal("access-1", cipher.Decrypt(stored, $"{service}.access_token"));
        Assert.Equal(0L, await ScalarAsync<long>(
            "SELECT count(*) FROM bsk_ops.integration WHERE service = @service AND pending_state IS NOT NULL"));
    }

    [Fact]
    public async Task A_wrong_used_or_expired_state_is_refused()
    {
        var (connection, oura) = Create();
        await connection.BeginConnectAsync(Redirect, Ct);
        var state = oura.AuthorizedState!;

        await Assert.ThrowsAsync<OuraConnectionException>(() => connection.ConnectAsync("code-1", "not-the-state", Ct));
        await connection.ConnectAsync("code-1", state, Ct);
        // Once only.
        await Assert.ThrowsAsync<OuraConnectionException>(() => connection.ConnectAsync("code-2", state, Ct));

        await connection.BeginConnectAsync(Redirect, Ct);
        clock.Advance(OuraConnectionService.PendingFor + TimeSpan.FromSeconds(1));
        var expired = await Assert.ThrowsAsync<OuraConnectionException>(() =>
            connection.ConnectAsync("code-3", oura.AuthorizedState!, Ct));
        Assert.Contains("expired", expired.Message);
    }

    [Fact]
    public async Task A_refused_code_says_so_and_stores_nothing()
    {
        var (connection, oura) = Create();
        await connection.BeginConnectAsync(Redirect, Ct);

        var ex = await Assert.ThrowsAsync<OuraConnectionException>(() =>
            connection.ConnectAsync("bad-code", oura.AuthorizedState!, Ct));

        Assert.Contains("Connect Oura again", ex.Message);
        Assert.False((await connection.GetStatusAsync(Ct)).Connected);
    }

    [Theory]
    [InlineData("http://lifeos.example/activity/connected")]
    [InlineData("https://lifeos.example/somewhere-else")]
    [InlineData("https://lifeos.example/activity/connected?next=/")]
    [InlineData("https://lifeos.example/activity/connected#x")]
    public async Task A_redirect_off_the_activity_page_is_refused(string redirect)
    {
        var (connection, _) = Create();

        await Assert.ThrowsAsync<ArgumentException>(() => connection.BeginConnectAsync(new Uri(redirect), Ct));
    }

    [Fact]
    public async Task Localhost_may_use_http()
    {
        var (connection, oura) = Create();

        await connection.BeginConnectAsync(new Uri("http://localhost:5173/activity/connected"), Ct);

        Assert.NotNull(oura.AuthorizedState);
    }

    [Fact]
    public async Task A_fresh_token_is_used_as_is_and_a_stale_one_refreshed()
    {
        var (connection, oura) = await ConnectedAsync();

        Assert.Equal("access-1", await connection.GetAccessTokenAsync(Ct));
        Assert.Equal(0, oura.Refreshes);

        clock.Advance(oura.Lifetime - OuraConnectionService.RefreshMargin + TimeSpan.FromSeconds(1));
        Assert.Equal("access-2", await connection.GetAccessTokenAsync(Ct));
        Assert.Equal("access-2", await connection.GetAccessTokenAsync(Ct));
        Assert.Equal(1, oura.Refreshes);

        // The refresh kept the original grant's scopes, which Oura didn't repeat.
        var status = await connection.GetStatusAsync(Ct);
        Assert.Equal(["daily", "workout"], status.Scopes);
        Assert.Equal(clock.UtcNow + oura.Lifetime, status.ExpiresAt);
    }

    [Fact]
    public async Task Two_refreshers_spend_the_refresh_token_once()
    {
        var (connection, oura) = await ConnectedAsync();
        // A second door (the CLI beside the API) on the same database.
        var other = new OuraConnectionService(
            new NpgsqlIntegrationRepository(postgres.ConnectionString, cipher), oura, clock, service);
        clock.Advance(oura.Lifetime);
        oura.RefreshDelay = TimeSpan.FromMilliseconds(300);

        var tokens = await Task.WhenAll(connection.GetAccessTokenAsync(Ct), other.GetAccessTokenAsync(Ct));

        // The first spent refresh-1 and saved the new pair; the second waited, then found it fresh.
        Assert.Equal(1, oura.Refreshes);
        Assert.All(tokens, t => Assert.Equal("access-2", t));
        Assert.Equal(OuraStatus.ConnectedState, (await connection.GetStatusAsync(Ct)).State);
    }

    [Fact]
    public async Task A_spent_grant_marks_the_connection_for_reconnecting()
    {
        var (connection, oura) = await ConnectedAsync();
        clock.Advance(oura.Lifetime);
        oura.FailNextRefresh = new OuraGrantException("invalid_grant", "Oura refused the sign-in (invalid_grant).", spentGrant: true);

        var ex = await Assert.ThrowsAsync<OuraConnectionException>(() => connection.GetAccessTokenAsync(Ct));

        Assert.Contains("Reconnect Oura", ex.Message);
        var status = await connection.GetStatusAsync(Ct);
        Assert.Equal(OuraStatus.Reconnect, status.State);
        Assert.Contains("invalid_grant", status.ReconnectReason);

        // Nothing calls Oura again until it's reconnected.
        await Assert.ThrowsAsync<OuraConnectionException>(() => connection.GetAccessTokenAsync(Ct));
        Assert.Equal(1, oura.Refreshes);

        await connection.BeginConnectAsync(Redirect, Ct);
        status = await connection.ConnectAsync("code-2", oura.AuthorizedState!, Ct);
        Assert.Equal(OuraStatus.ConnectedState, status.State);
        Assert.Null(status.ReconnectReason);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_outage_or_a_client_refusal_keeps_the_tokens(bool outage)
    {
        var (connection, oura) = await ConnectedAsync();
        clock.Advance(oura.Lifetime);
        oura.FailNextRefresh = outage
            ? new OuraUnavailableException("Oura answered 503; try again later.")
            : new OuraGrantException("invalid_client", "Oura refused the sign-in (invalid_client).", spentGrant: false);

        await Assert.ThrowsAnyAsync<Exception>(() => connection.GetAccessTokenAsync(Ct));

        Assert.Equal(OuraStatus.ConnectedState, (await connection.GetStatusAsync(Ct)).State);
        // refresh-1 is still the stored one, and still good.
        Assert.Equal("access-2", await connection.GetAccessTokenAsync(Ct));
    }

    [Fact]
    public async Task Tokens_sealed_under_another_key_ask_to_reconnect()
    {
        await ConnectedAsync();
        var (rekeyed, oura) = Create(new TokenCipher(RandomNumberGenerator.GetBytes(32)));

        var ex = await Assert.ThrowsAsync<OuraConnectionException>(() => rekeyed.GetAccessTokenAsync(Ct));

        Assert.Contains("TokenKey", ex.Message);
        Assert.Equal(OuraStatus.Reconnect, (await rekeyed.GetStatusAsync(Ct)).State);
        Assert.Equal(0, oura.Refreshes);
    }

    [Fact]
    public async Task Disconnecting_revokes_and_forgets_even_when_oura_is_down()
    {
        var (connection, oura) = await ConnectedAsync();
        oura.FailRevoke = new OuraUnavailableException("Oura couldn't be reached to revoke the token; try again later.");

        var status = await connection.DisconnectAsync(Ct);

        Assert.Equal(["access-1"], oura.Revoked);
        Assert.Equal(OuraStatus.NotConnected, status.State);
        Assert.Empty(status.Scopes);
        Assert.Null(status.ConnectedAt);
        await Assert.ThrowsAsync<OuraConnectionException>(() => connection.GetAccessTokenAsync(Ct));
        Assert.Equal(0L, await ScalarAsync<long>(
            "SELECT count(*) FROM bsk_ops.integration WHERE service = @service AND access_token_enc IS NOT NULL"));
    }

    [Fact]
    public async Task Without_an_oura_app_the_status_says_so_and_the_rest_refuses()
    {
        var connection = new OuraConnectionService(
            new NpgsqlIntegrationRepository(postgres.ConnectionString, null), null, clock, service);

        Assert.Equal(OuraStatus.NotConfigured, (await connection.GetStatusAsync(Ct)).State);
        var ex = await Assert.ThrowsAsync<OuraConnectionException>(() => connection.BeginConnectAsync(Redirect, Ct));
        Assert.Contains("Oura__ClientId", ex.Message);
        await Assert.ThrowsAsync<OuraConnectionException>(() => connection.GetAccessTokenAsync(Ct));
        Assert.Equal(OuraStatus.NotConfigured, (await connection.DisconnectAsync(Ct)).State);
    }
}
