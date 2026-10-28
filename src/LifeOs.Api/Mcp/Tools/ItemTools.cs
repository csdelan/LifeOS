using System.ComponentModel;
using LifeOs.Api.Read;
using LifeOs.Domain;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace LifeOs.Api.Mcp.Tools;

/// <summary>Finding items, listing them, reading one, and how they align (agent-access plan §4.3).</summary>
[McpServerToolType]
public sealed class ItemTools(SubjectReader subjects, McpReads reads, McpOptions options)
{
    /// <summary>The journal entries, status changes and repetitions <c>get_item</c> brings, newest first.</summary>
    public const int HistoryLimit = 20;

    /// <summary>The longest journal entry <c>get_item</c> passes whole.</summary>
    public const int MaxEntryLength = 2000;

    /// <summary>The most nodes one <c>alignment</c> answer carries.</summary>
    public const int MaxTreeNodes = 400;

    private const string TypeHelp =
        "Item type: Value (an identity statement), Area, Goal, Project, Task, Habit, Commitment, Appointment, "
        + "Decision, Problem, Idea, Person, Constraint, Season, Reward or Space.";

    [McpServerTool(Name = "find_items", Title = "Find items", ReadOnly = true, Idempotent = true, OpenWorld = false, Destructive = false)]
    [Description("Items whose title or one of whose tags contains the words given, ignoring case. Titles that start with them come first.")]
    public async Task<CallToolResult> FindItems(
        [Description("The words to look for in titles and tags.")] string query,
        [Description(TypeHelp + " Leave out for every type.")] string? type = null,
        [Description("Include archived items. Default false.")] bool include_archived = false,
        [Description("At most this many items (default 50, at most 200).")] int? limit = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new McpException("query is empty: give a word or two from the item's title or tags.");
        }

