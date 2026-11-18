using System.Globalization;
using System.Text.Json.Nodes;
using LifeOs.Api.Http;
using LifeOs.Api.Mcp.Tools;
using LifeOs.Api.Read;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure.DependencyInjection;
using ModelContextProtocol;

namespace LifeOs.Api.Mcp;

/// <summary>
/// The writes an agent can make at the <c>full</c> level (agent-access plan §5.2, step 2.3 of
/// §5.6): it changes Chris's items as he would, through the services the API's endpoints call,
/// from a kernel of the agent's own (<see cref="SourceKernels"/>), so each event it writes
/// carries <c>mcp:&lt;client_id&gt;</c> and provenance <c>declared</c> (§5.3), and a status
/// change pays as Chris's does (decision 6).
/// <para>
/// An item is named by id or URN, never by title, so a write can't land on an item that only
/// shares a name, and an item of a switched-off area isn't found. Two things stay Chris's: a
/// repeating Task's pattern (its series is stopped, restarted and dated by him), and a Review,
/// which he completes. A status, a title, a field, a tag or a link set to what it already is
/// writes nothing, so none of these takes a request id; a new item does (§5.4).
/// </para>
/// </summary>
public sealed class AgentItemWrites(SourceKernels kernels, SubjectReader reader, McpOptions options)
{
    /// <summary>The types <c>create_item</c> makes: the items an agent tracks work and questions with.</summary>
    public static readonly IReadOnlyList<string> CreatableTypes =
        [SubjectTypes.Task, SubjectTypes.Project, SubjectTypes.Problem, SubjectTypes.Idea, SubjectTypes.Decision];

    /// <summary>The longest estimated duration kept, which is free text such as <c>45m</c> or <c>1h 30m</c>.</summary>
    public const int MaxEstimateLength = 40;

