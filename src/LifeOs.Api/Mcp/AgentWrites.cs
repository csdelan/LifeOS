using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using LifeOs.Api.Auth;
using LifeOs.Api.Http;
using LifeOs.Api.Mcp.Tools;
using LifeOs.Api.Read;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Capture;
using LifeOs.Application.Reviews;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using ModelContextProtocol;
using Npgsql;

namespace LifeOs.Api.Mcp;

/// <summary>An agent allowed to write, as one call sees it: its client and the AI-agent Person it acts as.</summary>
public sealed record McpAgent(string ClientId, Guid PersonId, string PersonName)
{
    /// <summary>The source its events carry: <c>mcp:&lt;client_id&gt;</c> (agent-access plan §5.3).</summary>
    public string SourceId => KernelSources.Agent(ClientId);
}

/// <summary>
/// Who may write through <c>/mcp</c> (agent-access plan §5.1): writes are on, the client is
/// named in <c>Mcp:WriteClients</c>, it's on LifeOS's list, and it acts as an AI-agent Person in
/// use. Anyone else doesn't see the write tools, and a call to one is refused.
/// </summary>
public sealed class McpWriteAccess(McpOptions options, IHttpContextAccessor http, AgentAccessStore? access)
{
    /// <summary>The write tools at the <c>capture</c> level (§5.2, notes plans N7 and N8).</summary>
    public static readonly IReadOnlySet<string> Tools =
        new HashSet<string>(StringComparer.Ordinal) { "capture", "add_journal_entry", "dismiss", "link_reflection" };

    public const string Refusal =
        "This agent can't write to LifeOS. Chris allows an agent to write in LifeOS's settings: it's named in "
        + "Mcp:WriteClients and acts as an AI-agent Person on the Connected agents page.";

    /// <summary>The agent <paramref name="user"/> signed in as, when it may write; null otherwise.</summary>
    public async Task<McpAgent?> WriterAsync(ClaimsPrincipal? user, CancellationToken cancellationToken)
        => (await CheckAsync(user, cancellationToken)).Agent;

    /// <summary>
    /// Whether <paramref name="user"/> may write, and if not, why, in words for the log: the
    /// server logs it when it hides the write tools, so a missing tool can be traced to its setting.
    /// </summary>
    public async Task<McpWriteCheck> CheckAsync(ClaimsPrincipal? user, CancellationToken cancellationToken)
    {
        var clientId = user is null ? null : AgentClaims.ClientId(user);
        if (clientId is null)
        {
            return new McpWriteCheck(null, "the token names no agent client");
        }

        if (!options.WritesOn)
        {
            return new McpWriteCheck(null, $"{McpOptions.WritesKey} is off");
        }

        if (!options.NamesWriter(clientId))
        {
            return new McpWriteCheck(null, $"{McpOptions.WriteClientsKey} doesn't name it");
        }

        if (access is null)
        {
            return new McpWriteCheck(null, "this host has no list of allowed agents");
        }

        var person = await access.WriterAsync(clientId, cancellationToken);
        return person is null
            ? new McpWriteCheck(null, "it acts as no AI-agent Person in use (Connected agents)")
            : new McpWriteCheck(new McpAgent(clientId, person.Id, person.Name), null);
    }

    /// <summary>The agent making this call, or a refusal it can read.</summary>
    public async Task<McpAgent> CurrentAsync(CancellationToken cancellationToken)
        => await WriterAsync(http.HttpContext?.User, cancellationToken) ?? throw new McpException(Refusal);
}

/// <summary>Whether a client may write: the agent when it may, else why not.</summary>
public sealed record McpWriteCheck(McpAgent? Agent, string? Why);

