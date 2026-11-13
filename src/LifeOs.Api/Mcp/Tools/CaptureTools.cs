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

    [McpServerTool(Name = "link_reflection", Title = "Link a reflection to its review", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("""
        Links a daily or weekly review in LifeOS to the reflection note that answers it in Chris's
        notes vault, once you've filed the note: the review's page then shows the summary and opens
        the note in Obsidian. Name the review by id or URN (a capture's concerns names it when Chris
        dictated from the review), or by kind and day; named by kind and day it's opened if it
        isn't yet. Linking again replaces the link, and the same note with the same summary writes
        nothing. It never completes the review: Chris does that. Write the answer's review URN into
        the note's review field.
        """)]
    public async Task<CallToolResult> LinkReflection(
        [Description("The note's path in the vault, from its top folder: '10 Reflections/2026-11-12 Daily reflection.md'.")] string path,
        [Description("The vault's name as Obsidian shows it, such as 'Chris2.0'.")] string vault,
        [Description("The note's summary field, at most 2,000 characters.")] string summary,
        [Description("The review's id or URN. Or leave out and give kind and day.")] string? review = null,
        [Description("daily or weekly, with day, when review is left out.")] string? kind = null,
        [Description("The day the review is for (yyyy-MM-dd): a daily's day, or any day of a weekly's Monday-to-Sunday week.")] string? day = null,
        [Description(RequestIdHelp)] string? request_id = null,
        CancellationToken cancellationToken = default)
    {
        var agent = await access.CurrentAsync(cancellationToken);
        var result = await writes.LinkReflectionAsync(agent, path, vault, summary, review, kind, day, request_id, cancellationToken);
        var stamp = await reads.NowAsync(cancellationToken);
        var linked = result.Review;
        return McpResults.Ok(stamp,
            result.AlreadyDone ? $"Already linked to '{linked.Title}' with this request_id; nothing new was written."
                : result.Written ? $"Linked the reflection to '{linked.Title}'."
                : $"'{linked.Title}' already links to this note with this summary; nothing changed.",
            new
            {
                Review = new
                {
                    linked.Id, linked.Urn, linked.Title, linked.Kind,
                    Period = linked.Period.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                    Status = result.Status,
                    Opened = result.Opened ? true : (bool?)null,
                },
                Reflection = linked.Reflection is { } r ? new { r.Path, r.Vault, r.Summary } : null,
                result.Written,
                AlreadyDone = result.AlreadyDone ? true : (bool?)null,
            });
    }
}