    /// <summary>
    /// The fields an agent sets, and the types whose form has each (<c>web/src/lib/subject-forms.ts</c>),
    /// so an agent can't store a key the item's page never shows. Anything else (a repeat pattern,
    /// a bounty, a Person on a Commitment, ...) is Chris's.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> FieldTypes =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            [AgentFieldKeys.Description] = Set(
                SubjectTypes.Goal, SubjectTypes.Project, SubjectTypes.Task, SubjectTypes.Problem, SubjectTypes.Decision,
                SubjectTypes.Idea, SubjectTypes.Person, SubjectTypes.Area, SubjectTypes.Commitment, SubjectTypes.Constraint),
            [AgentFieldKeys.Due] = Set(SubjectTypes.Task, SubjectTypes.Commitment),
            [AgentFieldKeys.Scheduled] = Set(SubjectTypes.Task),
            [AgentFieldKeys.Priority] = Set(SubjectTypes.Task),
            [AgentFieldKeys.EstimatedDuration] = Set(SubjectTypes.Task),
            [AgentFieldKeys.TargetDate] = Set(SubjectTypes.Goal, SubjectTypes.Project),
            [AgentFieldKeys.Area] = Set(
                SubjectTypes.Goal, SubjectTypes.Project, SubjectTypes.Task, SubjectTypes.Value, SubjectTypes.Problem,
                SubjectTypes.Decision, SubjectTypes.Idea, SubjectTypes.Person, SubjectTypes.Appointment, SubjectTypes.Habit,
                SubjectTypes.Commitment, SubjectTypes.Constraint),
        };

    /// <summary>A Task's priorities, as its page offers them.</summary>
    public static readonly IReadOnlyList<string> Priorities = ["Low", "Medium", "High"];

    /// <summary>Moves an item's status within its type's own statuses, as <c>POST /api/status</c> does.</summary>
    public async Task<AgentStatusChange> SetStatusAsync(
        McpAgent agent, string item, string status, CancellationToken cancellationToken)
    {
        var subject = await FindAsync(item, "item", cancellationToken);
        if (subject.Repeating is { IsSeries: true })
        {
            throw new McpException(
                $"'{subject.Title}' is a repeating Task, and whether it repeats is Chris's to change. "
                + "Set the status of one of its repetitions instead: get_item on it lists them.");
        }

        var changed = await Service<StatusService>(agent).ChangeStatusAsync(subject.Urn, status, cancellationToken);
        var earned = options.Allows(McpDomains.Rewards)
            ? changed.Awards.Select(a => new AgentEarning(a.Entry, a.Amounts)).ToList()
            : [];
        return new AgentStatusChange(
            AgentWrites.Ref(changed.Subject), subject.Status, changed.Status, changed.Unchanged, earned,
            changed.Repetitions.Select(AgentWrites.Ref).ToList());
    }

    /// <summary>
    /// Renames an item and sets or clears its fields (<see cref="FieldTypes"/>), as the item's page
    /// does through <c>POST /api/rename</c> and <c>/api/attributes</c>. Only what differs is written,
    /// each change an <c>attribute_change</c> event from and to; the fields go first, so a title
    /// refused after them says what did change.
    /// </summary>
    public async Task<AgentItemUpdate> UpdateAsync(
        McpAgent agent, string item, string? title, AgentFields fields, CancellationToken cancellationToken)
    {
        var subject = await FindAsync(item, "item", cancellationToken);
        var wanted = await CheckFieldsAsync(subject.Type, subject.Repeating?.IsSeries == true, subject.Title, fields, cancellationToken);
        var newTitle = string.IsNullOrWhiteSpace(title) ? null : title;
        if (newTitle is null && wanted.Count == 0)
        {
            throw new McpException("Send a new title, or at least one field to set (\"\" clears a field).");
        }

        var current = JsonNode.Parse(subject.Attributes ?? "{}") as JsonObject ?? [];
        var changes = new List<AgentFieldChange>();
        foreach (var (key, value) in wanted)
        {
            var before = current[key] is JsonValue v && v.TryGetValue<string>(out var text) && text.Length > 0 ? text : null;
            if (!string.Equals(before, value, StringComparison.Ordinal))
            {
                changes.Add(new AgentFieldChange(key, before, value));
            }
        }

        var kernel = kernels.For(agent.SourceId);
        if (changes.Count > 0)
        {
            await kernel.GetRequiredService<AttributeService>().SetAsync(
                subject.Urn, changes.Select(c => new AttributeAssignment(c.Field, c.To)).ToList(), cancellationToken);
        }

        var named = subject.Title;
        if (newTitle is not null)
        {
            try
            {
                var renamed = await kernel.GetRequiredService<RenameService>().RenameAsync(subject.Urn, newTitle, cancellationToken);
                if (renamed.Changed)
                {
                    changes.Insert(0, new AgentFieldChange(AgentFieldKeys.Title, renamed.PreviousTitle, renamed.Subject.Title));
                    named = renamed.Subject.Title;
                }
            }
            catch (Exception e) when (changes.Count > 0 && KernelRefusals.Describe(e) is { } refusal)
            {
                throw new McpException(
                    $"Set {string.Join(", ", changes.Select(c => c.Field))}, but not the title: {refusal.Message}");
            }
        }

        return new AgentItemUpdate(new McpItemRef(subject.Id, subject.Urn, subject.Type, named), changes);
    }

    /// <summary>
    /// Makes a Task, Project, Problem, Idea or Decision, under a parent when one is named, as
    /// <c>POST /api/subjects</c> does. With a request id it's made once: a retry answers the item
    /// the first call made. A Problem with the title of one already there is that one.
    /// </summary>
    public async Task<AgentCreation> CreateAsync(
        McpAgent agent, string type, string title, string? parent, string? relation, AgentFields fields,
        string? requestId, CancellationToken cancellationToken)
    {
        var made = CreatableTypes.FirstOrDefault(t => string.Equals(t, type?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new McpException($"create_item makes a {string.Join(", ", CreatableTypes.SkipLast(1))} or {CreatableTypes[^1]}, not '{type}'.");
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new McpException("title is empty. Send the new item's title.");
        }

        if (string.IsNullOrWhiteSpace(parent) && !string.IsNullOrWhiteSpace(relation))
        {
            throw new McpException("relation goes with parent: name the item the new one is made under.");
        }

        var under = string.IsNullOrWhiteSpace(parent) ? null : await FindAsync(parent, "parent", cancellationToken);
        var key = AgentWrites.RequestKey(requestId);
        var attributes = new JsonObject();
        foreach (var (field, value) in await CheckFieldsAsync(made, series: false, title, fields, cancellationToken))
        {
            if (value is not null)
            {
                attributes[field] = value;
            }
        }

        var created = await Service<SubjectCreationService>(agent).CreateAsync(
            made, title, attributes.ToJsonString(), under?.Urn, relation?.Trim().ToLowerInvariant(), key, cancellationToken);
        var link = created.Link is { } l ? new AgentEdge(AgentWrites.Ref(l.Child), l.Relation, AgentWrites.Ref(l.Parent)) : null;
        return new AgentCreation(AgentWrites.Ref(created.Subject), link, created.Reused, created.AlreadyWritten);
    }

    /// <summary>Adds and removes an item's tags, as <c>POST /api/tag</c> does.</summary>
    public async Task<AgentTagging> TagAsync(
        McpAgent agent, string item, IReadOnlyList<string>? add, IReadOnlyList<string>? remove,
        CancellationToken cancellationToken)
    {
        if ((add?.Count ?? 0) == 0 && (remove?.Count ?? 0) == 0)
        {
            throw new McpException("Send tags to add, to remove, or both.");
        }

        var subject = await FindAsync(item, "item", cancellationToken);
        var tagged = await Service<TagService>(agent).ApplyAsync(subject.Urn, add ?? [], remove ?? [], cancellationToken);
        return new AgentTagging(
            new McpItemRef(subject.Id, subject.Urn, subject.Type, subject.Title),
            tagged.Added, tagged.Removed, tagged.AddedCount, tagged.RemovedCount);
    }

    /// <summary>
    /// Links one item to another (<c>serves</c>, <c>results_in</c> or <c>supersedes</c>), or
    /// removes that link, as <c>POST /api/link</c> does. Linking again answers the link already there.
    /// </summary>
    public async Task<AgentLinking> LinkAsync(
        McpAgent agent, string from, string relation, string to, bool remove, CancellationToken cancellationToken)
    {
        var source = await FindAsync(from, "from", cancellationToken);
        var target = await FindAsync(to, "to", cancellationToken);
        var kind = (relation ?? "").Trim().ToLowerInvariant();
        if (!SubjectRelations.All.Contains(kind))
        {
            throw new McpException($"relation is {string.Join(", ", SubjectRelations.All)}; got '{relation}'.");
        }

        var relations = Service<RelationService>(agent);
        var edge = new AgentEdge(
            new McpItemRef(source.Id, source.Urn, source.Type, source.Title), kind,
            new McpItemRef(target.Id, target.Urn, target.Type, target.Title));
        if (remove)
        {
            var unlinked = await relations.UnlinkAsync(source.Urn, kind, target.Urn, cancellationToken);
            return new AgentLinking(edge, Linked: false, Removed: unlinked.Removed > 0);
        }

        await relations.LinkAsync(source.Urn, kind, target.Urn, cancellationToken);
        return new AgentLinking(edge, Linked: true, Removed: false);
    }

    /// <summary>
    /// The fields sent, checked and in the form they're stored: a day as <c>yyyy-MM-dd</c>, a
    /// priority in its page's casing, an Area as its URN; <c>""</c> clears a field (null). A field
    /// the type's page doesn't have is refused, as are a repeating Task's own dates.
    /// </summary>
    private async Task<List<KeyValuePair<string, string?>>> CheckFieldsAsync(
        string type, bool series, string title, AgentFields fields, CancellationToken cancellationToken)
    {
        var wanted = new List<KeyValuePair<string, string?>>();
        foreach (var (field, sent) in fields.Sent())
        {
            if (!FieldTypes[field].Contains(type))
            {
                var has = FieldTypes.Where(f => f.Value.Contains(type)).Select(f => f.Key).ToList();
                throw new McpException(
                    $"{(type[0] is 'A' or 'E' or 'I' or 'O' or 'U' ? "An" : "A")} {type} has no {field}. "
                    + (has.Count == 0 ? "Its fields aren't yours to set." : $"Its fields are: title, {string.Join(", ", has)}."));
            }

            if (series && field is AgentFieldKeys.Due or AgentFieldKeys.Scheduled)
            {
                throw new McpException(
                    $"'{title}' is a repeating Task: each repetition is dated by its pattern, which is Chris's. "
                    + "Set a repetition's due or scheduled date instead.");
            }

            var value = sent.Trim();
            wanted.Add(new(field, value.Length == 0 ? null : field switch
            {
                AgentFieldKeys.Due or AgentFieldKeys.Scheduled or AgentFieldKeys.TargetDate => Day(field, value),
                AgentFieldKeys.Priority => Priorities.FirstOrDefault(p => string.Equals(p, value, StringComparison.OrdinalIgnoreCase))
                    ?? throw new McpException($"priority is {string.Join(", ", Priorities)}; got '{value}'."),
                AgentFieldKeys.EstimatedDuration => value.Length <= MaxEstimateLength
                    ? value
                    : throw new McpException($"estimated_duration is a short duration such as 45m or 1h 30m, at most {MaxEstimateLength} characters."),
                AgentFieldKeys.Area => await AreaAsync(value, cancellationToken),
                _ => value.Length <= PlanTools.MaxCaptureLength
                    ? value
                    : throw new McpException($"{field} is {value.Length:N0} characters; the most is {PlanTools.MaxCaptureLength:N0}."),
            }));
        }

        return wanted;
    }

    private static string Day(string field, string value)
        => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : throw new McpException($"{field} is a day as yyyy-MM-dd; '{value}' isn't one.");

    private async Task<string> AreaAsync(string reference, CancellationToken cancellationToken)
    {
        var area = await FindAsync(reference, "area", cancellationToken);
        return area.Type == SubjectTypes.Area
            ? area.Urn
            : throw new McpException($"area: {area.Title} is {AgentWrites.Article(area.Type)} {area.Type}, not an Area. list_items with type Area finds them.");
    }

    /// <summary>
    /// The item an id or URN names, as <c>get_item</c> finds it, or a refusal the agent can act on.
    /// A Review isn't an agent's to change: Chris completes it, and <c>link_reflection</c> links it.
    /// </summary>
    private async Task<SubjectDetail> FindAsync(string reference, string name, CancellationToken cancellationToken)
    {
        var detail = await reader.FindByIdOrUrnAsync(reference, cancellationToken);
        if (detail is null || !options.AllowsType(detail.Type))
        {
            throw AgentWrites.NotFound(name, reference);
        }

        return detail.Type == SubjectTypes.Review
            ? throw new McpException(
                $"{name}: '{detail.Title}' is a Review, which is Chris's to change. link_reflection links one to its reflection note.")
            : detail;
    }

    private T Service<T>(McpAgent agent) where T : notnull => kernels.For(agent.SourceId).GetRequiredService<T>();

    private static IReadOnlySet<string> Set(params string[] types) => types.ToHashSet(StringComparer.Ordinal);
}