        var stamp = await reads.NowAsync(cancellationToken);
        var take = McpResults.Limit(limit);
        var found = subjects.SearchSubjects(query, McpResults.ParseType(type), include_archived, take + 1)
            .Where(s => options.AllowsType(s.Type))
            .Select(ItemRef.From);
        var (items, truncated) = McpResults.Cap(found, take);
        return McpResults.Ok(stamp, $"{McpResults.Plural(items.Count, "item")} match '{query.Trim()}'.",
            new { Items = items, Truncated = truncated });
    }

    [McpServerTool(Name = "list_items", Title = "List items", ReadOnly = true, Idempotent = true, OpenWorld = false, Destructive = false)]
    [Description("""
        Items filtered by type, status, area, tag and due date, by title (by due date when a due
        filter is given). Archived items and a repeating Task's series are left out unless asked
        for; a repeating Task's repetitions are the Tasks to do.
        """)]
    public async Task<CallToolResult> ListItems(
        [Description(TypeHelp + " Leave out for every type.")] string? type = null,
        [Description("A status as the type spells it (e.g. Active, Not started, In progress, Completed), or 'open' for any status that isn't finished, or 'closed' for any that is.")] string? status = null,
        [Description("An area's name or URN.")] string? area = null,
        [Description("A tag the item carries.")] string? tag = null,
        [Description("Only items due on or before this day (yyyy-MM-dd).")] string? due_before = null,
        [Description("Only items due on or after this day (yyyy-MM-dd).")] string? due_after = null,
        [Description("Include archived items. Default false.")] bool include_archived = false,
        [Description("At most this many items (default 50, at most 200).")] int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var stamp = await reads.NowAsync(cancellationToken);
        var before = McpResults.ParseDay(due_before, "due_before") is { } b ? McpResults.Day(b) : null;
        var after = McpResults.ParseDay(due_after, "due_after") is { } a ? McpResults.Day(a) : null;
        var rows = subjects.QuerySubjects(McpResults.ParseType(type), include_archived)
            .Where(s => options.AllowsType(s.Type))
            .Where(s => MatchesStatus(s.Status, status))
            .Where(s => string.IsNullOrWhiteSpace(area)
                        || Same(s.Area, area) || Same(s.AreaName, area))
            .Where(s => string.IsNullOrWhiteSpace(tag) || Tags(s.Tags).Any(t => Same(t, tag)))
            .Where(s => before is null || (s.Due is { } d1 && string.CompareOrdinal(d1, before) <= 0))
            .Where(s => after is null || (s.Due is { } d2 && string.CompareOrdinal(d2, after) >= 0));
        if (before is not null || after is not null)
        {
            rows = rows.OrderBy(s => s.Due, StringComparer.Ordinal).ThenBy(s => s.Title, StringComparer.CurrentCultureIgnoreCase);
        }

        var take = McpResults.Limit(limit);
        var (items, truncated) = McpResults.Cap(rows.Select(ItemRef.From), take);
        return McpResults.Ok(stamp, $"{McpResults.Plural(items.Count, "item")}{(truncated ? " (more not shown)" : "")}.",
            new { Items = items, Truncated = truncated });
    }

    [McpServerTool(Name = "get_item", Title = "Get item", ReadOnly = true, Idempotent = true, OpenWorld = false, Destructive = false)]
    [Description("""
        One item in full: its fields and attributes, what it serves and what serves it, the
        people on it, its tags, its latest journal entries and status changes, its timer, and
        its repeating series. A Person also lists what they're involved in; a Habit its last
        14 days.
        """)]
    public async Task<CallToolResult> GetItem(
        [Description("The item's id (a GUID) or URN, from any other tool's answer.")] string id,
        CancellationToken cancellationToken = default)
    {
        var stamp = await reads.NowAsync(cancellationToken);
        var detail = Find(id);

        var journal = subjects.GetJournal(detail.Id)
            .OrderByDescending(j => j.OccurredAt)
            .Take(HistoryLimit)
            .Select(j => new
            {
                j.EventId,
                j.OccurredAt,
                Text = j.Content.Length > MaxEntryLength ? j.Content[..MaxEntryLength] : j.Content,
                Cut = j.Content.Length > MaxEntryLength ? true : (bool?)null,
            })
            .ToList();
        var history = subjects.GetStatusHistory(detail.Id)
            .OrderByDescending(h => h.OccurredAt)
            .Take(HistoryLimit)
            .Select(h => new { h.Status, h.OccurredAt })
            .ToList();

        object? series = null;
        if (detail.Repeating is { IsSeries: true } repeat && subjects.GetTaskSeriesHistory(repeat.SeriesId) is { } h)
        {
            series = new { h.Series, Repetitions = h.Repetitions.Take(HistoryLimit).ToList() };
        }

        object? habit = null;
        if (detail.Type == SubjectTypes.Habit)
        {
            var since = stamp.Today.AddDays(-13);
            habit = subjects.GetHabitOccurrences(habitId: detail.Id)
                .Where(o => o.OccurrenceDate >= since)
                .Select(o => new { Day = McpResults.Day(o.OccurrenceDate), o.State })
                .ToList();
        }

        var body = new
        {
            Item = new
            {
                detail.Id, detail.Urn, detail.Type, detail.Title, detail.Status, detail.Due,
                detail.TargetDate, detail.ExpectedCadence, detail.NextReviewAt, detail.Scope,
                detail.Statement, detail.CreatedAt,
                Archived = detail.Archived ? true : (bool?)null,
                Area = detail.AreaName ?? detail.Area,
                AreaUrn = detail.Area,
                Attributes = McpResults.ParseJson(detail.Attributes),
                detail.Repeating,
            },
            Serves = Allowed(subjects.GetServes(detail.Id)),
            ServedBy = Allowed(subjects.GetServedBy(detail.Id)),
            People = subjects.GetPeopleOn(detail.Id).Select(p => new { p.PersonId, p.PersonUrn, p.PersonName, p.Role }).ToList(),
            Tags = subjects.GetTags(detail.Id),
            Journal = journal,
            StatusHistory = history,
            Timer = detail.Type == SubjectTypes.Task ? subjects.GetTimer(detail.Id) : null,
            Series = series,
            InvolvedIn = detail.Type == SubjectTypes.Person
                ? subjects.GetInvolvements(detail.Id)
                    .Where(p => options.AllowsType(p.SubjectType))
                    .Select(p => new { p.SubjectId, p.SubjectUrn, p.SubjectType, p.SubjectTitle, p.Role })
                    .ToList()
                : null,
            HabitLast14Days = habit,
        };
        return McpResults.Ok(stamp, $"{detail.Type} '{detail.Title}'{(detail.Status is { } s ? $", {s}" : "")}.", body);
    }

    [McpServerTool(Name = "alignment", Title = "Alignment", ReadOnly = true, Idempotent = true, OpenWorld = false, Destructive = false)]
    [Description("""
        How items align: an item's children are the items that serve it (Task serves Project
        serves Goal serves identity statement). With an id: everything serving the item, down to
        Tasks ("below"), and what it serves, up to identity statements ("above"). With no id: the
        whole tree, identity statements first. Finished items are left out unless include_done.
        """)]
    public async Task<CallToolResult> Alignment(
        [Description("An item's id or URN. Leave out for the whole tree.")] string? id = null,
        [Description("Include finished items (Completed, Abandoned, Cancelled…). Default false.")] bool include_done = false,
        CancellationToken cancellationToken = default)
    {
        var stamp = await reads.NowAsync(cancellationToken);
        var all = subjects.QuerySubjects(null).Where(s => options.AllowsType(s.Type));
        var forest = new AlignmentForest(all, subjects.GetAlignmentEdges(), include_done, MaxTreeNodes);

        if (string.IsNullOrWhiteSpace(id))
        {
            var roots = forest.Forest();
            return McpResults.Ok(stamp, $"The alignment tree: {McpResults.Plural(roots.Count, "root")}.",
                new { Roots = roots, Truncated = forest.Truncated });
        }

        var detail = Find(id);
        if (!forest.Contains(detail.Id))
        {
            throw new McpException($"{detail.Type} '{detail.Title}' is archived, so it isn't in the tree.");
        }

        var below = forest.Below(detail.Id);
        var above = forest.Above(detail.Id);
        return McpResults.Ok(stamp,
            $"{detail.Type} '{detail.Title}': {McpResults.Plural(below?.Children?.Count ?? 0, "item")} serve it directly; it serves {above.Count}.",
            new { Below = below, Above = above, Truncated = forest.Truncated });
    }

    /// <summary>The item an id or URN names, or a refusal an agent can act on.</summary>
    private SubjectDetail Find(string id)
    {
        var key = (id ?? "").Trim();
        var detail = Guid.TryParse(key, out var guid) ? subjects.GetSubject(guid)
            : key.Length > 0 ? subjects.GetSubjectByUrn(key)
            : null;
        return detail is not null && options.AllowsType(detail.Type)
            ? detail
            : throw new McpException($"No item has the id or URN '{id}'. Find one with find_items or list_items.");
    }

    private IReadOnlyList<RelationEdge> Allowed(IEnumerable<RelationEdge> edges)
        => edges.Where(e => options.AllowsType(e.Type)).ToList();

    private static bool MatchesStatus(string? status, string? wanted)
    {
        if (string.IsNullOrWhiteSpace(wanted))
        {
            return true;
        }

        return wanted.Trim().ToLowerInvariant() switch
        {
            "open" => !StatusVocabulary.IsTerminal(status),
            "closed" => StatusVocabulary.IsTerminal(status),
            _ => SnapshotTools.Is(status, wanted),
        };
    }

    private static bool Same(string? a, string? b)
        => a is not null && b is not null && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> Tags(string? tags)
        => (tags ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
