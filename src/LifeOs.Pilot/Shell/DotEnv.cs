namespace LifeOs.Pilot.Shell;

/// <summary>
/// Minimal .env reader: KEY=VALUE lines, '#' comments, surrounding quotes
/// stripped. Values may themselves contain '=' (connection strings do), so only
/// the first '=' splits the line. Mirrors what scripts/migrate-staging.ps1 does.
/// </summary>
internal static class DotEnv
{
    public static IReadOnlyDictionary<string, string> Read(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator < 1)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim().Trim('"', '\'');
            if (key.Length > 0)
            {
                values[key] = value;
            }
        }

        return values;
    }
}
