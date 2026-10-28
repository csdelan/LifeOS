using LifeOs.Api.Auth;

namespace LifeOs.Tests;

/// <summary>
/// Claims shaped like a Supabase access token's (agent-access plan §4.4): a top-level
/// <c>email</c>, <c>amr</c> saying how the session signed in, and <c>app_metadata</c> naming
/// the account's provider. A web session signs in with Google (<c>oauth</c>); an agent's token,
/// with a <c>client_id</c>, comes from the OAuth server (<c>oauth_provider/authorization_code</c>).
/// </summary>
public static class SupabaseTokens
{
    public const string Password = "password";
    public const string MagicLink = "otp";

    /// <param name="signIn">The <c>amr</c> method; by default Google for a web session, or the
    /// OAuth server for an agent's token. Null with <paramref name="noAmr"/> leaves amr out.</param>
    /// <param name="google">Whether <c>app_metadata</c> names Google as the account's provider
    /// (otherwise email).</param>
    /// <param name="userMetadata">Extra <c>user_metadata</c>, which the API must ignore.</param>
    public static Dictionary<string, object> Claims(
        string? email,
        string? clientId = null,
        string? signIn = null,
        bool google = true,
        bool noAmr = false,
        Dictionary<string, object>? userMetadata = null)
    {
        var claims = new Dictionary<string, object>
        {
            ["sub"] = "user-1",
            ["role"] = "authenticated",
            ["aal"] = "aal1",
            ["session_id"] = "session-1",
            ["is_anonymous"] = false,
            ["app_metadata"] = google
                ? new Dictionary<string, object> { ["provider"] = "google", ["providers"] = new[] { "google" } }
                : new Dictionary<string, object> { ["provider"] = "email", ["providers"] = new[] { "email" } },
            ["user_metadata"] = userMetadata ?? new Dictionary<string, object>
            {
                ["email"] = email ?? "",
                ["email_verified"] = true,
                ["iss"] = "https://accounts.google.com",
            },
        };
        if (email is not null)
        {
            claims["email"] = email;
        }

        if (!noAmr)
        {
            var method = signIn ?? (clientId is null ? SupabaseClaims.OAuthSignIn : SupabaseClaims.AgentSignIn);
            claims["amr"] = new object[]
            {
                new Dictionary<string, object>
                {
                    ["method"] = method,
                    ["timestamp"] = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds(),
                },
            };
        }

        if (clientId is not null)
        {
            claims["client_id"] = clientId;
            claims["scope"] = "openid email profile";
        }

        return claims;
    }
}
