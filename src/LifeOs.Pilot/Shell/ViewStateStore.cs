using System.Text.Json;

namespace LifeOs.Pilot.Shell;

/// <summary>
/// Persists per-tab filters / sort / selection across restarts (NAV-1), next to
/// the window-placement file. Best-effort: a corrupt file never blocks startup.
/// </summary>
internal sealed class ViewStateStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string _path;
    private readonly Dictionary<string, Dictionary<string, string>> _tabs;

    private ViewStateStore(string path, Dictionary<string, Dictionary<string, string>> tabs)
    {
        _path = path;
        _tabs = tabs;
    }

    public static ViewStateStore Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var tabs = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(
                    File.ReadAllText(path), Json);
                if (tabs is not null)
                {
                    return new ViewStateStore(path, tabs);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Fall through to empty.
        }

        return new ViewStateStore(path, new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal));
    }

    public string? Get(string tab, string key)
        => _tabs.TryGetValue(tab, out var values) && values.TryGetValue(key, out var value) ? value : null;

    public void Set(string tab, string key, string? value)
    {
        if (!_tabs.TryGetValue(tab, out var values))
        {
            values = new Dictionary<string, string>(StringComparer.Ordinal);
            _tabs[tab] = values;
        }

        if (string.IsNullOrEmpty(value))
        {
            values.Remove(key);
        }
        else
        {
            values[key] = value;
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_tabs, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort; never block shutdown.
        }
    }
}
