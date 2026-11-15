using LifeOs.Api.Mcp;
using LifeOs.Domain;

namespace LifeOs.Api.Read;

/// <summary>
/// Browse (BROWSE-2, <c>GET /api/browse</c>, code review CR120): the type list's counts, the
/// type's filter options and columns, and a page of its items through the filters, sorted. The web
/// read every subject of every type and did all of it in the browser, every repetition of every
/// repeating Task, a review a day, and the Spaces and Metrics it then threw away among them.
/// </summary>
public sealed class BrowseReader(SubjectReader subjects, McpReads reads)
{
    public async Task<BrowseRead> ReadAsync(BrowseQuery query, CancellationToken cancellationToken = default)
    {
        BrowseRules.Check(query);
        var stamp = await reads.NowAsync(cancellationToken);
        var counts = await subjects.CountListedAsync(BrowseRules.TypeOrder, query.Archived, cancellationToken);
        var typed = await subjects.ListSubjectsAsync(
            new SubjectFilter { Types = BrowseRules.TypesFor(query.Type), IncludeArchived = query.Archived },
            cancellationToken);
        DateTime? since = BrowseRules.EnteredDays(query.Entered) is { } days
            ? (await reads.StartOfDayAsync(stamp.Today.AddDays(-(days - 1)), cancellationToken)).UtcDateTime
            : null;
        return BrowseRules.Page(query, typed, McpResults.Day(stamp.Today), since, counts);
    }
}

/// <summary>What Browse is asked for; each value as the web's <c>BrowseFilters</c> spells it.</summary>
public sealed record BrowseQuery
{
    /// <summary>A type Browse lists, or <see cref="BrowseRules.All"/>.</summary>
    public string Type { get; init; } = BrowseRules.All;

    /// <summary>Words that must all appear in the title or URN.</summary>
    public string Text { get; init; } = "";

    public IReadOnlyList<string> Statuses { get; init; } = [];

    /// <summary>Area URNs; <see cref="BrowseRules.NoArea"/> picks items without one.</summary>
    public IReadOnlyList<string> Areas { get; init; } = [];

    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>any, overdue, today, upcoming or none.</summary>
    public string Due { get; init; } = "any";

    /// <summary>any, or entered within the last 7, 30, 90 or 365 days, counting today.</summary>
    public string Entered { get; init; } = "any";

    /// <summary>Include archived items alongside the rest.</summary>
    public bool Archived { get; init; }

    /// <summary>title, type, status, due, entered, area or tags.</summary>
    public string Sort { get; init; } = "title";

    /// <summary>asc or desc.</summary>
    public string Dir { get; init; } = "asc";

    /// <summary>At most this many rows, from 1 to 1000.</summary>
    public int Limit { get; init; } = BrowseRules.PageSize;
}

/// <summary>
/// Browse's rules, as <c>web/src/lib/mock/browse.ts</c> copies them for the mock;
/// <c>tests/rules/browse.json</c> holds both to the same answers. Categories combine with AND,
/// the values picked inside one with OR, and a picked value the type doesn't offer is ignored.
/// </summary>
public static class BrowseRules
{
    public const string All = "all";

    /// <summary>The area filter's value for an item with no Area.</summary>
    public const string NoArea = "__none__";

    /// <summary>A page of rows, as the web draws them.</summary>
    public const int PageSize = 200;

    /// <summary>Every type Browse lists, in its order: the alignment chain first.</summary>
    public static readonly IReadOnlyList<string> TypeOrder =
    [
        SubjectTypes.Value, SubjectTypes.Goal, SubjectTypes.Project, SubjectTypes.Task, SubjectTypes.Habit,
        SubjectTypes.Commitment, SubjectTypes.Decision, SubjectTypes.Problem, SubjectTypes.Idea,
        SubjectTypes.Appointment, SubjectTypes.Person, SubjectTypes.Area, SubjectTypes.Constraint,
        SubjectTypes.Season, SubjectTypes.Reward,
    ];

