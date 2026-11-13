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
using LifeOs.Infrastructure.DependencyInjection;
using ModelContextProtocol;

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

        if (SettingsRefuse(options, clientId) is { } why)
        {
            return new McpWriteCheck(null, why);
        }

        if (access is null)
        {
            return new McpWriteCheck(null, "this host has no list of allowed agents");
        }

        var person = await access.WriterAsync(clientId, cancellationToken);
        return person is null
            ? new McpWriteCheck(null, NoPerson)
            : new McpWriteCheck(new McpAgent(clientId, person.Id, person.Name), null);
    }

    /// <summary>
    /// Why <paramref name="agent"/> can't write, as the Connected agents page shows it, or null when
    /// it may: the rule <see cref="CheckAsync"/> applies on each call, read from the list instead of
    /// a token (code review CR64), so the page and <c>/mcp</c> can't disagree about who writes.
    /// </summary>
    public static string? ReadOnlyBecause(McpOptions options, AgentClient agent)
        => !agent.Allowed ? "LifeOS doesn't allow it"
            : SettingsRefuse(options, agent.ClientId) ?? (agent.ActsAsAgentPerson ? null : NoPerson);

    private const string NoPerson = "it acts as no AI-agent Person in use (Connected agents)";

    private static string? SettingsRefuse(McpOptions options, string clientId)
        => !options.WritesOn ? $"{McpOptions.WritesKey} is off"
            : !options.NamesWriter(clientId) ? $"{McpOptions.WriteClientsKey} doesn't name it"
            : null;

    /// <summary>The agent making this call, or a refusal it can read.</summary>
    public async Task<McpAgent> CurrentAsync(CancellationToken cancellationToken)
        => await WriterAsync(http.HttpContext?.User, cancellationToken) ?? throw new McpException(Refusal);
}

/// <summary>Whether a client may write: the agent when it may, else why not.</summary>
public sealed record McpWriteCheck(McpAgent? Agent, string? Why);

/// <summary>
/// The writes an agent can make at the <c>capture</c> level (agent-access plan §5.2, notes plan
/// N7), through the same Application services the API's endpoints use, from a kernel of the
/// agent's own (<see cref="SourceKernels"/>), so every event carries <c>mcp:&lt;client_id&gt;</c>
/// and provenance <c>declared</c> (§5.3). A
/// capture, journal entry or reflection link also records the agent's Person in its payload.
/// Nothing here changes or removes what's already there, except that <see cref="DismissAsync"/>
/// clears a raw capture from the Inbox, which flagging it again undoes, and a newer reflection
/// link replaces the older one (both stay in the log).
/// </summary>
public sealed class AgentWrites(
    SourceKernels kernels,
    IHomeCalendar calendar,
    IClock clock,
    SubjectReader reader,
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
        if (!InboxFilingService.TryKind(kind, out var type))
        {
            throw new McpException($"kind is note, idea or problem, not '{kind}'.");
        }

        var about = string.IsNullOrWhiteSpace(concerning) ? null : await FindAsync(concerning, "concerning", cancellationToken);

        // A retried request id finishes and answers its first capture (code review CR68). Once
        // the capture is written, filing it runs to the end even if the agent hangs up.
        var captured = await Kernel(agent).Filing.CaptureAsync(
            body, type, RequestKey(requestId), Payload(agent), about?.Urn, cancellationToken);
        var item = captured.Item is { } made ? Ref(made) : null;
        return new AgentCapture(captured.EventId, type, item, captured.Concerns.Select(Ref).ToList(), captured.AlreadyWritten);
    }

    /// <summary>Adds an entry to an item's journal (JOURNAL-1), as <c>POST /api/journal</c> does.</summary>
    public async Task<AgentJournalEntry> AddJournalEntryAsync(
        McpAgent agent, string item, string text, string? requestId, CancellationToken cancellationToken)
    {
        var body = Body(text);
        var subject = await FindAsync(item, "item", cancellationToken);
        var entry = await Kernel(agent).Journal.AppendAsync(subject.Urn, body, RequestKey(requestId), Payload(agent), cancellationToken);
        return new AgentJournalEntry(entry.EventId, Ref(entry.Subject), entry.AlreadyWritten);
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

        var waiting = (await reader.GetInboxAsync(itemId: id, cancellationToken: cancellationToken)).FirstOrDefault();
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
        var today = await calendar.TodayAsync(cancellationToken);
        var linked = await kernel.Reflections.LinkAsync(target.Id, path, vault, summary, key, Person(agent), cancellationToken);
        return new AgentReflection(linked.Review, linked.Review.StatusAsOf(today), opened, linked.Written, linked.AlreadyWritten);
    }

    /// <summary>The review a link names, opened first when it's named by kind and day.</summary>
    private async Task<(Review Review, bool Opened)> ReviewForAsync(
        AgentKernel kernel, string? review, string? kind, string? day, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(review))
        {
            var item = await FindAsync(review, "review", cancellationToken);
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

    private static McpItemRef Ref(SubjectRef subject) => new(subject.Id, subject.Urn, subject.Type, subject.Title);

    /// <summary>The item an id or URN names, as <c>get_item</c> finds it, or a refusal the agent can act on.</summary>
    private async Task<McpItemRef> FindAsync(string reference, string name, CancellationToken cancellationToken)
    {
        var detail = await reader.FindByIdOrUrnAsync(reference, cancellationToken);
        return detail is not null && options.AllowsType(detail.Type)
            ? new McpItemRef(detail.Id, detail.Urn, detail.Type, detail.Title)
            : throw new McpException($"{name}: no item has the id or URN '{reference}'. Find one with find_items or list_items.");
    }

    /// <summary>The services for one agent, from its own kernel, writing as its source.</summary>
    private AgentKernel Kernel(McpAgent agent)
    {
        var kernel = kernels.For(agent.SourceId);
        return new AgentKernel(
            kernel.GetRequiredService<TriageService>(),
            kernel.GetRequiredService<InboxFilingService>(),
            kernel.GetRequiredService<JournalService>(),
            kernel.GetRequiredService<ReviewService>(),
            kernel.GetRequiredService<ReflectionService>());
    }

    private sealed record AgentKernel(
        TriageService Triage, InboxFilingService Filing, JournalService Journal,
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
