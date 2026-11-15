using System.Text.RegularExpressions;

namespace LifeOs.Tests;

/// <summary>
/// The API's lists that can answer every row there is (code review CR121, fifth review R16). The
/// shell read every timer ever to find the running one, a detail every habit to find its own, and
/// the journal and status history read every entry. A list endpoint is bounded when its route is
/// one item's (<c>{id}</c>) or it takes a limit or a day range; any other is listed here with why,
/// so a new one is a choice made on purpose. <c>scoped-reads.test.ts</c> checks the web asks each
/// for what it shows.
/// </summary>
public sealed partial class UnboundedReadsTests
{
    private static readonly string Http = Path.Combine(RepoRoot.Path, "src", "LifeOs.Api", "Http");

    /// <summary>Each list endpoint that takes no bound, by file and route, and why it may answer every row.</summary>
    private static readonly Dictionary<string, string> Unbounded = new()
    {
        ["EnvironmentEndpoints.cs /spaces"] = "The home's spaces: a room each, a dozen or so.",
        ["MeasurementEndpoints.cs /catalog"] = "The starter metrics, a fixed list in code.",
        ["MeasurementEndpoints.cs /metrics"] = "The metrics Chris tracks, with each one's latest reading: a few dozen.",
        ["ReadEndpoints.cs /areas"] = "Areas: a handful Chris makes by hand.",
        ["ReadEndpoints.cs /habits"] = "Every live habit, for the Habits page and a review; a detail reads its own (GET /api/habits/{id}).",
        ["ReadEndpoints.cs /inbox"] = "What waits to be triaged: the Inbox page lists it all, and it empties as Chris works it; the badge reads /inbox/count.",
        ["ReadEndpoints.cs /people"] = "Everyone Chris has added, for the People page and the person pickers: a few hundred at most.",
        ["ReadEndpoints.cs /tags"] = "Every tag with its count, for the tag pickers: words Chris picks, a few hundred at most.",
        ["ReadEndpoints.cs /timers"] = "With neither active nor status, every timed Task's timer; the web always asks for one (scoped-reads.test.ts).",
        ["RewardEndpoints.cs /bounty/defaults"] = "One default price per type, a fixed list.",
        ["RewardEndpoints.cs /rewards"] = "The rewards Chris has priced: a short list he keeps by hand.",
        ["TaskEndpoints.cs /series"] = "Every repeating Task, with its repetitions counted, not listed: a few dozen.",
    };

    [Fact]
    public void A_list_endpoint_takes_a_bound_or_says_why_it_answers_every_row()
    {
        var unbounded = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(Http, "*.cs"))
        {
            var text = File.ReadAllText(file);
            foreach (Match route in ListRoute().Matches(text))
            {
                var path = route.Groups["route"].Value;
                var handler = route.Groups["handler"].Value;
                var declared = Handler(handler).Match(text);
                Assert.True(declared.Success, $"{Path.GetFileName(file)}: no handler {handler} for {path}");
                if (!path.Contains('{', StringComparison.Ordinal) && !Bounded().IsMatch(Parameters(text, declared.Index + declared.Length - 1)))
                {
                    unbounded.Add($"{Path.GetFileName(file)} {path}");
                }
            }
        }

        // The whole list on a failure, not where the two first differ.
        var found = string.Join('\n', unbounded);
        Assert.True(string.Join('\n', Unbounded.Keys.Order(StringComparer.Ordinal)) == found, $"The unbounded list endpoints are:\n{found}");
    }

    [Fact]
    public void The_check_tells_a_bound_from_none()
    {
        Assert.Matches(Bounded(), "SubjectReader reader, int? limit = null, CancellationToken ct");
        Assert.Matches(Bounded(), "string? from, string? to, RewardReader reader");
        Assert.Matches(Bounded(), "string on, SubjectReader reader");
        Assert.DoesNotMatch(Bounded(), "SubjectReader reader, CancellationToken cancellationToken, bool includeArchived = false");
        Assert.DoesNotMatch(Bounded(), "SubjectReader reader, bool active = false, string? status = null");
        Assert.Matches(ListRoute(), "api.MapGet(\"/timers\", GetTimers).WithName(\"GetTimers\")\n    .Produces<IReadOnlyList<TaskTimer>>();");
        Assert.DoesNotMatch(ListRoute(), "api.MapGet(\"/focus\", GetFocus).WithName(\"GetFocus\")\n    .Produces<DashboardRead>();");
    }

    // From the handler's opening parenthesis to its match: the parameters.
    private static string Parameters(string text, int open)
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '(')
            {
                depth++;
            }
            else if (text[i] == ')' && --depth == 0)
            {
                return text[(open + 1)..i];
            }
        }

        return text[(open + 1)..];
    }

    private static Regex Handler(string name)
        => new($@"\b(?:Task<IResult>|IResult)\s+{Regex.Escape(name)}\s*\(");

    // A GET whose chained calls, up to the statement's end, say it answers a list.
    [GeneratedRegex("""\.MapGet\("(?<route>[^"]*)",\s*(?<handler>\w+)\)[^;]*?\.Produces<IReadOnlyList<""")]
    private static partial Regex ListRoute();

    // A parameter that bounds the rows: a limit, a day, or a range of days.
    [GeneratedRegex(@"\b(int\??|string\??|DateOnly\??)\s+(limit|on|from|to|days|day)\b")]
    private static partial Regex Bounded();
}
