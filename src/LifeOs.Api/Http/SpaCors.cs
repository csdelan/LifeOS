namespace LifeOs.Api.Http;

public static class SpaCors
{
    public const string PolicyName = "spa";

    public static string[] ResolveOrigins(IConfiguration configuration)
    {
        var items = new List<string>();
        var section = configuration.GetSection("Cors:AllowedOrigins");
        var children = section.GetChildren().ToList();
        if (children.Count > 0)
        {
            items.AddRange(children
                .Select(child => child.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.Trim()));
        }
        else if (!string.IsNullOrWhiteSpace(section.Value))
        {
            items.AddRange(Split(section.Value));
        }

        var extra = configuration["CORS_ALLOWED_ORIGINS"];
        if (!string.IsNullOrWhiteSpace(extra))
        {
            items.AddRange(Split(extra));
        }

        return items
            .Where(static origin => origin.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> Split(string value)
        => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
