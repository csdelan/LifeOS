using System.Reflection;
using System.Text.RegularExpressions;

namespace LifeOs.Infrastructure.Diagnostics;

/// <summary>
/// Loads diagnostic SQL scripts embedded in an assembly (from
/// <c>db/diagnostics</c> at the repo root) and returns them ordered by their
/// filename prefix. Embedding keeps the runner independent of the process
/// working directory, exactly as <see cref="Migrations.EmbeddedMigrationSource"/>
/// does for migrations, so it behaves identically from the CLI and the tests.
/// </summary>
public sealed partial class EmbeddedDiagnosticSource
{
    private const string ResourcePrefix = "LifeOs.Infrastructure.Diagnostics.";

    private readonly Assembly _assembly;

    public EmbeddedDiagnosticSource()
        : this(typeof(EmbeddedDiagnosticSource).Assembly)
    {
    }

    public EmbeddedDiagnosticSource(Assembly assembly) => _assembly = assembly;

    public IReadOnlyList<Diagnostic> Load()
    {
        var diagnostics = new List<Diagnostic>();

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
                    $"Diagnostic resource '{fileName}' does not match the required NN__slug.sql pattern.");
            }

            var order = long.Parse(match.Groups["order"].Value);
            var name = match.Groups["name"].Value;

            using var stream = _assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Could not open diagnostic resource '{resourceName}'.");
            using var reader = new StreamReader(stream);
            var sql = reader.ReadToEnd();

            var title = ParseTitle(sql) ?? name;

            diagnostics.Add(new Diagnostic(name, title, order, sql));
        }

        var duplicate = diagnostics
            .GroupBy(d => d.Name, StringComparer.Ordinal)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Duplicate diagnostic name '{duplicate.Key}'.");
        }

        diagnostics.Sort((a, b) =>
        {
            var byOrder = a.Order.CompareTo(b.Order);
            return byOrder != 0 ? byOrder : string.CompareOrdinal(a.Name, b.Name);
        });
        return diagnostics;
    }

    // The human rule statement, read from the first `-- title:` header line.
    private static string? ParseTitle(string sql)
    {
        var match = TitlePattern().Match(sql);
        return match.Success ? match.Groups["title"].Value.Trim() : null;
    }

    [GeneratedRegex(@"^(?<order>\d{2,})__(?<name>.+)\.sql$", RegexOptions.CultureInvariant)]
    private static partial Regex FileNamePattern();

    [GeneratedRegex(@"^\s*--\s*title:\s*(?<title>.+?)\s*$",
        RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex TitlePattern();
}