/// <summary>The attribute keys of the fields an agent sets, as the item's page stores them.</summary>
public static class AgentFieldKeys
{
    public const string Title = "title";
    public const string Description = "description";
    public const string Due = TaskSeries.DueKey;
    public const string Scheduled = TaskSeries.ScheduledKey;
    public const string Priority = "priority";
    public const string EstimatedDuration = "estimated_duration";
    public const string Area = "area";
    public const string TargetDate = SubjectGates.TargetDateKey;
}

/// <summary>
/// The fields <c>update_item</c> and <c>create_item</c> take, as sent: null leaves a field as it
/// is, <c>""</c> clears it.
/// </summary>
public sealed record AgentFields(
    string? Description = null, string? Due = null, string? Scheduled = null, string? Priority = null,
    string? EstimatedDuration = null, string? Area = null, string? TargetDate = null)
{
    /// <summary>The fields sent, by attribute key.</summary>
    public IEnumerable<KeyValuePair<string, string>> Sent()
    {
        (string Key, string? Value)[] all =
        [
            (AgentFieldKeys.Description, Description), (AgentFieldKeys.Due, Due), (AgentFieldKeys.Scheduled, Scheduled),
            (AgentFieldKeys.Priority, Priority), (AgentFieldKeys.EstimatedDuration, EstimatedDuration),
            (AgentFieldKeys.Area, Area), (AgentFieldKeys.TargetDate, TargetDate),
        ];
        return all.Where(f => f.Value is not null).Select(f => new KeyValuePair<string, string>(f.Key, f.Value!));
    }
}

