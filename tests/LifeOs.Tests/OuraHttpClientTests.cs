using System.Net;
using System.Text;
using LifeOs.Application.Abstractions;
using LifeOs.Infrastructure.Integrations;
using Microsoft.AspNetCore.WebUtilities;

namespace LifeOs.Tests;

/// <summary>
/// Oura's OAuth calls over HTTP (activity plan A1, §3.1), against a stub handler: what LifeOS
/// sends, how it reads Oura's answers, and how each refusal or outage surfaces — never with a
/// token or the client secret in the message.
/// </summary>
public sealed class OuraHttpClientTests
{
    private const string Secret = "the-client-secret";
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly Uri Redirect = new("https://lifeos.example/activity/connected");

    private static readonly OuraOptions Options = new(
        "client-123", Secret, OuraOptions.DefaultAuthorizeUrl, OuraOptions.DefaultTokenUrl,
        new Uri("https://moi.ouraring.com/oauth/v2/ext/oauth-token"), OuraOptions.DefaultRevokeUrl);

    private static (OuraHttpClient Client, StubHandler Handler) Create(params Func<HttpRequestMessage, HttpResponseMessage>[] answers)
    {
        var handler = new StubHandler(answers);
        return (new OuraHttpClient(new HttpClient(handler), Options, new TimerTestClock(Now)), handler);
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> Json(HttpStatusCode status, string json)
        => _ => new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private const string Granted = """
        {"token_type":"bearer","access_token":"access-1","expires_in":86400,
         "refresh_token":"refresh-1","scope":"extapi:daily extapi:workout"}
        """;

    [Fact]
    public void The_authorize_url_carries_the_client_scopes_state_and_challenge()
    {
        var (client, _) = Create();

        var url = client.AuthorizeUrl("state-1", "challenge-1", Redirect);

        Assert.StartsWith("https://cloud.ouraring.com/oauth/authorize?", url.AbsoluteUri);
        var query = QueryHelpers.ParseQuery(url.Query);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("client-123", query["client_id"]);
        Assert.Equal(Redirect.AbsoluteUri, query["redirect_uri"]);
        Assert.Equal("daily workout", query["scope"]);
        Assert.Equal("state-1", query["state"]);
        Assert.Equal("challenge-1", query["code_challenge"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.DoesNotContain(Secret, url.AbsoluteUri);
    }

    [Fact]
    public async Task Exchanging_a_code_posts_the_form_and_reads_the_tokens()
    {
        var (client, handler) = Create(Json(HttpStatusCode.OK, Granted));

        var tokens = await client.ExchangeCodeAsync("code-1", "verifier-1", Redirect, TestContext.Current.CancellationToken);

        Assert.Equal("access-1", tokens.AccessToken);
        Assert.Equal("refresh-1", tokens.RefreshToken);
        Assert.Equal(Now.AddDays(1), tokens.ExpiresAt);
        Assert.Equal(["daily", "workout"], tokens.Scopes);

        var sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal(OuraOptions.DefaultTokenUrl.AbsoluteUri, sent.Uri);
        Assert.Equal("application/x-www-form-urlencoded", sent.ContentType);
        var form = QueryHelpers.ParseQuery(sent.Body);
        Assert.Equal("authorization_code", form["grant_type"]);
        Assert.Equal("code-1", form["code"]);
        Assert.Equal("verifier-1", form["code_verifier"]);
        Assert.Equal(Redirect.AbsoluteUri, form["redirect_uri"]);
        Assert.Equal("client-123", form["client_id"]);
        Assert.Equal(Secret, form["client_secret"]);
    }

    [Fact]
    public async Task Refreshing_goes_to_the_refresh_url_and_keeps_missing_answers_sensible()
    {
        // No expires_in and no scope: a day's lifetime, and the scopes left for the caller.
        var (client, handler) = Create(Json(HttpStatusCode.OK, """{"access_token":"access-2","refresh_token":"refresh-2"}"""));

        var tokens = await client.RefreshAsync("refresh-1", TestContext.Current.CancellationToken);

        Assert.Equal("access-2", tokens.AccessToken);
        Assert.Equal(Now + OuraHttpClient.DefaultLifetime, tokens.ExpiresAt);
        Assert.Empty(tokens.Scopes);
        var sent = Assert.Single(handler.Requests);
        Assert.Equal("https://moi.ouraring.com/oauth/v2/ext/oauth-token", sent.Uri);
        var form = QueryHelpers.ParseQuery(sent.Body);
        Assert.Equal("refresh_token", form["grant_type"]);
        Assert.Equal("refresh-1", form["refresh_token"]);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, """{"error":"invalid_grant","error_description":"Refresh token already used"}""", "invalid_grant", true)]
    [InlineData(HttpStatusCode.Unauthorized, """{"error":"invalid_client"}""", "invalid_client", false)]
    [InlineData(HttpStatusCode.Unauthorized, """{"detail":"Unauthorized"}""", "http_401", true)]
    [InlineData(HttpStatusCode.BadRequest, "not json", "http_400", false)]
    public async Task A_refused_grant_says_whether_it_is_spent(
        HttpStatusCode status, string body, string error, bool spent)
    {
        var (client, _) = Create(Json(status, body));

        var ex = await Assert.ThrowsAsync<OuraGrantException>(() =>
            client.RefreshAsync("refresh-secret-token", TestContext.Current.CancellationToken));

        Assert.Equal(error, ex.Error);
        Assert.Equal(spent, ex.SpentGrant);
        Assert.Contains(error, ex.Message);
        Assert.DoesNotContain("refresh-secret-token", ex.Message);
        Assert.DoesNotContain(Secret, ex.Message);
    }

    [Fact]
    public async Task Too_many_requests_says_how_long_to_wait()
    {
        var (client, _) = Create(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(90));
            return response;
        });

        var ex = await Assert.ThrowsAsync<OuraUnavailableException>(() =>
            client.RefreshAsync("refresh-1", TestContext.Current.CancellationToken));

        Assert.Equal(TimeSpan.FromSeconds(90), ex.RetryAfter);
        Assert.Contains("90 seconds", ex.Message);
    }

