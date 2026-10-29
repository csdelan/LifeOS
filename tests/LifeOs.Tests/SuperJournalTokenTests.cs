using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using LifeOs.Api.Auth;
using LifeOs.Api.Hosting;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;
using LifeOs.Infrastructure.Integrations;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace LifeOs.Tests;

/// <summary>
/// How LifeOS proves who it calls SuperJournal for (D19, csdelan/LifeOS#28): the ES256 token it
/// signs per call, the key it reads from <c>SuperJournal:SigningKey</c>, who a request is for (a
/// web session, or an agent through <c>/mcp</c> as the person behind its token), and the
/// SuperJournal settings. Keys here are made per test and thrown away.
/// </summary>
public sealed class SuperJournalTokenTests
{
    private const string Subject = "6f1c2b3a-0000-4000-8000-00000000c4a1";

    // ---- The token ----

    [Fact]
    public async Task A_token_is_es256_for_the_person_from_lifeos_to_superjournal_as_owner_for_five_minutes()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var now = DateTimeOffset.UtcNow;
        var signer = new SuperJournalTokenSigner(key, new TimerTestClock(now));

        var token = signer.TokenFor(Subject);

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token);
        Assert.Equal("ES256", jwt.Alg);
        using var payload = JsonDocument.Parse(Base64UrlEncoder.Decode(jwt.EncodedPayload));
        var claims = payload.RootElement;
        Assert.Equal("lifeos", claims.GetProperty("iss").GetString());
        Assert.Equal("superjournal", claims.GetProperty("aud").GetString());
        Assert.Equal(Subject, claims.GetProperty("sub").GetString());
        Assert.Equal(["owner"], claims.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ToArray());
        Assert.Equal(now.ToUnixTimeSeconds(), claims.GetProperty("iat").GetInt64());
        Assert.Equal(300, claims.GetProperty("exp").GetInt64() - claims.GetProperty("iat").GetInt64());

        // As SuperJournal checks it: the public key only, issuer, audience and lifetime.
        var result = await Validate(token, key);
        Assert.True(result.IsValid, result.Exception?.Message);
        Assert.Equal(Subject, result.ClaimsIdentity.FindFirst("sub")?.Value);
        Assert.Contains(result.ClaimsIdentity.FindAll("roles"), c => c.Value == "owner");
    }

    [Fact]
    public async Task A_token_lapses_after_five_minutes_and_another_key_cannot_have_signed_it()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var stale = new SuperJournalTokenSigner(key, new TimerTestClock(DateTimeOffset.UtcNow.AddMinutes(-6))).TokenFor(Subject);
        var fresh = new SuperJournalTokenSigner(key, new TimerTestClock(DateTimeOffset.UtcNow)).TokenFor(Subject);

        Assert.IsType<SecurityTokenExpiredException>((await Validate(stale, key)).Exception);
        Assert.False((await Validate(fresh, other)).IsValid);
    }

    [Fact]
    public void A_token_needs_a_person()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signer = new SuperJournalTokenSigner(key, new TimerTestClock(DateTimeOffset.UtcNow));

        Assert.Throws<ArgumentException>(() => signer.TokenFor(" "));
    }

    // ---- The signing key ----

    public static TheoryData<string> KeyForms()
    {
        // Made once per run and thrown away; never a real key.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pkcs8 = key.ExportPkcs8PrivateKeyPem();
        var sec1 = key.ExportECPrivateKeyPem();
        return new TheoryData<string>
        {
            pkcs8,
            sec1,
            pkcs8.Replace("\r\n", "\\n").Replace("\n", "\\n"),
            Convert.ToBase64String(key.ExportPkcs8PrivateKey()),
            Convert.ToBase64String(key.ExportECPrivateKey()),
        };
    }

    [Theory]
    [MemberData(nameof(KeyForms))]
    public async Task The_signing_key_reads_as_pem_or_its_one_line_body(string keyText)
    {
        using var key = SuperJournalTokenSigner.ParseKey(keyText);
        var token = new SuperJournalTokenSigner(key, new TimerTestClock(DateTimeOffset.UtcNow)).TokenFor(Subject);

        Assert.True((await Validate(token, key)).IsValid);
    }

    [Fact]
    public void A_signing_key_that_cannot_sign_es256_fails_startup_in_words_without_the_key()
    {
        using var p256 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var publicOnly = p256.ExportSubjectPublicKeyInfoPem();
        var tooBig = p384.ExportPkcs8PrivateKeyPem();

        var missing = Assert.Throws<InvalidOperationException>(() => SuperJournalTokenSigner.ParseKey(null));
        Assert.Contains(SuperJournalOptions.SigningKeyKey, missing.Message, StringComparison.Ordinal);
        var notPrivate = Assert.Throws<InvalidOperationException>(() => SuperJournalTokenSigner.ParseKey(publicOnly));
        Assert.Contains("isn't an EC private key", notPrivate.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("MFkw", notPrivate.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => SuperJournalTokenSigner.ParseKey("not a key at all"));
        var curve = Assert.Throws<InvalidOperationException>(() => SuperJournalTokenSigner.ParseKey(tooBig));
        Assert.Contains("P-256", curve.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_api_host_refuses_a_superjournal_url_without_a_signing_key()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development",
            ApplicationName = "LifeOs.Api.SuperJournalTests",
        });
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Owner"] = "Host=127.0.0.1;Port=1;Database=postgres;Username=postgres;Password=x",
            ["ConnectionStrings:Reader"] = "Host=127.0.0.1;Port=1;Database=postgres;Username=postgres;Password=x",
            [SuperJournalOptions.BaseUrlKey] = "https://journal.example.test/api/journal",
            [SuperJournalOptions.SigningKeyKey] = "",
        });

        var ex = Assert.Throws<InvalidOperationException>(() => ApiHost.ConfigureServices(builder));
        Assert.Contains(SuperJournalOptions.SigningKeyKey, ex.Message, StringComparison.Ordinal);
    }

    // ---- Who a call is for ----

    [Fact]
    public void A_web_session_saves_as_api_and_an_agent_as_mcp_client_both_as_the_person_behind_the_token()
    {
        var web = JournalCallers.For(Principal(("sub", Subject), ("email", "owner@example.com")));
        var agent = JournalCallers.For(Principal(("sub", Subject), (AgentClaims.ClientIdClaim, "f1ec147b-08d1-4cc3-93fd-1bed4aba6b93")));

        Assert.Equal(new JournalCaller(Subject, KernelSources.Api), web);
        Assert.Equal(new JournalCaller(Subject, "mcp:f1ec147b-08d1-4cc3-93fd-1bed4aba6b93"), agent);
        var error = Assert.Throws<SuperJournalException>(() => JournalCallers.For(Principal(("email", "owner@example.com"))));
        Assert.Equal(SuperJournalFailure.Forbidden, error.Failure);
    }

    // ---- Settings ----

    [Fact]
    public void Without_a_base_url_superjournal_is_off()
    {
        Assert.Null(SuperJournalOptions.Resolve(Settings()));
        Assert.Null(SuperJournalOptions.Resolve(Settings((SuperJournalOptions.BaseUrlKey, " "))));
    }

    [Fact]
    public void The_base_url_is_a_folder_and_the_timeout_defaults_to_fifteen_seconds()
    {
        var options = SuperJournalOptions.Resolve(Settings((SuperJournalOptions.BaseUrlKey, " https://blueskies.example.test/api/journal ")))!;
        Assert.Equal("https://blueskies.example.test/api/journal/", options.BaseUrl.AbsoluteUri);
        Assert.Equal(TimeSpan.FromSeconds(15), options.Timeout);

        var local = SuperJournalOptions.Resolve(Settings(
            (SuperJournalOptions.BaseUrlKey, "http://127.0.0.1:5297/"), (SuperJournalOptions.TimeoutKey, "40")))!;
        Assert.Equal("http://127.0.0.1:5297/", local.BaseUrl.AbsoluteUri);
        Assert.Equal(TimeSpan.FromSeconds(40), local.Timeout);
    }

    [Theory]
    [InlineData("http://journal.example.test/api/journal", null)]
    [InlineData("journal.example.test", null)]
    [InlineData("https://journal.example.test/?x=1", null)]
    [InlineData("https://journal.example.test/", "0")]
    [InlineData("https://journal.example.test/", "121")]
    [InlineData("https://journal.example.test/", "ten")]
    public void A_malformed_setting_fails_startup(string baseUrl, string? timeout)
        => Assert.Throws<InvalidOperationException>(() => SuperJournalOptions.Resolve(Settings(
            (SuperJournalOptions.BaseUrlKey, baseUrl), (SuperJournalOptions.TimeoutKey, timeout))));

    private static Task<TokenValidationResult> Validate(string token, ECDsa signedWith)
    {
        var publicOnly = ECDsa.Create();
        publicOnly.ImportSubjectPublicKeyInfo(signedWith.ExportSubjectPublicKeyInfo(), out _);
        return new JsonWebTokenHandler { MapInboundClaims = false }.ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = "lifeos",
            ValidAudience = "superjournal",
            IssuerSigningKey = new ECDsaSecurityKey(publicOnly),
            ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "sub",
            RoleClaimType = "roles",
        });
    }

    private static ClaimsPrincipal Principal(params (string Type, string Value)[] claims)
        => new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "test"));

    private static Func<string, string?> Settings(params (string Key, string? Value)[] pairs)
    {
        var map = pairs.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        return key => map.GetValueOrDefault(key);
    }
}
