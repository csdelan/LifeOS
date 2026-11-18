using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Tests;

/// <summary>
/// Code review CR140: a door reads a day as <c>yyyy-MM-dd</c> and nothing else. <c>/api/adhere</c>
/// and <c>/api/recur</c> read their day with <c>DateOnly.TryParse</c>, which takes
/// <c>03/04/2026</c> as 4 March, so a check-off typed the other way round landed on the wrong day,
/// in a log that is never edited, while <c>bsk recur</c> refused the same text.
/// </summary>
public sealed class StrictDaysTests(ClassDatabase classDb) : IClassFixture<ClassDatabase>
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// The places that may read a date leniently, each with why. Everything else in <c>src</c> reads
    /// a day exactly.
    /// </summary>
    private static readonly Dictionary<string, string> Lenient = new(StringComparer.Ordinal)
    {
        [Path.Combine("LifeOs.Pilot", "Shell", "Ui.cs")] =
            "the Pilot's own text boxes, where Chris types a date in his PC's format; what it writes goes to bsk as yyyy-MM-dd",
    };

    [Fact]
    public void No_door_reads_a_day_leniently()
    {
        var source = Path.Combine(RepoRoot.Path, "src");
        var lenient = new Regex(@"\bDateOnly\.(Try)?Parse\(");
        var found = Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => !Lenient.ContainsKey(Path.GetRelativePath(source, f)))
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (File: Path.GetRelativePath(source, f), Line: i + 1, Text: line)))
            .Where(l => lenient.IsMatch(l.Text))
            .Select(l => $"{l.File}:{l.Line}: {l.Text.Trim()}")
            .ToList();

        Assert.Empty(found);
    }

    [Fact]
    public async Task A_check_off_on_a_day_written_another_way_is_refused()
    {
        await using var kernel = classDb.Services().BuildServiceProvider();
        var habit = await kernel.CreateTrackedHabitAsync($"Read {Guid.NewGuid():N}", cancellationToken: Ct);
        await using var host = await ApiTestHost.StartAsync(classDb.ConnectionString, Ct);

        var response = await host.Client.PostAsJsonAsync(
            "/api/adhere", new { habit = habit.Urn, state = "followed", on = "03/04/2026" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
