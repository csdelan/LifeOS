using System.Text.RegularExpressions;

namespace LifeOs.Tests;

/// <summary>
/// The server's twin of the web's <c>scoped-reads.test.ts</c> (code review CR119, fifth review
/// R14): a read reads what it shows. Focus and <c>life_snapshot</c> read every open item of every
/// type, an Appointment's occurrences two years ahead and every Person, Area, Value and Metric among
/// them, to use the Goals, Projects and Tasks. This finds a list of subjects that reads every type
/// coming back, in the API's code.
/// </summary>
public sealed partial class ScopedReadsTests
{
    private static readonly string Source = Path.Combine(RepoRoot.Path, "src");

    /// <summary>Each read of a list of subjects that can be of every type, by file and method, and why.</summary>
    private static readonly Dictionary<string, string> EveryType = new()
    {
        ["LifeOs.Api/Http/ReadEndpoints.cs ListSubjects"] =
            "GET /api/subjects: the caller names its type, or reads every one; Browse and the Map read every one (fifth review R15).",
        ["LifeOs.Api/Mcp/Tools/ItemTools.cs ListItems"] =
            "list_items: an agent names the type, or lists every one, at most 200.",
        ["LifeOs.Api/Mcp/Tools/ItemTools.cs Alignment"] =
            "alignment: the whole tree, of every type that serves (fifth review R15).",
    };

    [Fact]
    public void A_list_of_subjects_reads_every_type_only_where_it_shows_them_all()
    {
        var everyType = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(Source, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(Source, file).Replace('\\', '/');
            if (relative.Contains("/obj/", StringComparison.Ordinal) || relative.Contains("/bin/", StringComparison.Ordinal)
                || relative.StartsWith("LifeOs.Pilot/", StringComparison.Ordinal)
                || relative == "LifeOs.Api/Read/SubjectReader.cs")
            {
                continue;
            }

            var text = File.ReadAllText(file);
            foreach (Match call in ListCall().Matches(text))
            {
                if (ReadsEveryType(call.Groups["name"].Value, Arguments(text, call.Index + call.Length - 1)))
                {
                    everyType.Add($"{relative} {Method(text, call.Index)}");
                }
            }
        }

        Assert.Equal(EveryType.Keys.Order(StringComparer.Ordinal), everyType);
    }

    [Fact]
    public void The_check_finds_a_read_of_every_type()
    {
        Assert.True(ReadsEveryType("ListSubjectsAsync", "new SubjectFilter { Status = SubjectFilter.Open, HiddenTypes = hidden }, ct"));
        Assert.True(ReadsEveryType("ListSubjectsAsync", "new SubjectFilter { Type = type, Limit = 10 }, ct"));
        Assert.True(ReadsEveryType("QuerySubjectsAsync", "null, cancellationToken: ct"));
        Assert.True(ReadsEveryType("QuerySubjectsAsync", "type, cancellationToken: ct"));
        Assert.False(ReadsEveryType("ListSubjectsAsync", "new SubjectFilter { Types = Types, Status = SubjectFilter.Open }, ct"));
        Assert.False(ReadsEveryType("ListSubjectsAsync", "new SubjectFilter { Type = SubjectTypes.Task, Status = SubjectFilter.Open }, ct"));
        Assert.False(ReadsEveryType("QuerySubjectsAsync", "SubjectTypes.Value, cancellationToken: ct"));
        Assert.False(ReadsEveryType("GetSubjectsAsync", "SubjectTypes.Goal, cancellationToken: ct"));
    }

    // A type named by a SubjectTypes constant or a named list of them is scoped; a variable, which
    // may be null, or none at all reads every type.
    private static bool ReadsEveryType(string name, string arguments)
    {
        if (name is "QuerySubjectsAsync" or "GetSubjectsAsync")
        {
            return !arguments.TrimStart().StartsWith("SubjectTypes.", StringComparison.Ordinal);
        }

        var type = TypeSet().Match(arguments);
        return !type.Success || !Scoped().IsMatch(type.Groups["value"].Value.Trim());
    }

    // From an opening parenthesis to its match.
    private static string Arguments(string text, int open)
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

    // The method a call is in: the last member declared before it.
    private static string Method(string text, int index)
        => Declaration().Matches(text[..index]).LastOrDefault()?.Groups["method"].Value ?? "?";

    [GeneratedRegex(@"\.(?<name>ListSubjectsAsync|QuerySubjectsAsync|GetSubjectsAsync)\(")]
    private static partial Regex ListCall();

    [GeneratedRegex(@"\bTypes?\s*=\s*(?<value>[^,}\r\n]+)")]
    private static partial Regex TypeSet();

    [GeneratedRegex(@"^(SubjectTypes\.\w+|[A-Z]\w*(\.[A-Z]\w*)*)$")]
    private static partial Regex Scoped();

    [GeneratedRegex(@"(?m)^\s*(?:public|private|internal|protected)\b[^;=\n]*?\s(?<method>\w+)\s*\(")]
    private static partial Regex Declaration();
}
