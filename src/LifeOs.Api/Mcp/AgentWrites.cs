using System.Security.Claims;
using System.Text.Json;
using LifeOs.Api.Auth;
using LifeOs.Api.Http;
using LifeOs.Api.Mcp.Tools;
using LifeOs.Api.Read;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Capture;
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
    /// <summary>The write tools at the <c>capture</c> level (§5.2, notes plan N7).</summary>
    public static readonly IReadOnlySet<string> Tools =
        new HashSet<string>(StringComparer.Ordinal) { "capture", "add_journal_entry", "dismiss" };

    public const string Refusal =
        "This agent can't write to LifeOS. Chris allows an agent to write in LifeOS's settings: it's named in "
        + "Mcp:WriteClients and acts as an AI-agent Person on the Connected agents page.";

    /// <summary>The agent <paramref name="user"/> signed in as, when it may write; null otherwise.</summary>
    public async Task<McpAgent?> WriterAsync(ClaimsPrincipal? user, CancellationToken cancellationToken)
    {
        var clientId = user is null ? null : AgentClaims.ClientId(user);
        if (clientId is null || access is null || !options.NamesWriter(clientId))
        {
            return null;
        }

        var person = await access.WriterAsync(clientId, cancellationToken);
        return person is null ? null : new McpAgent(clientId, person.Id, person.Name);
    }

    /// <summary>The agent making this call, or a refusal it can read.</summary>
    public async Task<McpAgent> CurrentAsync(CancellationToken cancellationToken)
        => await WriterAsync(http.HttpContext?.User, cancellationToken) ?? throw new McpException(Refusal);
}

/// <summary>
/// The writes an agent can make at the <c>capture</c> level (agent-access plan §5.2, notes plan
/// N7), through the same Application services the API's endpoints use, built for each agent so
/// every event carries <c>mcp:&lt;client_id&gt;</c> and provenance <c>declared</c> (§5.3). A
/// capture or journal entry also records the agent's Person in its payload. Nothing here
/// changes or removes what's already there, except that <see cref="DismissAsync"/> clears a raw
/// capture from the Inbox, which flagging it again undoes.
/// </summary>
public sealed class AgentWrites(
    IEventStore events,
    IArtifactStore artifacts,
    IEventReader eventReader,
    ISubjectRepository subjectRepository,
    ISubjectEventRepository edges,
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
            return new AgentCapture(earlier.EventId, null, null, earlier.Concerns, AlreadyDone: true);
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
            return new AgentCapture(first.EventId, null, null, first.Concerns, AlreadyDone: true);
        }

        var made = await CaptureEndpoints.FileAsync(
            type, captured.EventId, deduplicated: false, CaptureEndpoints.FirstLine(body) ?? "Capture", body,
            kernel.Filing, cancellationToken);
        if (about is not null)
        {
            await kernel.Relate.RelateAsync(captured.EventId, about.Urn, SubjectEventRelations.Concerns, cancellationToken);
        }

        var item = made is null ? null : new McpItemRef(made.Id, made.Urn, made.Type, made.Title);
        return new AgentCapture(captured.EventId, type, item, about is null ? [] : [about], AlreadyDone: false);
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
    /// is only there for <see cref="TriageService.DropAsync"/>, which no write tool calls.
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
            new CaptureFiling(subjects, relate, triage, calendar, clock));
    }

    private sealed record AgentKernel(CaptureService Capture, RelateService Relate, TriageService Triage, CaptureFiling Filing);
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
