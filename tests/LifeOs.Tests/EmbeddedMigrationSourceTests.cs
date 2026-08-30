using LifeOs.Infrastructure.Migrations;

namespace LifeOs.Tests;

/// <summary>
/// Unit tests for migration discovery. These need no database and run anywhere.
/// </summary>
public sealed class EmbeddedMigrationSourceTests
{
    [Fact]
    public void Loads_the_embedded_baseline_migration()
    {
        var scripts = new EmbeddedMigrationSource().Load();

        var baseline = Assert.Single(scripts, s => s.Version == 1);
        Assert.Equal("baseline", baseline.Name);
        Assert.Contains("CREATE SCHEMA IF NOT EXISTS bsk", baseline.Sql);
    }

    [Fact]
    public void Returns_scripts_ordered_by_version()
    {
        var versions = new EmbeddedMigrationSource().Load()
            .Select(s => s.Version)
            .ToList();

        var sorted = versions.OrderBy(v => v).ToList();
        Assert.Equal(sorted, versions);
    }

    [Fact]
    public void Computes_a_stable_lowercase_sha256_checksum()
    {
        var script = new MigrationScript(1, "baseline", "SELECT 1;");

        // SHA-256 of "SELECT 1;", lowercase hex.
        Assert.Equal(64, script.Checksum.Length);
        Assert.Equal(script.Checksum, script.Checksum.ToLowerInvariant());
        Assert.Equal(new MigrationScript(1, "baseline", "SELECT 1;").Checksum, script.Checksum);
    }

    [Fact]
    public void Checksum_is_invariant_to_line_endings()
    {
        var crlf = new MigrationScript(1, "x", "CREATE TABLE t (id int);\r\nSELECT 1;\r\n");
        var lf = new MigrationScript(1, "x", "CREATE TABLE t (id int);\nSELECT 1;\n");
        var cr = new MigrationScript(1, "x", "CREATE TABLE t (id int);\rSELECT 1;\r");

        Assert.Equal(lf.Checksum, crlf.Checksum);
        Assert.Equal(lf.Checksum, cr.Checksum);
    }
}
