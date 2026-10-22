using System.Net;
using System.Reflection;
using System.Text;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;
using LifeOs.Infrastructure.Integrations;
using Microsoft.AspNetCore.WebUtilities;

namespace LifeOs.Tests;

/// <summary>
/// Oura's OAuth calls and data collections over HTTP (activity plan A1, A2, §3.1, §3.3), against
/// a stub handler: what LifeOS sends, how it reads Oura's answers (recorded from Oura's sandbox
/// in <c>OuraFixtures</c>), and how each refusal or outage surfaces — never with a token or the
/// client secret in the message.
/// </summary>
public sealed class OuraHttpClientTests
{
    private const string Secret = "the-client-secret";
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly Uri Redirect = new("https://lifeos.example/activity/connected");

    private static readonly OuraOptions Options = new(
        "client-123", Secret, OuraOptions.DefaultAuthorizeUrl, OuraOptions.DefaultTokenUrl,
        new Uri("https://moi.ouraring.com/oauth/v2/ext/oauth-token"), OuraOptions.DefaultRevokeUrl,
        OuraOptions.DefaultCollectionsUrl);

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

        // The new portal's sign-in, which adds the external-API sign-in on its way to Oura's.
        Assert.StartsWith("https://developer.ouraring.com/authorize?", url.AbsoluteUri);
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

    // ---------------------------------------------------------------------------------
    // Collections (A2)
    // ---------------------------------------------------------------------------------

    private static readonly DateOnly Start = new(2026, 9, 29);
    private static readonly DateOnly End = new(2026, 10, 4);

