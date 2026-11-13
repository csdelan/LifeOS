using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace LifeOs.Api.Mcp.Tools;

/// <summary>
/// The write tools at the <c>capture</c> level (agent-access plan §5.2, notes plan N7). They're
/// registered only when <c>Mcp:Writes</c> is on, listed only to an agent allowed to write, and
/// refuse anyone else. Each adds, or clears a raw capture from the Inbox; none changes anything else.
/// </summary>
[McpServerToolType]
public sealed class CaptureTools(McpWriteAccess access, AgentWrites writes, McpReads reads)
{
    private const string RequestIdHelp =
        "A new UUID for this write. If you retry it, send the same one: LifeOS writes it once.";

    [McpServerTool(Name = "capture", Title = "Capture to the Inbox", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("""
        Adds a capture to Chris's LifeOS Inbox, for him to triage like anything he captures himself.
        A note stays a capture. An idea or a problem becomes an Idea or Problem item now, titled by
        the text's first line, and that item waits in the Inbox. It changes nothing already there.
        """)]
    public async Task<CallToolResult> Capture(
        [Description("What to capture, at most 20,000 characters. Its first line titles an idea or problem.")] string text,
        [Description("note (the default), idea or problem.")] string? kind = null,
        [Description("The id or URN of an item this capture is about. Optional.")] string? concerning = null,
        [Description(RequestIdHelp)] string? request_id = null,
        CancellationToken cancellationToken = default)
    {
        var agent = await access.CurrentAsync(cancellationToken);
        var result = await writes.CaptureAsync(agent, text, kind, concerning, request_id, cancellationToken);
        var stamp = await reads.NowAsync(cancellationToken);
        var summary = result.AlreadyDone
            ? "Already captured with this request_id; nothing new was written."
            : result.Item is { } item
                ? $"Made the {item.Type} '{item.Title}' and put it in Chris's Inbox."
                : "Captured a note to Chris's Inbox.";
        return McpResults.Ok(stamp, summary, new
        {
            result.CaptureId,
            Kind = result.Kind?.ToLowerInvariant(),
            result.Item,
            Concerns = result.Concerns.Count > 0 ? result.Concerns : null,
            AlreadyDone = result.AlreadyDone ? true : (bool?)null,
        });
    }

    [McpServerTool(Name = "add_journal_entry", Title = "Add a journal entry", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Adds an entry to an item's journal, as Chris does on its page. It changes nothing already there.")]
    public async Task<CallToolResult> AddJournalEntry(
        [Description("The item's id or URN.")] string item,
        [Description("The entry, at most 20,000 characters.")] string text,
        [Description(RequestIdHelp)] string? request_id = null,
        CancellationToken cancellationToken = default)
    {
        var agent = await access.CurrentAsync(cancellationToken);
        var result = await writes.AddJournalEntryAsync(agent, item, text, request_id, cancellationToken);
        var stamp = await reads.NowAsync(cancellationToken);
        return McpResults.Ok(stamp,
            result.AlreadyDone
                ? $"Already in the journal of {result.Item.Type} '{result.Item.Title}' with this request_id; nothing new was written."
                : $"Added an entry to the journal of {result.Item.Type} '{result.Item.Title}'.",
            new { result.EntryId, result.Item, AlreadyDone = result.AlreadyDone ? true : (bool?)null });
    }

    [McpServerTool(Name = "dismiss", Title = "Dismiss a capture", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("""
        Clears one raw capture (a note or a voice note, item_kind event in inbox) from Chris's Inbox
        once you've dealt with it, for example filed it in his notes. The capture itself stays in
        LifeOS, and flagging it again brings it back. Ideas, Problems and other items aren't yours
        to dismiss: Chris triages them. A capture already out of the Inbox is left as it is.
        """)]
    public async Task<CallToolResult> Dismiss(
        [Description("The capture's item_id from inbox.")] string item_id,
        CancellationToken cancellationToken = default)
    {
        var agent = await access.CurrentAsync(cancellationToken);
        var result = await writes.DismissAsync(agent, item_id, cancellationToken);
        var stamp = await reads.NowAsync(cancellationToken);
        return McpResults.Ok(stamp,
            result.Dismissed ? "Dismissed the capture from Chris's Inbox." : "That capture isn't in the Inbox; nothing changed.",
            new { result.ItemId, result.Dismissed });
    }
}
