using System.Text.Json;
using System.Text.Json.Serialization;

namespace LifeOs.Pilot.Shell;

/// <summary>
/// Remembers which environment the pilot last ran as, and where the staging .env
/// lives — persisted next to the window-placement file so both survive restarts
/// (like window position). Best-effort: a corrupt file never blocks startup.
/// </summary>
internal sealed class EnvironmentSettings
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;

    private EnvironmentSettings(string path, PilotEnvironment environment, string? envFilePath)
    {
        _path = path;
        Environment = environment;
        EnvFilePath = envFilePath;
    }

    public PilotEnvironment Environment { get; set; }

    /// <summary>Full path to the .env used in Staging. May be null until one is chosen.</summary>
    public string? EnvFilePath { get; set; }

    public static EnvironmentSettings Load(string path, string? defaultEnvFilePath)
    {
        try
        {
            if (File.Exists(path))
            {
                var dto = JsonSerializer.Deserialize<Dto>(File.ReadAllText(path), Json);
                if (dto is not null)
                {
                    return new EnvironmentSettings(path, dto.Environment, dto.EnvFilePath ?? defaultEnvFilePath);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Fall through to defaults.
        }

        return new EnvironmentSettings(path, PilotEnvironment.Dev, defaultEnvFilePath);
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(new Dto(Environment, EnvFilePath), Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort; never block shutdown.
        }
    }

    private sealed record Dto(PilotEnvironment Environment, string? EnvFilePath);
}
