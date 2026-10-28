using System.Security.Claims;
using System.Text.Json;

namespace LifeOs.Api.Auth;

/// <summary>
/// What a Supabase access token says about who signed in and how (agent-access plan §4.4).
/// Only claims Supabase itself writes are read: the top-level <c>email</c>, <c>amr</c> (how the
/// session signed in) and <c>app_metadata</c> (which only an admin can change). Never
/// <c>user_metadata</c>: the user, and so any agent holding one of the user's tokens, can
/// rewrite it with <c>PUT /auth/v1/user</c>.
/// </summary>
public static class SupabaseClaims
{
    /// <summary>
    /// <c>amr</c> method for a sign-in through an outside provider. Google is the only one
    /// switched on, so for LifeOS this means a Google sign-in.
    /// </summary>
    public const string OAuthSignIn = "oauth";

    /// <summary><c>amr</c> method for a token Supabase's OAuth server issued to an agent.</summary>
    public const string AgentSignIn = "oauth_provider/authorization_code";

    private const string Google = "google";

    /// <summary>The account's email (the top-level <c>email</c> claim), or null.</summary>
    public static string? ReadEmail(ClaimsPrincipal user)
        => user.FindFirst("email")?.Value is { } email && !string.IsNullOrWhiteSpace(email) ? email.Trim() : null;

    /// <summary>
    /// How the session signed in: the <c>amr</c> methods, most recent first as Supabase lists
    /// them. Each entry is an object (<c>{"method":"oauth","timestamp":…}</c>) or a plain string.
    /// </summary>
    public static IReadOnlyList<string> SignInMethods(ClaimsPrincipal user)
    {
        var methods = new List<string>();
        foreach (var claim in user.FindAll("amr"))
        {
            var value = claim.Value?.Trim();
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            if (value[0] != '{')
            {
                methods.Add(value);
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(value);
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("method", out var method)
                    && method.ValueKind == JsonValueKind.String
                    && method.GetString() is { Length: > 0 } name)
                {
                    methods.Add(name);
                }
            }
            catch (JsonException)
            {
                // Not an amr entry: ignore it.
            }
        }

        return methods;
    }

    /// <summary>
    /// The account signs in with Google: <c>app_metadata</c> names Google as its provider.
    /// Google checks the address, so its email is verified.
    /// </summary>
    public static bool IsGoogleAccount(ClaimsPrincipal user)
    {
        foreach (var claim in user.FindAll("app_metadata"))
        {
            if (string.IsNullOrWhiteSpace(claim.Value) || claim.Value.TrimStart()[0] != '{')
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(claim.Value);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (root.TryGetProperty("provider", out var provider)
                    && provider.ValueKind == JsonValueKind.String
                    && string.Equals(provider.GetString(), Google, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (root.TryGetProperty("providers", out var providers)
                    && providers.ValueKind == JsonValueKind.Array
                    && providers.EnumerateArray().Any(p => p.ValueKind == JsonValueKind.String
                        && string.Equals(p.GetString(), Google, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
            catch (JsonException)
            {
                // Not Supabase's app_metadata: ignore it.
            }
        }

        return false;
    }

    /// <summary>
    /// A browser session that signed in with Google: a Google account whose session's
    /// <c>amr</c> has <see cref="OAuthSignIn"/>. A password or magic-link session doesn't count,
    /// so a password an agent sets on the account opens nothing here.
    /// </summary>
    public static bool SignedInWithGoogle(ClaimsPrincipal user)
        => IsGoogleAccount(user) && SignInMethods(user).Contains(OAuthSignIn, StringComparer.Ordinal);

    /// <summary>
    /// A token Supabase's OAuth server issued to an agent for a Google account: its session's
    /// <c>amr</c> has <see cref="AgentSignIn"/>.
    /// </summary>
    public static bool IssuedToAgent(ClaimsPrincipal user)
        => IsGoogleAccount(user) && SignInMethods(user).Contains(AgentSignIn, StringComparer.Ordinal);
}