/// <summary>
/// What <c>set_status</c> did: the item, its status before and now, whether it already had that
/// one (nothing written), what the change paid or took back (only while the rewards area is on),
/// and the repetitions a closed repetition of a repeating Task made.
/// </summary>
public sealed record AgentStatusChange(
    McpItemRef Item, string? From, string To, bool Unchanged, IReadOnlyList<AgentEarning> Earned,
    IReadOnlyList<McpItemRef> Repetitions);

/// <summary>A ledger entry a status change wrote: an earn or a reversal, and its amounts.</summary>
public sealed record AgentEarning(string Entry, IReadOnlyDictionary<string, long> Amounts);

/// <summary>What <c>update_item</c> changed, each field from and to; empty when nothing differed.</summary>
public sealed record AgentItemUpdate(McpItemRef Item, IReadOnlyList<AgentFieldChange> Changes);

/// <summary>One field <c>update_item</c> changed: null is empty.</summary>
public sealed record AgentFieldChange(string Field, string? From, string? To);

/// <summary>
/// What <c>create_item</c> made, and its link to a parent. <paramref name="Reused"/>: a Problem
/// with that title was there already. <paramref name="AlreadyDone"/>: this request id made it before.
/// </summary>
public sealed record AgentCreation(McpItemRef Item, AgentEdge? Link, bool Reused, bool AlreadyDone);

/// <summary>A link between two items: <paramref name="From"/> serves, results in or supersedes <paramref name="To"/>.</summary>
public sealed record AgentEdge(McpItemRef From, string Relation, McpItemRef To);

/// <summary>The tags <c>tag</c> was sent, normalized, and how many it added and removed.</summary>
public sealed record AgentTagging(
    McpItemRef Item, IReadOnlyList<string> Add, IReadOnlyList<string> Remove, int Added, int Removed);

/// <summary>What <c>link</c> did: linked, or removed a link that was there (false when there was none).</summary>
public sealed record AgentLinking(AgentEdge Edge, bool Linked, bool Removed);
