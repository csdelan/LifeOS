using LifeOs.Infrastructure.Diagnostics;

namespace LifeOs.Tests;

/// <summary>
/// Unit tests for diagnostic discovery. These need no database. They run against
/// the fixture SQL embedded in this test assembly (see the DiagnosticFixtures
/// item group in the csproj), which the source discovers exactly as it discovers
/// the real diagnostics embedded in LifeOs.Infrastructure.
/// </summary>
public sealed class EmbeddedDiagnosticSourceTests
{
    private static EmbeddedDiagnosticSource FixtureSource()
        => new(typeof(EmbeddedDiagnosticSourceTests).Assembly);

    [Fact]
    public void Discovers_the_embedded_fixture_diagnostics_by_slug()
    {
        var names = FixtureSource().Load().Select(d => d.Name).ToList();

        Assert.Contains("titled", names);
        Assert.Contains("untitled", names);
    }

    [Fact]
    public void Reads_the_title_header_as_the_rule_statement()
    {
        var titled = Assert.Single(FixtureSource().Load(), d => d.Name == "titled");

        Assert.Equal("A titled fixture diagnostic", titled.Title);
    }

    [Fact]
    public void Falls_back_to_the_slug_when_there_is_no_title_header()
    {
        var untitled = Assert.Single(FixtureSource().Load(), d => d.Name == "untitled");

        Assert.Equal("untitled", untitled.Title);
    }

    [Fact]
    public void Orders_diagnostics_by_their_filename_prefix()
    {
        var loaded = FixtureSource().Load();

        var titledIndex = IndexOf(loaded, "titled");
        var untitledIndex = IndexOf(loaded, "untitled");

        // 10__titled.sql before 20__untitled.sql.
        Assert.True(titledIndex < untitledIndex);
    }

    [Fact]
    public void Production_assembly_ships_the_neglect_diagnostic()
    {
        // M4.2 added the first real diagnostic; more land in M4.3-M4.6.
        var names = new EmbeddedDiagnosticSource().Load().Select(d => d.Name).ToList();

        Assert.Contains("neglect", names);
    }

    private static int IndexOf(IReadOnlyList<Diagnostic> diagnostics, string name)
    {
        for (var i = 0; i < diagnostics.Count; i++)
        {
            if (diagnostics[i].Name == name)
            {
                return i;
            }
        }

        return -1;
    }
}
