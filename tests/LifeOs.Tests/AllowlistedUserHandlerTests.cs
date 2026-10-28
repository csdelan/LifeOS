using System.Security.Claims;
using LifeOs.Api.Auth;
using Microsoft.AspNetCore.Authorization;

namespace LifeOs.Tests;

/// <summary>
/// The web's sign-in rule (agent-access plan §4.4): an allowlisted email, signed in with
/// Google, never an agent's token, and nothing taken from <c>user_metadata</c>.
/// </summary>
public sealed class AllowlistedUserHandlerTests
{
    private const string GoogleAccount = """{"provider":"google","providers":["google"]}""";
    private const string GoogleSignIn = """{"method":"oauth","timestamp":1793200000}""";

    [Fact]
    public async Task An_allowlisted_Google_sign_in_succeeds()
    {
        var context = await HandleAsync(Principal("Owner@Example.com"), "owner@example.com, other@example.com");

        Assert.True(context.HasSucceeded);
        Assert.False(context.HasFailed);
    }

    [Fact]
    public async Task A_plain_string_amr_entry_counts()
    {
        var context = await HandleAsync(Principal("owner@example.com", amr: ["oauth"]));

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Google_with_MFA_still_counts()
    {
        var context = await HandleAsync(Principal(
            "owner@example.com",
            amr: ["""{"method":"totp","timestamp":1793200100}""", GoogleSignIn]));

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Someone_else_fails()
    {
        var context = await HandleAsync(Principal("intruder@example.com"));

        Assert.True(context.HasFailed);
    }

    [Theory]
    [InlineData("""{"method":"password","timestamp":1793200000}""")]
    [InlineData("""{"method":"otp","timestamp":1793200000}""")]
    [InlineData("""{"method":"magiclink","timestamp":1793200000}""")]
    [InlineData("""{"method":"oauth_provider/authorization_code","timestamp":1793200000}""")]
    [InlineData("""{"method":"anonymous","timestamp":1793200000}""")]
    public async Task The_right_account_signed_in_another_way_fails(string amr)
    {
        // A password an agent set on the account, say, signs in as "password".
        var context = await HandleAsync(Principal("owner@example.com", amr: [amr]));

        Assert.True(context.HasFailed);
    }

    [Fact]
    public async Task A_session_without_amr_fails()
    {
        var context = await HandleAsync(Principal("owner@example.com", amr: []));

        Assert.True(context.HasFailed);
    }

    [Fact]
    public async Task An_account_that_isnt_Googles_fails()
    {
        var context = await HandleAsync(Principal(
            "owner@example.com",
            appMetadata: """{"provider":"email","providers":["email"]}"""));

        Assert.True(context.HasFailed);
    }

    [Fact]
    public async Task A_Google_account_that_later_gained_a_password_still_signs_in_with_Google()
    {
        var context = await HandleAsync(Principal(
            "owner@example.com",
            appMetadata: """{"provider":"google","providers":["google","email"]}"""));

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task User_metadata_is_never_read()
    {
        // The user (so any agent with one of their tokens) can write user_metadata, so neither
        // an email nor "verified" nor Google's issuer in it counts.
        var noEmail = await HandleAsync(Principal(
            email: null,
            userMetadata: """{"email":"owner@example.com","email_verified":true,"iss":"https://accounts.google.com"}"""));
        var notGoogle = await HandleAsync(Principal(
            "owner@example.com",
            appMetadata: """{"provider":"email","providers":["email"]}""",
            amr: ["""{"method":"password","timestamp":1793200000}"""],
            userMetadata: """{"email_verified":true,"iss":"https://accounts.google.com"}"""));

        Assert.True(noEmail.HasFailed);
        Assert.True(notGoogle.HasFailed);
    }

    [Fact]
    public async Task An_agents_token_fails()
    {
        var context = await HandleAsync(Principal(
            "owner@example.com",
            amr: ["""{"method":"oauth_provider/authorization_code","timestamp":1793200000}"""],
            clientId: "client-1"));

        Assert.True(context.HasFailed);
    }

    [Fact]
    public async Task Malformed_claims_are_ignored()
    {
        var context = await HandleAsync(Principal(
            "owner@example.com",
            appMetadata: "{not json",
            amr: ["{not json", """{"method":5}"""]));

        Assert.True(context.HasFailed);
    }

    [Fact]
    public async Task An_unauthenticated_principal_does_not_succeed()
    {
        var context = await HandleAsync(Principal("owner@example.com", authenticated: false));

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public void Sign_in_methods_read_objects_and_strings_in_order()
    {
        var user = Principal("owner@example.com", amr: ["""{"method":"totp","timestamp":2}""", "oauth", "", "{bad"]);

        Assert.Equal(new[] { "totp", "oauth" }, SupabaseClaims.SignInMethods(user));
    }

    private static ClaimsPrincipal Principal(
        string? email,
        string? appMetadata = GoogleAccount,
        string[]? amr = null,
        string? userMetadata = null,
        string? clientId = null,
        bool authenticated = true)
    {
        var claims = new List<Claim>();
        if (email is not null)
        {
            claims.Add(new Claim("email", email));
        }

        if (appMetadata is not null)
        {
            claims.Add(new Claim("app_metadata", appMetadata));
        }

        claims.AddRange((amr ?? [GoogleSignIn]).Select(a => new Claim("amr", a)));
        if (userMetadata is not null)
        {
            claims.Add(new Claim("user_metadata", userMetadata));
        }

        if (clientId is not null)
        {
            claims.Add(new Claim("client_id", clientId));
        }

        return new ClaimsPrincipal(authenticated
            ? new ClaimsIdentity(claims, authenticationType: "Bearer")
            : new ClaimsIdentity(claims));
    }

    private static async Task<AuthorizationHandlerContext> HandleAsync(
        ClaimsPrincipal user,
        string allowedEmails = "owner@example.com")
    {
        var options = new SupabaseAuthOptions { AllowedEmails = allowedEmails };
        options.Normalize();
        var handler = new AllowlistedUserHandler(options);
        var context = new AuthorizationHandlerContext([new AllowlistedUserRequirement()], user, resource: null);
        await handler.HandleAsync(context);
        return context;
    }
}