    private static readonly string[] DueFilters = ["any", "overdue", "today", "upcoming", "none"];
    private static readonly string[] EnteredFilters = ["any", "7", "30", "90", "365"];
    private static readonly string[] Sorts = ["title", "type", "status", "due", "entered", "area", "tags"];

    /// <summary>The types a read of <paramref name="type"/> lists: itself, or every type Browse lists.</summary>
    public static IReadOnlyList<string> TypesFor(string type) => type == All ? TypeOrder : [type];

    /// <summary>The days an Entered filter counts back, today among them, or null for any time.</summary>
    public static int? EnteredDays(string entered) => entered == "any" ? null : int.Parse(entered, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Refuses a query no Browse sends.</summary>
    public static void Check(BrowseQuery query)
    {
        static void OneOf(string name, string value, IReadOnlyCollection<string> allowed)
        {
            if (!allowed.Contains(value))
            {
                throw new ArgumentException($"{name} is one of {string.Join(", ", allowed)}, not '{value}'.");
            }
        }

        OneOf("type", query.Type, [All, .. TypeOrder]);
        OneOf("due", query.Due, DueFilters);
        OneOf("entered", query.Entered, EnteredFilters);
        OneOf("sort", query.Sort, Sorts);
        OneOf("dir", query.Dir, ["asc", "desc"]);
        if (query.Limit is < 1 or > 1000)
        {
            throw new ArgumentException($"limit is a whole number from 1 to 1000, not {query.Limit}.");
        }
    }

    /// <summary>
    /// The page for <paramref name="query"/> from the type's items (<paramref name="typed"/>,
    /// archived ones only when included), on <paramref name="today"/>; an item entered before
    /// <paramref name="enteredSince"/> fails the Entered filter. <paramref name="counts"/> is the
    /// type list's.
    /// </summary>
    public static BrowseRead Page(
        BrowseQuery query, IReadOnlyList<SubjectListItem> typed, string today, DateTime? enteredSince,
        IReadOnlyDictionary<string, int> counts)
    {
        var options = Options(typed, query.Type);
        var picked = query with
        {
            Statuses = Keep(query.Statuses, options.Statuses),
            Areas = Keep(query.Areas, options.Areas),
            Tags = Keep(query.Tags, options.Tags),
        };
        var matching = Sorted(typed.Where(i => Matches(i, picked, today, enteredSince)), query.Sort, query.Dir);
        return new BrowseRead
        {
            Types = [.. TypeOrder.Where(t => counts.GetValueOrDefault(t) > 0).Select(t => new BrowseTypeCount { Type = t, Count = counts[t] })],
            Total = typed.Count,
            Matching = matching.Count,
            Options = options,
            Columns = Columns(query.Type, typed),
            Rows = [.. matching.Take(query.Limit)],
        };
    }

    /// <summary>The shown status: the item's, else its type's first; a Habit's is Building. Empty for a type with none.</summary>
    public static string StatusOf(SubjectListItem item)
        => !string.IsNullOrEmpty(item.Status) ? item.Status
            : item.Type == SubjectTypes.Habit ? "Building"
            : StatusVocabulary.For(item.Type).FirstOrDefault() ?? "";

    public static IReadOnlyList<string> TagsOf(SubjectListItem item)
        => [.. (item.Tags ?? "").Split(',').Select(t => t.Trim()).Where(t => t.Length > 0)];

    /// <summary>What the filters offer for the type's items, each value with how many items have it.</summary>
    public static BrowseOptionsRead Options(IReadOnlyList<SubjectListItem> typed, string type)
    {
        var statusCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var areaCounts = new Dictionary<string, (string Label, int Count)>(StringComparer.Ordinal);
        var tagCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in typed)
        {
            var status = StatusOf(item);
            if (status.Length > 0)
            {
                statusCounts[status] = statusCounts.GetValueOrDefault(status) + 1;
            }

            var key = string.IsNullOrEmpty(item.Area) ? NoArea : item.Area;
            var (label, count) = areaCounts.GetValueOrDefault(key,
                (string.IsNullOrEmpty(item.Area) ? "No area" : (string.IsNullOrEmpty(item.AreaName) ? item.Area : item.AreaName), 0));
            areaCounts[key] = (label, count + 1);
            foreach (var tag in TagsOf(item))
            {
                tagCounts[tag] = tagCounts.GetValueOrDefault(tag) + 1;
            }
        }

        var vocabulary = type == All ? [] : StatusVocabulary.For(type);
        var statuses = vocabulary
            .Concat(statusCounts.Keys.Where(s => !vocabulary.Contains(s)).Order(Words))
            .Select(s => new BrowseFilterOption { Value = s, Label = s, Count = statusCounts.GetValueOrDefault(s) })
            .ToList();

        // Worth offering only when it tells items apart: every item without an Area offers nothing.
        var areas = areaCounts.Count == 1 && areaCounts.ContainsKey(NoArea)
            ? []
            : areaCounts
                .OrderBy(a => a.Key == NoArea ? 1 : 0)
                .ThenBy(a => a.Value.Label, Words)
                .Select(a => new BrowseFilterOption { Value = a.Key, Label = a.Value.Label, Count = a.Value.Count })
                .ToList();
        var tags = tagCounts
            .OrderBy(t => t.Key, Words)
            .Select(t => new BrowseFilterOption { Value = t.Key, Label = t.Key, Count = t.Value })
            .ToList();
        return new BrowseOptionsRead { Statuses = statuses, Areas = areas, Tags = tags };
    }

