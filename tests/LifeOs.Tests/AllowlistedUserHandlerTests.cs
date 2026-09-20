using System.Security.Claims;
using LifeOs.Api.Auth;
using Microsoft.AspNetCore.Authorization;

namespace LifeOs.Tests;

public sealed class AllowlistedUserHandlerTests
{
    [Fact]
    public async Task Allowlisted_verified_email_succeeds()
    {
        var context = await HandleAsync(
            "owner@example.com, other@example.com",
            email: "Owner@Example.com",
            verified: true,
            authenticated: true);

        Assert.True(context.HasSucceeded);
        Assert.False(context.HasFailed);
    }

    [Fact]
    public async Task Valid_token_for_someone_else_fails()
    {
        var context = await HandleAsync(
            "owner@example.com",
            email: "intruder@example.com",
            verified: true,
            authenticated: true);

        Assert.False(context.HasSucceeded);
        Assert.True(context.HasFailed);
    }

    [Fact]
    public async Task Unverified_email_fails_even_when_allowlisted()
    {
        var context = await HandleAsync(
            "owner@example.com",
            email: "owner@example.com",
            verified: false,
            authenticated: true);

        Assert.False(context.HasSucceeded);
        Assert.True(context.HasFailed);
    }

    [Fact]
    public async Task Missing_email_fails()
    {
        var context = await HandleAsync(
            "owner@example.com",
            email: null,
            verified: true,
            authenticated: true);

        Assert.False(context.HasSucceeded);
        Assert.True(context.HasFailed);
    }

    [Fact]
    public async Task Unauthenticated_principal_does_not_succeed()
    {
        var context = await HandleAsync(
            "owner@example.com",
            email: "owner@example.com",
            verified: true,
            authenticated: false);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Supabase_google_token_with_nested_verification_succeeds()
    {
        var options = new SupabaseAuthOptions { AllowedEmails = "owner@example.com" };
        options.Normalize();
        var handler = new AllowlistedUserHandler(options);
        var user = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("email", "owner@example.com"),
            new Claim("app_metadata", """{"provider":"google","providers":["google"]}"""),
            new Claim("user_metadata", """{"email":"owner@example.com","email_verified":true,"iss":"https://accounts.google.com"}"""),
        ], authenticationType: "Bearer"));
        var context = new AuthorizationHandlerContext(
            [new AllowlistedUserRequirement()], user, resource: null);
        await handler.HandleAsync(context);
        Assert.True(context.HasSucceeded);
    }

    private static async Task<AuthorizationHandlerContext> HandleAsync(
        string allowedEmails,
        string? email,
        bool verified,
        bool authenticated)
    {
        var options = new SupabaseAuthOptions { AllowedEmails = allowedEmails };
        options.Normalize();
        var handler = new AllowlistedUserHandler(options);

        var claims = new List<Claim>();
        if (email is not null)
        {
            claims.Add(new Claim("email", email));
        }

        if (verified)
        {
            claims.Add(new Claim("email_verified", "true"));
        }
        else
        {
            claims.Add(new Claim("email_verified", "false"));
        }

        var identity = authenticated
            ? new ClaimsIdentity(claims, authenticationType: "Bearer")
            : new ClaimsIdentity(claims);
        var user = new ClaimsPrincipal(identity);
        var context = new AuthorizationHandlerContext(
            [new AllowlistedUserRequirement()], user, resource: null);
        await handler.HandleAsync(context);
        return context;
    }
}
