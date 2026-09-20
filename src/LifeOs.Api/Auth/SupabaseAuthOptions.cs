namespace LifeOs.Api.Auth;

/// <summary>
/// Supabase Auth settings for JWT validation and the single-user allowlist.
/// Bound from configuration / environment — never from source.
/// </summary>
public sealed class SupabaseAuthOptions
{
    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
    public string JwksUrl { get; set; } = "";
    public string JwtSecret { get; set; } = "";
    public string AllowedEmails { get; set; } = "";

    public IReadOnlySet<string> AllowedEmailSet { get; private set; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public static SupabaseAuthOptions From(IConfiguration configuration)
    {
        var options = new SupabaseAuthOptions
        {
            Issuer = Pick(configuration, "Auth:Issuer", "AUTH_ISSUER"),
            Audience = Pick(configuration, "Auth:Audience", "AUTH_AUDIENCE"),
            JwksUrl = Pick(configuration, "Auth:JwksUrl", "AUTH_JWKS_URL"),
            JwtSecret = Pick(configuration, "Auth:JwtSecret", "AUTH_JWT_SECRET"),
            AllowedEmails = Pick(configuration, "ALLOWED_EMAILS", "Auth:AllowedEmails"),
        };
        options.Normalize();
        return options;
    }

    public void Normalize()
    {
        Issuer = Issuer.Trim().TrimEnd('/');
        Audience = Audience.Trim();
        JwksUrl = JwksUrl.Trim();
        JwtSecret = JwtSecret.Trim();
        AllowedEmails = AllowedEmails.Trim();

        if (string.IsNullOrWhiteSpace(JwksUrl)
            && string.IsNullOrWhiteSpace(JwtSecret)
            && !string.IsNullOrWhiteSpace(Issuer))
        {
            JwksUrl = Issuer + "/.well-known/jwks.json";
        }

        AllowedEmailSet = AllowedEmails
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static email => email.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Issuer))
        {
            throw new InvalidOperationException(
                "Auth:Issuer (or AUTH_ISSUER) must be the Supabase Auth issuer, e.g. https://<project-ref>.supabase.co/auth/v1.");
        }

        if (string.IsNullOrWhiteSpace(Audience))
        {
            throw new InvalidOperationException(
                "Auth:Audience (or AUTH_AUDIENCE) must be set. Supabase access tokens use \"authenticated\".");
        }

        if (string.IsNullOrWhiteSpace(JwksUrl) && string.IsNullOrWhiteSpace(JwtSecret))
        {
            throw new InvalidOperationException(
                "Configure Auth:JwksUrl (Supabase JWKS) and/or Auth:JwtSecret (legacy HS256 fallback).");
        }

        if (AllowedEmailSet.Count == 0)
        {
            throw new InvalidOperationException(
                "ALLOWED_EMAILS (or Auth:AllowedEmails) must list the Google account(s) allowed to use this app.");
        }
    }

    public bool IsAllowed(string email) => AllowedEmailSet.Contains(email.Trim());

    private static string Pick(IConfiguration configuration, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = configuration[key];
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return "";
    }
}