    /// <summary>Whether an item passes every category (AND), any picked value within one (OR).</summary>
    public static bool Matches(SubjectListItem item, BrowseQuery filters, string today, DateTime? enteredSince)
    {
        var words = filters.Text.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 0)
        {
            var hay = $"{item.Title} {item.Urn}".ToLowerInvariant();
            if (!words.All(w => hay.Contains(w, StringComparison.Ordinal)))
            {
                return false;
            }
        }

        if (filters.Statuses.Count > 0 && !filters.Statuses.Contains(StatusOf(item)))
        {
            return false;
        }

        if (filters.Areas.Count > 0 && !filters.Areas.Contains(string.IsNullOrEmpty(item.Area) ? NoArea : item.Area))
        {
            return false;
        }

        if (filters.Tags.Count > 0 && !filters.Tags.Any(TagsOf(item).Contains))
        {
            return false;
        }

        var due = string.IsNullOrEmpty(item.Due) ? null : item.Due;
        var passesDue = filters.Due switch
        {
            "overdue" => due is not null && !StatusVocabulary.IsTerminal(StatusOf(item)) && string.CompareOrdinal(due, today) < 0,
            "today" => due == today,
            "upcoming" => due is not null && string.CompareOrdinal(due, today) > 0,
            "none" => due is null,
            _ => true,
        };
        return passesDue && (enteredSince is not { } since || item.CreatedAt >= since);
    }

    /// <summary>
    /// Sorted by the chosen field, ties by title. Items without the field go last whichever way the
    /// sort runs. Type runs in <see cref="TypeOrder"/>, Status in the type's own order.
    /// </summary>
    public static IReadOnlyList<SubjectListItem> Sorted(IEnumerable<SubjectListItem> rows, string sort, string dir)
    {
        var sign = dir == "desc" ? -1 : 1;
        int MissingLast(string? a, string? b)
            => string.IsNullOrEmpty(a) ? (string.IsNullOrEmpty(b) ? 0 : 1)
                : string.IsNullOrEmpty(b) ? -1
                : sign * Natural(a, b);

        int Compare(SubjectListItem a, SubjectListItem b)
        {
            if (sort == "title")
            {
                return sign * Natural(a.Title, b.Title) is var t && t != 0 ? t : string.CompareOrdinal(a.Title, b.Title);
            }

            var c = sort switch
            {
                "type" => sign * (TypeRank(a) - TypeRank(b)),
                "status" => StatusCompare(a, b, sign),
                "due" => MissingLast(a.Due, b.Due),
                "entered" => sign * a.CreatedAt.CompareTo(b.CreatedAt),
                "area" => MissingLast(a.AreaName, b.AreaName),
                "tags" => MissingLast(string.Join(' ', TagsOf(a)), string.Join(' ', TagsOf(b))),
                _ => 0,
            };
            return c != 0 ? c : Natural(a.Title, b.Title) is var n && n != 0 ? n : string.CompareOrdinal(a.Title, b.Title);
        }

        var list = rows.ToList();
        list.Sort(Compare);
        return list;
    }

    /// <summary>
    /// The list's columns for the type (BROWSE-2: context-sensitive), from its items before the
    /// other filters, so they don't jump as filters change.
    /// </summary>
    public static IReadOnlyList<string> Columns(string type, IReadOnlyList<SubjectListItem> typed)
    {
        var columns = new List<string>();
        if (type == All)
        {
            columns.Add("type");
        }

        if (type == All ? typed.Any(i => StatusOf(i).Length > 0) : StatusVocabulary.For(type).Count > 0)
        {
            columns.Add("status");
        }

        if (typed.Any(i => !string.IsNullOrEmpty(i.Due)))
        {
            columns.Add("due");
        }

        if (typed.Any(i => !string.IsNullOrEmpty(i.Area)))
        {
            columns.Add("area");
        }

        if (typed.Any(i => TagsOf(i).Count > 0))
        {
            columns.Add("tags");
        }

        columns.Add("entered");
        return columns;
    }

    /// <summary>
    /// Text in the order Browse lists it: letters whatever their case, and a run of digits by its
    /// number, so "Task 9" comes before "Task 10".
    /// </summary>
    public static int Natural(string? a, string? b)
    {
        a ??= "";
        b ??= "";
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsAsciiDigit(a[i]) && char.IsAsciiDigit(b[j]))
            {
                int si = i, sj = j;
                while (i < a.Length && char.IsAsciiDigit(a[i]))
                {
                    i++;
                }

                while (j < b.Length && char.IsAsciiDigit(b[j]))
                {
                    j++;
                }

                var x = a[si..i].TrimStart('0');
                var y = b[sj..j].TrimStart('0');
                var byNumber = x.Length != y.Length ? x.Length.CompareTo(y.Length) : string.CompareOrdinal(x, y);
                if (byNumber != 0)
                {
                    return byNumber;
                }

                continue;
            }

            var p = char.ToUpperInvariant(a[i]);
            var q = char.ToUpperInvariant(b[j]);
            if (p != q)
            {
                return p.CompareTo(q);
            }

            i++;
            j++;
        }

        return (a.Length - i).CompareTo(b.Length - j);
    }

    private static readonly IComparer<string> Words = Comparer<string>.Create(
        (a, b) => string.Compare(a, b, StringComparison.OrdinalIgnoreCase) is var c && c != 0 ? c : string.CompareOrdinal(a, b));

    private static int TypeRank(SubjectListItem item)
        => TypeOrder.IndexOf(item.Type) is var i and >= 0 ? i : TypeOrder.Count;

    // In the type's own order (Open before Closed), status-less items last.
    private static int StatusCompare(SubjectListItem a, SubjectListItem b, int sign)
    {
        var (sa, sb) = (StatusOf(a), StatusOf(b));
        if (sa.Length == 0 || sb.Length == 0)
        {
            return sa.Length == 0 && sb.Length == 0 ? 0 : sa.Length == 0 ? 1 : -1;
        }

        int Rank(SubjectListItem item, string status)
            => StatusVocabulary.For(item.Type).IndexOf(status) is var i and >= 0 ? i : StatusVocabulary.For(item.Type).Count;

        var rank = Rank(a, sa) - Rank(b, sb);
        return sign * (rank != 0 ? rank : Natural(sa, sb));
    }

    private static List<string> Keep(IReadOnlyList<string> picked, IReadOnlyList<BrowseFilterOption> offered)
        => [.. picked.Where(p => offered.Any(o => o.Value == p))];

    private static int IndexOf(this IReadOnlyList<string> list, string value)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] == value)
            {
                return i;
            }
        }

        return -1;
    }
}