    [Fact]
    public async Task An_outage_a_timeout_or_a_strange_answer_is_unavailable()
    {
        var (failing, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var (unreachable, _) = Create(_ => throw new HttpRequestException("No such host is known."));
        var (slow, _) = Create(_ => throw new TaskCanceledException("timed out"));
        var (garbled, _) = Create(Json(HttpStatusCode.OK, "<html>maintenance</html>"));
        var (tokenless, _) = Create(Json(HttpStatusCode.OK, """{"access_token":"a"}"""));
        var ct = TestContext.Current.CancellationToken;

        Assert.Contains("503", (await Assert.ThrowsAsync<OuraUnavailableException>(() => failing.RefreshAsync("r", ct))).Message);
        Assert.Contains("reached", (await Assert.ThrowsAsync<OuraUnavailableException>(() => unreachable.RefreshAsync("r", ct))).Message);
        Assert.Contains("in time", (await Assert.ThrowsAsync<OuraUnavailableException>(() => slow.RefreshAsync("r", ct))).Message);
        await Assert.ThrowsAsync<OuraUnavailableException>(() => garbled.RefreshAsync("r", ct));
        await Assert.ThrowsAsync<OuraUnavailableException>(() => tokenless.RefreshAsync("r", ct));
    }

    [Fact]
    public async Task Revoking_sends_the_token_and_shrugs_at_one_oura_no_longer_knows()
    {
        var (client, handler) = Create(
            _ => new HttpResponseMessage(HttpStatusCode.OK),
            _ => new HttpResponseMessage(HttpStatusCode.BadRequest));
        var ct = TestContext.Current.CancellationToken;

        await client.RevokeAsync("access-1", ct);
        await client.RevokeAsync("access-1", ct);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Equal("https://api.ouraring.com/oauth/revoke?access_token=access-1", handler.Requests[0].Uri);
    }

    [Theory]
    [InlineData("extapi:daily extapi:workout", new[] { "daily", "workout" })]
    [InlineData("daily,workout daily", new[] { "daily", "workout" })]
    [InlineData("extapi:spo2Daily", new[] { "spo2Daily" })]
    public void Scopes_lose_their_prefix(string scope, string[] expected)
        => Assert.Equal(expected, OuraHttpClient.NormalizeScopes(scope));

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage>[] answers) : HttpMessageHandler
    {
        public List<SentRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new SentRequest(
                request.Method, request.RequestUri!.AbsoluteUri, request.Content?.Headers.ContentType?.MediaType, body));
            return answers[Math.Min(Requests.Count - 1, answers.Length - 1)](request);
        }
    }

    private sealed record SentRequest(HttpMethod Method, string Uri, string? ContentType, string Body);
}
