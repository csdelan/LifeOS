using System.Security.Claims;
using System.Text.Json;

namespace LifeOs.Api.Auth;

public static class EmailClaims
{
    public static string? ReadEmail(ClaimsPrincipal user)
    {
        var email = FirstNonEmpty(
            user.FindFirst("email")?.Value,
            user.FindFirst(ClaimTypes.Email)?.Value,
            ReadMetadataString(user, "email"));
        return string.IsNullOrWhiteSpace(email) ? null : email.Trim();
    }

    public static bool IsEmailVerified(ClaimsPrincipal user)
    {
        if (IsTruthy(user.FindFirst("email_verified")?.Value))
        {
            return true;
        }

        if (ReadMetadataTruthy(user, "email_verified"))
        {
            return true;
        }

        // Google OAuth via Supabase: Google has already verified the address.
        if (IsGoogle(user) && !string.IsNullOrWhiteSpace(ReadEmail(user)))
        {
            return true;
        }

        return false;
    }

    private static string? ReadMetadataString(ClaimsPrincipal user, string property)
    {
        foreach (var claim in user.FindAll("user_metadata"))
        {
            if (TryReadJsonString(claim.Value, property, out var value))
            {
                return value;
            }
        }

        return null;
    }

    private static bool ReadMetadataTruthy(ClaimsPrincipal user, string property)
    {
        foreach (var claim in user.FindAll("user_metadata"))
        {
            if (TryReadJsonTruthy(claim.Value, property))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryReadJsonString(string? json, string property, out string? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(json) || json[0] is not '{' and not '[')
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty(property, out var el)
                && el.ValueKind == JsonValueKind.String)
            {
                value = el.GetString();
                return !string.IsNullOrWhiteSpace(value);
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }

    private static bool TryReadJsonTruthy(string? json, string property)
    {
        if (string.IsNullOrWhiteSpace(json) || json[0] is not '{' and not '[')
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty(property, out var el))
            {
                return false;
            }

            return el.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.String => IsTruthy(el.GetString()),
                JsonValueKind.Number => el.TryGetInt32(out var n) && n != 0,
                _ => false
            };
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsGoogle(ClaimsPrincipal user)
    {
        if (string.Equals(ReadMetadataString(user, "iss"), "https://accounts.google.com", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var claim in user.FindAll("app_metadata"))
        {
            if (TryReadJsonString(claim.Value, "provider", out var provider)
                && string.Equals(provider, "google", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTruthy(string? value)
        => value is "true" or "True" or "TRUE" or "1";

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(static v => !string.IsNullOrWhiteSpace(v));
}