    /// <summary>A recorded answer from Oura's sandbox (<c>OuraFixtures/&lt;collection&gt;.json</c>).</summary>
    internal static string Fixture(string collection)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"OuraFixtures.{collection}.json")
                           ?? throw new InvalidOperationException($"No Oura fixture for {collection}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [Fact]
    public async Task A_collection_is_read_with_the_bearer_token_and_the_days_asked_for()
    {
        var (client, handler) = Create(Json(HttpStatusCode.OK, Fixture("daily_activity")));

        var days = await client.GetDailyActivityAsync("access-1", Start, End, TestContext.Current.CancellationToken);

        var sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, sent.Method);
        Assert.Equal(
            "https://api.ouraring.com/v2/usercollection/daily_activity?start_date=2026-09-29&end_date=2026-10-04", sent.Uri);
        Assert.Equal("Bearer access-1", sent.Authorization);

        Assert.Equal(2, days.Count);
        var day = days[1];
        Assert.Equal(new DateOnly(2026, 10, 2), day.Day);
        Assert.Equal("daily_activity-1-2026-10-2", day.OuraId);
        Assert.Equal(new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero), day.Timestamp);
        Assert.Equal(20000, day.Steps);
        Assert.Equal(200, day.ActiveCalories);
        Assert.Equal(3000, day.TotalCalories);
        Assert.Equal(2000, day.WalkingM);
        Assert.Equal((200, 40, 20), (day.HighS, day.MediumS, day.LowS));
        Assert.Equal((20, 20, 20), (day.SedentaryS, day.RestingS, day.NonWearS));
        Assert.Equal(74, day.Score);
    }

    [Fact]
    public async Task Workouts_sleep_and_readiness_read_from_the_sandbox_shapes()
    {
        var ct = TestContext.Current.CancellationToken;
        var (workoutClient, _) = Create(Json(HttpStatusCode.OK, Fixture("workout")));
        var (sleepClient, _) = Create(Json(HttpStatusCode.OK, Fixture("daily_sleep")));
        var (readinessClient, _) = Create(Json(HttpStatusCode.OK, Fixture("daily_readiness")));
        var (periodClient, periodHandler) = Create(Json(HttpStatusCode.OK, Fixture("sleep")));

        var workouts = await workoutClient.GetWorkoutsAsync("a", Start, End, ct);
        Assert.Equal(["walking", "swimming", "running"], workouts.Select(w => w.Activity));
        Assert.Equal(["confirmed", "manual", "workout_heart_rate"], workouts.Select(w => w.Source));
        var swim = workouts[1];
        Assert.Equal("workout-2-2026-10-2", swim.Id);
        Assert.Equal((1000, 200), (swim.Calories, swim.DistanceM));
        Assert.Equal("moderate", swim.Intensity);
        Assert.Null(swim.Label);
        Assert.Equal("My Workout", workouts[0].Label);

        var sleep = await sleepClient.GetDailySleepAsync("a", Start, End, ct);
        Assert.Equal(73, sleep[1].Score);
        Assert.Equal(
            ["deep_sleep", "efficiency", "latency", "rem_sleep", "restfulness", "timing", "total_sleep"],
            sleep[1].Contributors.Keys);

        var readiness = await readinessClient.GetDailyReadinessAsync("a", Start, End, ct);
        Assert.Equal(80, readiness[0].Score);
        Assert.Equal((0.5, 0.2), (readiness[0].TemperatureDeviation, readiness[0].TemperatureTrendDeviation));
        Assert.Null(readiness[0].Contributors["sleep_regularity"]);

        var periods = await periodClient.GetSleepPeriodsAsync("a", Start, End, ct);
        Assert.EndsWith("/v2/usercollection/sleep?start_date=2026-09-29&end_date=2026-10-04", Assert.Single(periodHandler.Requests).Uri);
        Assert.Equal(["long_sleep", "late_nap"], periods.Select(p => p.Type));
        var nap = periods[1];
        Assert.Equal((3000, 2370, 30, 200, 58, 24), (nap.InBedS, nap.AsleepS, nap.DeepS, nap.LightS, nap.RemS, nap.AwakeS));
        Assert.Equal((50, 70, 56, 64.0, 54), (nap.LatencyS, nap.Efficiency, nap.AverageHrv, nap.AverageHr, nap.LowestHr));
    }

    [Fact]
    public async Task Paging_follows_next_token_until_it_runs_out_or_repeats()
    {
        var (client, handler) = Create(
            Json(HttpStatusCode.OK, """{"data":[{"day":"2026-10-01","steps":1}],"next_token":"page-2"}"""),
            Json(HttpStatusCode.OK, """{"data":[{"day":"2026-10-02","steps":2}],"next_token":"page-3"}"""),
            Json(HttpStatusCode.OK, """{"data":[{"day":"2026-10-03","steps":3}],"next_token":"page-3"}"""));

        var days = await client.GetDailyActivityAsync("access-1", Start, End, TestContext.Current.CancellationToken);

        // The third page names itself as the next page: a token already followed, so it stops.
        Assert.Equal([1, 2, 3], days.Select(d => d.Steps));
        Assert.Equal(3, handler.Requests.Count);
        Assert.DoesNotContain("next_token", handler.Requests[0].Uri);
        Assert.EndsWith("&next_token=page-2", handler.Requests[1].Uri);
        Assert.EndsWith("&next_token=page-3", handler.Requests[2].Uri);
        Assert.All(handler.Requests, r => Assert.Equal("Bearer access-1", r.Authorization));
    }

    [Fact]
    public async Task Records_without_what_identifies_them_are_skipped_and_unknown_values_kept()
    {
        var (client, _) = Create(Json(HttpStatusCode.OK, """
            {"data":[
              {"id":"w1","day":"2026-10-01","activity":"padel_v2","source":"garmin_import",
               "start_datetime":"2026-10-01T07:00:00-07:00","end_datetime":"2026-10-01T07:45:00-07:00",
               "calories":301.6,"distance":null,"intensity":"brutal"},
              {"id":"w2","day":"2026-10-01","start_datetime":"2026-10-01T09:00:00-07:00","end_datetime":"2026-10-01T09:30:00-07:00"},
              {"day":"2026-10-01","start_datetime":"2026-10-01T09:00:00-07:00","end_datetime":"2026-10-01T09:30:00-07:00"},
              {"id":"w4","day":"not a day","start_datetime":"2026-10-01T09:00:00-07:00","end_datetime":"2026-10-01T09:30:00-07:00"},
              {"id":"w5","day":"2026-10-01","start_datetime":"2026-10-01T09:00:00-07:00"},
              "not an object"
            ],"next_token":null}
            """));

        var workouts = await client.GetWorkoutsAsync("a", Start, End, TestContext.Current.CancellationToken);

        Assert.Equal(["w1", "w2"], workouts.Select(w => w.Id));
        var padel = workouts[0];
        Assert.Equal(("padel_v2", "garmin_import", "brutal"), (padel.Activity, padel.Source, padel.Intensity));
        Assert.Equal(302, padel.Calories);
        Assert.Null(padel.DistanceM);
        Assert.Equal(TimeSpan.FromHours(-7), padel.Start.Offset);
        Assert.Equal(("unknown", "unknown"), (workouts[1].Activity, workouts[1].Source));
    }

    [Fact]
    public async Task A_refused_token_or_access_says_which()
    {
        var ct = TestContext.Current.CancellationToken;
        var (unauthorized, _) = Create(Json(HttpStatusCode.Unauthorized, """{"detail":"Invalid token"}"""));
        var (forbidden, _) = Create(Json(HttpStatusCode.Forbidden, """{"detail":"Membership required"}"""));

        var refused = await Assert.ThrowsAsync<OuraAccessDeniedException>(() =>
            unauthorized.GetWorkoutsAsync("secret-access-token", Start, End, ct));
        Assert.Equal(401, refused.Status);
        Assert.Equal("Oura refused the access token for workout: “Invalid token”", refused.Message);
        Assert.Equal(("workout", "Invalid token"), (refused.Collection, refused.Detail));
        Assert.DoesNotContain("secret-access-token", refused.Message);

        var denied = await Assert.ThrowsAsync<OuraAccessDeniedException>(() =>
            forbidden.GetDailySleepAsync("secret-access-token", Start, End, ct));
        Assert.Equal(403, denied.Status);
        Assert.Contains("Membership required", denied.Message);
        Assert.Contains("daily_sleep", denied.Message);
        Assert.Equal(("daily_sleep", "Membership required"), (denied.Collection, denied.Detail));
        Assert.DoesNotContain("secret-access-token", denied.Message);
    }

    [Fact]
    public async Task A_refused_token_without_words_says_the_challenge_or_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var (challenged, _) = Create(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Unauthorized);
            response.Headers.WwwAuthenticate.ParseAdd("Bearer error=\"insufficient_scope\", scope=\"extapi:daily\"");
            return response;
        });
        var (silent, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var scoped = await Assert.ThrowsAsync<OuraAccessDeniedException>(() =>
            challenged.GetDailyActivityAsync("secret-access-token", Start, End, ct));
        Assert.Equal("daily_activity", scoped.Collection);
        Assert.Contains("insufficient_scope", scoped.Detail);
        Assert.Contains("insufficient_scope", scoped.Message);

        var bare = await Assert.ThrowsAsync<OuraAccessDeniedException>(() =>
            silent.GetSleepPeriodsAsync("secret-access-token", Start, End, ct));
        Assert.Equal("Oura refused the access token for sleep.", bare.Message);
        Assert.Null(bare.Detail);
    }

    [Fact]
    public void The_client_says_who_is_asking_and_asks_for_json()
    {
        using var http = OuraHttpClient.CreateHttpClient();

        Assert.Equal("LifeOS/1.0", http.DefaultRequestHeaders.UserAgent.ToString());
        Assert.Equal("application/json", http.DefaultRequestHeaders.Accept.ToString());
        Assert.Equal(TimeSpan.FromSeconds(30), http.Timeout);
    }

    [Fact]
    public async Task A_collection_that_fails_or_answers_strangely_is_unavailable()
    {
        var ct = TestContext.Current.CancellationToken;
        var (limited, _) = Create(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
            return response;
        });
        var (failing, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var (listless, _) = Create(Json(HttpStatusCode.OK, """{"items":[]}"""));
        var (garbled, _) = Create(Json(HttpStatusCode.OK, "<html>maintenance</html>"));
        var (endless, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($$"""{"data":[],"next_token":"{{Guid.NewGuid():N}}"}""", Encoding.UTF8, "application/json")
        });

        Assert.Equal(TimeSpan.FromSeconds(30),
            (await Assert.ThrowsAsync<OuraUnavailableException>(() => limited.GetWorkoutsAsync("a", Start, End, ct))).RetryAfter);
        await Assert.ThrowsAsync<OuraUnavailableException>(() => failing.GetDailyActivityAsync("a", Start, End, ct));
        await Assert.ThrowsAsync<OuraUnavailableException>(() => listless.GetDailyActivityAsync("a", Start, End, ct));
        await Assert.ThrowsAsync<OuraUnavailableException>(() => garbled.GetDailyActivityAsync("a", Start, End, ct));
        Assert.Contains("kept paging",
            (await Assert.ThrowsAsync<OuraUnavailableException>(() => endless.GetSleepPeriodsAsync("a", Start, End, ct))).Message);
    }

    [Fact]
    public void The_collections_url_can_point_at_the_sandbox()
    {
        var sandbox = OuraOptions.Resolve(key => key switch
        {
            OuraOptions.ClientIdKey => "client-123",
            OuraOptions.ClientSecretKey => Secret,
            OuraOptions.CollectionsUrlKey => "https://api.ouraring.com/v2/sandbox/usercollection",
            _ => null
        })!;
        var standard = OuraOptions.Resolve(key => key switch
        {
            OuraOptions.ClientIdKey => "client-123",
            OuraOptions.ClientSecretKey => Secret,
            _ => null
        })!;

        // Without the trailing slash, the collection's name would replace the last segment.
        Assert.Equal("https://api.ouraring.com/v2/sandbox/usercollection/", sandbox.CollectionsUrl.AbsoluteUri);
        Assert.Equal(
            "https://api.ouraring.com/v2/sandbox/usercollection/workout",
            new Uri(sandbox.CollectionsUrl, OuraCollections.Workout.Name).AbsoluteUri);
        Assert.Equal(OuraOptions.DefaultCollectionsUrl, standard.CollectionsUrl);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage>[] answers) : HttpMessageHandler
    {
        public List<SentRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new SentRequest(
                request.Method, request.RequestUri!.AbsoluteUri, request.Content?.Headers.ContentType?.MediaType, body,
                request.Headers.Authorization?.ToString()));
            return answers[Math.Min(Requests.Count - 1, answers.Length - 1)](request);
        }
    }

    private sealed record SentRequest(HttpMethod Method, string Uri, string? ContentType, string Body, string? Authorization);
}