/// <summary>
/// The writes an agent can make at the <c>capture</c> level (agent-access plan §5.2, notes plan
/// N7), through the same Application services the API's endpoints use, built for each agent so
/// every event carries <c>mcp:&lt;client_id&gt;</c> and provenance <c>declared</c> (§5.3). A
/// capture, journal entry or reflection link also records the agent's Person in its payload.
/// Nothing here changes or removes what's already there, except that <see cref="DismissAsync"/>
/// clears a raw capture from the Inbox, which flagging it again undoes, and a newer reflection
/// link replaces the older one (both stay in the log).
/// </summary>
public sealed class AgentWrites(
    IEventStore events,
    IArtifactStore artifacts,
    IEventReader eventReader,
    ISubjectRepository subjectRepository,
    ISubjectEventRepository edges,
    IReviewRepository reviews,
    StatusService status,
    IHomeCalendar calendar,
    IClock clock,
    SubjectReader reader,
    McpReads reads,
    McpOptions options)
{
    /// <summary>The longest request id kept as an event's external id.</summary>
    public const int MaxRequestIdLength = 200;

    /// <summary>The payload field that names the agent's Person (§5.3).</summary>
    public const string PersonField = "agent_person_id";

    /// <summary>
    /// Puts <paramref name="text"/> in Chris's Inbox as a typed capture does (D4): a note is the
    /// capture event, flagged; an idea or a problem is made now from it and flagged.
    /// </summary>
    public async Task<AgentCapture> CaptureAsync(
        McpAgent agent, string text, string? kind, string? concerning, string? requestId,
        CancellationToken cancellationToken)
    {
        var body = Body(text);
        if (!CaptureEndpoints.TryKind(kind, out var type))
        {
            throw new McpException($"kind is note, idea or problem, not '{kind}'.");
        }

        var about = string.IsNullOrWhiteSpace(concerning) ? null : Find(concerning, "concerning");
        var key = RequestKey(requestId);
        if (key is not null && await EarlierAsync(agent, key, EventKinds.Note, cancellationToken) is { } earlier)
        {
            return await FinishCaptureAsync(agent, earlier, type, body, about);
        }

        var kernel = Kernel(agent);
        CaptureResult captured;
        try
        {
            captured = await kernel.Capture.CaptureFromAsync(EventKinds.Note, body, key, Payload(agent), cancellationToken);
        }
        catch (PostgresException e) when (key is not null && e.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // The same request, retried while the first was still writing.
            var first = await EarlierAsync(agent, key, EventKinds.Note, cancellationToken) ?? throw new McpException(Busy);
            return await FinishCaptureAsync(agent, first, type, body, about);
        }

        // The capture is written, so filing it runs to the end even if the agent hangs up:
        // a capture that is never flagged never reaches the Inbox.
        var made = await CaptureEndpoints.FileAsync(
            type, captured.EventId, deduplicated: false, CaptureEndpoints.FirstLine(body) ?? "Capture", body,
            kernel.Filing, CancellationToken.None);
        if (about is not null)
        {
            await kernel.Relate.RelateAsync(captured.EventId, about.Urn, SubjectEventRelations.Concerns, CancellationToken.None);
        }

        var item = made is null ? null : new McpItemRef(made.Id, made.Urn, made.Type, made.Title);
        return new AgentCapture(captured.EventId, type, item, about is null ? [] : [about], AlreadyDone: false);
    }

    /// <summary>
    /// A retried capture: does whatever the first call stopped before (flagging the note, making
    /// the idea or problem, relating the item it concerns), then answers as the first call would
    /// have. A capture flagged once and since cleared by Chris stays cleared.
    /// </summary>
    private async Task<AgentCapture> FinishCaptureAsync(
        McpAgent agent, McpEarlierWrite earlier, string type, string body, McpItemRef? about)
    {
        var kernel = Kernel(agent);
        var concerns = earlier.Concerns.ToList();
        McpItemRef? item = null;
        if (type == CaptureEndpoints.Note)
        {
            if (!await reads.EverTriagedAsync(earlier.EventId, CancellationToken.None))
            {
                await kernel.Triage.FlagItemAsync(isEvent: true, earlier.EventId, CancellationToken.None);
            }
        }
        else
        {
            item = concerns.FirstOrDefault(c => c.Type == type && c.Id != about?.Id);
            if (item is null)
            {
                var made = await CaptureEndpoints.FileAsync(
                    type, earlier.EventId, deduplicated: false, CaptureEndpoints.FirstLine(body) ?? "Capture", body,
                    kernel.Filing, CancellationToken.None);
                item = new McpItemRef(made!.Id, made.Urn, made.Type, made.Title);
            }
            else if (!await reads.EverTriagedAsync(item.Id, CancellationToken.None))
            {
                await kernel.Triage.FlagItemAsync(isEvent: false, item.Id, CancellationToken.None);
            }
        }

        if (about is not null && concerns.All(c => c.Id != about.Id))
        {
            await kernel.Relate.RelateAsync(earlier.EventId, about.Urn, SubjectEventRelations.Concerns, CancellationToken.None);
            concerns.Add(about);
        }

        return new AgentCapture(
            earlier.EventId, type, item, concerns.Where(c => c.Id != item?.Id).ToList(), AlreadyDone: true);
    }

    /// <summary>Adds an entry to an item's journal (JOURNAL-1), as <c>POST /api/journal</c> does.</summary>
    public async Task<AgentJournalEntry> AddJournalEntryAsync(
        McpAgent agent, string item, string text, string? requestId, CancellationToken cancellationToken)
    {
        var body = Body(text);
        var subject = Find(item, "item");
        var key = RequestKey(requestId);
        var kernel = Kernel(agent);
        var earlier = key is null ? null : await EarlierAsync(agent, key, EventKinds.Journal, cancellationToken);
        if (earlier is null)
        {
            try
            {
                var captured = await kernel.Capture.CaptureFromAsync(EventKinds.Journal, body, key, Payload(agent), cancellationToken);
                await kernel.Relate.RelateAsync(captured.EventId, subject.Urn, SubjectEventRelations.Concerns, cancellationToken);
                return new AgentJournalEntry(captured.EventId, subject, AlreadyDone: false);
            }
            catch (PostgresException e) when (key is not null && e.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                earlier = await EarlierAsync(agent, key, EventKinds.Journal, cancellationToken) ?? throw new McpException(Busy);
            }
        }

        if (earlier.Concerns.Count == 0)
        {
            // The first call wrote the entry and stopped before it reached the item: finish it.
            await kernel.Relate.RelateAsync(earlier.EventId, subject.Urn, SubjectEventRelations.Concerns, cancellationToken);
            return new AgentJournalEntry(earlier.EventId, subject, AlreadyDone: true);
        }

        return new AgentJournalEntry(earlier.EventId, earlier.Concerns[0], AlreadyDone: true);
    }

    /// <summary>
    /// Clears a raw capture (a note or a voice note) from the Inbox, attention-only: the capture
    /// stays, and flagging it again brings it back. An Idea, a Problem or any other item isn't an
    /// agent's to dismiss. One that has already left the Inbox is left alone.
    /// </summary>
    public async Task<AgentDismissal> DismissAsync(McpAgent agent, string itemId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse((itemId ?? "").Trim(), out var id))
        {
            throw new McpException($"item_id is a capture's item_id from inbox, a GUID; '{itemId}' isn't one.");
        }

        var waiting = reader.GetInbox().FirstOrDefault(i => i.ItemId == id);
        if (waiting is null)
        {
            return new AgentDismissal(id, Dismissed: false);
        }

        if (waiting.ItemKind != "event" || waiting.EventKind is not (EventKinds.Note or EventKinds.Voice))
        {
            var what = waiting.SubjectType is { } type ? $"{Article(type)} {type}" : $"{Article(waiting.EventKind ?? "")} {waiting.EventKind} capture";
            throw new McpException(
                $"dismiss clears only a raw capture, a note or a voice note. {id} is {what}, which Chris triages himself in LifeOS.");
        }

        await Kernel(agent).Triage.SetStateAsync(isEvent: true, id, TriageStates.Dismissed, cancellationToken);
        return new AgentDismissal(id, Dismissed: true);
    }

    /// <summary>
    /// Links a review to the vault note that answers it (reviews plan §4.1, notes plan N8). The
    /// review is named by id or URN, or by kind and day, and then opened if it isn't yet. A link
    /// that fails its checks opens nothing.
    /// </summary>
    public async Task<AgentReflection> LinkReflectionAsync(
        McpAgent agent, string path, string vault, string summary, string? review, string? kind, string? day,
        string? requestId, CancellationToken cancellationToken)
    {
        // Checked first, so a bad link doesn't open a review.
        ReviewReflections.Check(path, vault, summary, clock.UtcNow);
        var key = RequestKey(requestId);
        var kernel = Kernel(agent);
        var (target, opened) = await ReviewForAsync(kernel, review, kind, day, cancellationToken);
        var today = await calendar.DateAtAsync(clock.UtcNow, cancellationToken);

        var earlier = key is null ? null : await EarlierAsync(agent, key, EventKinds.Observation, cancellationToken);
        if (earlier is null)
        {
            try
            {
                var linked = await kernel.Reflections.LinkAsync(
                    target.Id, path, vault, summary, key, Person(agent), cancellationToken);
                return new AgentReflection(linked.Review, linked.Review.StatusAsOf(today), opened, linked.Written, AlreadyDone: false);
            }
            catch (PostgresException e) when (key is not null && e.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                earlier = await EarlierAsync(agent, key, EventKinds.Observation, cancellationToken) ?? throw new McpException(Busy);
            }
        }

        if (earlier.Concerns.Count == 0)
        {
            // The first call wrote the link and stopped before it reached the review: finish it.
            await kernel.Reflections.FinishAsync(earlier.EventId, target.Id, cancellationToken);
        }
        else if (earlier.Concerns[0].Id != target.Id)
        {
            throw new McpException($"request_id '{key}' already linked a reflection to {earlier.Concerns[0].Title}. Send a new UUID for each write.");
        }

        var now = await kernel.Reflections.MustGetAsync(target.Id, cancellationToken);
        return new AgentReflection(now, now.StatusAsOf(today), opened, Written: false, AlreadyDone: true);
    }

    /// <summary>The review a link names, opened first when it's named by kind and day.</summary>
    private async Task<(Review Review, bool Opened)> ReviewForAsync(
        AgentKernel kernel, string? review, string? kind, string? day, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(review))
        {
            var item = Find(review, "review");
            if (item.Type != SubjectTypes.Review)
            {
                throw new McpException($"review: {item.Title} is {Article(item.Type)} {item.Type}, not a Review.");
            }

            return (await kernel.Reflections.MustGetAsync(item.Id, cancellationToken), false);
        }

        if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(day))
        {
            throw new McpException("Name the review: its id or URN as review, or kind (daily or weekly) and day (yyyy-MM-dd).");
        }

        if (!DateOnly.TryParseExact(day.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var on))
        {
            throw new McpException($"day is a day as yyyy-MM-dd; '{day}' isn't one.");
        }

        var result = await kernel.Reviews.OpenAsync(kind, on, cancellationToken);
        return (result.Review, result.Created);
    }

    private static JsonObject Person(McpAgent agent) => new() { [PersonField] = agent.PersonId.ToString() };

    private static string Article(string word) => word.Length > 0 && "AEIOUaeiou".Contains(word[0]) ? "an" : "a";

    private const string Busy ="That request_id is being written right now. Wait a moment, then call again with the same request_id.";

    private static string Body(string text)
    {
        var body = text?.Trim();
        if (string.IsNullOrEmpty(body))
        {
            throw new McpException("text is empty. Send what to write.");
        }

        return body.Length <= PlanTools.MaxCaptureLength
            ? body
            : throw new McpException(
                $"text is {body.Length:N0} characters; the most is {PlanTools.MaxCaptureLength:N0}. Split it into parts, each with its own request_id.");
    }

    private static string? RequestKey(string? requestId)
    {
        var key = requestId?.Trim();
        if (string.IsNullOrEmpty(key))
        {
            return null;
        }

        return key.Length <= MaxRequestIdLength
            ? key
            : throw new McpException($"request_id is at most {MaxRequestIdLength} characters. Send a new UUID.");
    }

    private static string Payload(McpAgent agent)
        => JsonSerializer.Serialize(new Dictionary<string, Guid> { [PersonField] = agent.PersonId });

    private async Task<McpEarlierWrite?> EarlierAsync(
        McpAgent agent, string key, string kind, CancellationToken cancellationToken)
    {
        var earlier = await reads.FindWriteAsync(agent.SourceId, key, cancellationToken);
        return earlier is null || earlier.Kind == kind
            ? earlier
            : throw new McpException($"request_id '{key}' was already used for a different write. Send a new UUID for each write.");
    }

    /// <summary>The item an id or URN names, as <c>get_item</c> finds it, or a refusal the agent can act on.</summary>
    private McpItemRef Find(string reference, string name)
    {
        var key = (reference ?? "").Trim();
        var detail = Guid.TryParse(key, out var guid) ? reader.GetSubject(guid)
            : key.Length > 0 ? reader.GetSubjectByUrn(key)
            : null;
        return detail is not null && options.AllowsType(detail.Type)
            ? new McpItemRef(detail.Id, detail.Urn, detail.Type, detail.Title)
            : throw new McpException($"{name}: no item has the id or URN '{reference}'. Find one with find_items or list_items.");
    }

    /// <summary>
    /// The services for one agent, writing as its source. The host's <see cref="StatusService"/>
    /// is only there for <see cref="TriageService.DropAsync"/> and for completing a review, which
    /// no write tool calls.
    /// </summary>
    private AgentKernel Kernel(McpAgent agent)
    {
        var source = agent.SourceId;
        var subjects = new SubjectService(subjectRepository, clock, source);
        var relate = new RelateService(subjects, eventReader, edges);
        var triage = new TriageService(subjects, eventReader, events, clock, source, status);
        return new AgentKernel(
            new CaptureService(events, artifacts, clock, source),
            relate,
            triage,
            new CaptureFiling(subjects, relate, triage, calendar, clock),
            new ReviewService(subjects, subjectRepository, reviews, status, calendar, clock),
            new ReflectionService(events, edges, reviews, clock, source));
    }

    private sealed record AgentKernel(
        CaptureService Capture, RelateService Relate, TriageService Triage, CaptureFiling Filing,
        ReviewService Reviews, ReflectionService Reflections);
}

/// <summary>
/// What <c>capture</c> wrote: the capture event, its kind (Note, Idea or Problem), the Idea or
/// Problem made from it, and the items it concerns. A retried request answers with the capture
/// it wrote the first time, and <paramref name="AlreadyDone"/>.
/// </summary>
public sealed record AgentCapture(
    Guid CaptureId, string? Kind, McpItemRef? Item, IReadOnlyList<McpItemRef> Concerns, bool AlreadyDone);

/// <summary>What <c>add_journal_entry</c> wrote, and on which item.</summary>
public sealed record AgentJournalEntry(Guid EntryId, McpItemRef Item, bool AlreadyDone);

/// <summary>Whether <c>dismiss</c> cleared the capture, or found it already out of the Inbox.</summary>
public sealed record AgentDismissal(Guid ItemId, bool Dismissed);

/// <summary>
/// What <c>link_reflection</c> did: the review as it reads now and its status today, whether the
/// link opened it, whether a link was written, and whether this request_id had written it before.
/// </summary>
public sealed record AgentReflection(Review Review, string Status, bool Opened, bool Written, bool AlreadyDone);
