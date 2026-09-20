using System.Text.Json;

namespace LifeOs.Api.Hosting;

/// <summary>
/// Public SPA config written at process start (not baked into the image).
/// Fly dashboard deploys do not pass Docker build-args; <c>fly secrets set</c>
/// supplies VITE_SUPABASE_URL / VITE_SUPABASE_ANON_KEY at runtime. Never put
/// owner/reader connection strings or the service-role key here.
/// </summary>
public static class SpaPublicConfig
{
    public const string FileName = "public-config.js";

    public static (string ApiBaseUrl, string SupabaseUrl, string SupabaseAnonKey) Resolve(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var supabaseUrl = Pick(configuration, "VITE_SUPABASE_URL", "Public:SupabaseUrl");
        var supabaseAnonKey = Pick(configuration, "VITE_SUPABASE_ANON_KEY", "Public:SupabaseAnonKey");
        var apiBaseUrl = Pick(configuration, "VITE_API_BASE_URL", "Public:ApiBaseUrl");
        if (string.IsNullOrWhiteSpace(apiBaseUrl))
        {
            apiBaseUrl = "/";
        }

        if (!environment.IsDevelopment()
            && (string.IsNullOrWhiteSpace(supabaseUrl) || string.IsNullOrWhiteSpace(supabaseAnonKey)))
        {
            throw new InvalidOperationException(
                "VITE_SUPABASE_URL and VITE_SUPABASE_ANON_KEY must be set at runtime "
                + "(fly secrets set). These are the public anon values — never the service-role key.");
        }

        return (apiBaseUrl, supabaseUrl, supabaseAnonKey);
    }

    public static void Write(
        string? webRoot,
        string apiBaseUrl,
        string supabaseUrl,
        string supabaseAnonKey)
    {
        if (string.IsNullOrEmpty(webRoot) || !Directory.Exists(webRoot))
        {
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            apiBaseUrl,
            supabaseUrl,
            supabaseAnonKey,
        });
        File.WriteAllText(
            Path.Combine(webRoot, FileName),
            "window.__LIFEOS_PUBLIC_CONFIG__=" + payload + ";");
    }

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
