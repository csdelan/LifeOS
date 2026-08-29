using System.Reflection;
using System.Text.RegularExpressions;

namespace LifeOs.Infrastructure.Migrations;

/// <summary>
/// Loads migration scripts embedded in this assembly (from <c>db/migrations</c>
/// at the repo root) and returns them ordered by version. Embedding keeps the
/// runner independent of the process working directory, so it behaves
/// identically from the CLI and from the test harness.
/// </summary>
public sealed partial class EmbeddedMigrationSource
{
    private const string ResourcePrefix = "LifeOs.Infrastructure.Migrations.";

    private readonly Assembly _assembly;

    public EmbeddedMigrationSource()
        : this(typeof(EmbeddedMigrationSource).Assembly)
    {
    }

    public EmbeddedMigrationSource(Assembly assembly) => _assembly = assembly;

    public IReadOnlyList<MigrationScript> Load()
    {
        var scripts = new List<MigrationScript>();

        foreach (var resourceName in _assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(ResourcePrefix, StringComparison.Ordinal) ||
                !resourceName.EndsWith(".sql", StringComparison.Ordinal))
            {
                continue;
            }

            var fileName = resourceName[ResourcePrefix.Length..];
            var match = FileNamePattern().Match(fileName);
            if (!match.Success)
            {
                throw new InvalidOperationException(
                    $"Migration resource '{fileName}' does not match the required NNNN__name.sql pattern.");
            }

            var version = long.Parse(match.Groups["version"].Value);
            var name = match.Groups["name"].Value;

            using var stream = _assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Could not open migration resource '{resourceName}'.");
            using var reader = new StreamReader(stream);
            var sql = reader.ReadToEnd();

            scripts.Add(new MigrationScript(version, name, sql));
        }

        var duplicate = scripts
            .GroupBy(s => s.Version)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Duplicate migration version {duplicate.Key}.");
        }

        scripts.Sort((a, b) => a.Version.CompareTo(b.Version));
        return scripts;
    }

    [GeneratedRegex(@"^(?<version>\d{4,})__(?<name>.+)\.sql$", RegexOptions.CultureInvariant)]
    private static partial Regex FileNamePattern();
}
