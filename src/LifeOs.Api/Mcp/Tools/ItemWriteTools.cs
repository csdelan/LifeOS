using System.ComponentModel;
using LifeOs.Domain;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace LifeOs.Api.Mcp.Tools;

/// <summary>
/// The write tools at the <c>full</c> level (agent-access plan §5.2, step 2.3 of §5.6): they
/// change Chris's items, their status, title, fields, tags and links, and make new ones. They're
/// registered when <c>Mcp:Writes</c> is on and listed only to an agent at <c>full</c>; the
/// server's call filter refuses anyone else (§5.1). Saying the same thing again writes nothing,
/// so only <c>create_item</c> takes a request id.
/// </summary>
[McpServerToolType]
public sealed class ItemWriteTools(McpWriteAccess access, AgentItemWrites writes, McpReads reads)
{
    private const string ItemHelp = "The item's id or URN, from find_items, list_items or get_item. Not its title.";
    private const string ClearHelp = " \"\" clears it; leave out to keep it.";

    [McpServerTool(Name = "set_status", Title = "Set an item's status", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("""
        Sets an item's status, one of its type's own (lifeos://guide lists them), as Chris does on
        its page: start, complete, cancel, and so on. Completing pays what the item earns, as it
        would for Chris, and moving it off Completed takes that back. Setting the status it already
        has writes nothing. A repeating Task's series and a Review stay Chris's.
        """)]
    public async Task<CallToolResult> SetStatus(
        [Description(ItemHelp)] string item,
        [Description("The new status, such as In progress or Completed, in any case.")] string status,
        CancellationToken cancellationToken = default)
    {
        var agent = await access.CurrentAsync(cancellationToken);
        var result = await writes.SetStatusAsync(agent, item, status, cancellationToken);
        var stamp = await reads.NowAsync(cancellationToken);
        var it = result.Item;
        return McpResults.Ok(stamp,
            result.Unchanged
                ? $"{it.Type} '{it.Title}' is already {result.To}; nothing was written."
                : $"Set {it.Type} '{it.Title}' from {result.From ?? "no status"} to {result.To}.",
            new
            {
                result.Item,
                result.From,
                result.To,
                Unchanged = result.Unchanged ? true : (bool?)null,
                Earned = result.Earned.Count > 0 ? result.Earned : null,
                Repetitions = result.Repetitions.Count > 0 ? result.Repetitions : null,
            });
    }

    [McpServerTool(Name = "update_item", Title = "Update an item", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("""
        Renames an item or sets its fields, as Chris does on its page. Send only what changes; a
        field left out stays as it is, and "" clears it. Each type has its own fields: a Task has
        description, due, scheduled, priority, estimated_duration and area; a Goal or Project
        description, target_date and area; most other types description and area. The answer
        says each change from and to; a value it already has writes nothing.
        """)]
    public async Task<CallToolResult> UpdateItem(
        [Description(ItemHelp)] string item,
        [Description("A new title. Leave out to keep it.")] string? title = null,
        [Description("The item's description, at most 20,000 characters." + ClearHelp)] string? description = null,
        [Description("A Task's or Commitment's due date, yyyy-MM-dd: a real deadline." + ClearHelp)] string? due = null,
        [Description("A Task's scheduled (do) date, yyyy-MM-dd: when Chris means to work on it." + ClearHelp)] string? scheduled = null,
        [Description("A Task's priority: Low, Medium or High." + ClearHelp)] string? priority = null,
        [Description("A Task's estimated duration, such as 45m or 1h 30m." + ClearHelp)] string? estimated_duration = null,
        [Description("The id or URN of the Area the item belongs to." + ClearHelp)] string? area = null,
        [Description("A Goal's or Project's target date, yyyy-MM-dd. An Active Goal keeps one." + ClearHelp)] string? target_date = null,
        CancellationToken cancellationToken = default)
    {
        var agent = await access.CurrentAsync(cancellationToken);
        var result = await writes.UpdateAsync(
            agent, item, title, new AgentFields(description, due, scheduled, priority, estimated_duration, area, target_date),
            cancellationToken);
        var stamp = await reads.NowAsync(cancellationToken);
        var it = result.Item;
        return McpResults.Ok(stamp,
            result.Changes.Count == 0
                ? $"{it.Type} '{it.Title}' already reads so; nothing was written."
                : $"Changed {string.Join(", ", result.Changes.Select(c => c.Field))} on {it.Type} '{it.Title}'.",
            new { result.Item, result.Changes });
    }

    [McpServerTool(Name = "create_item", Title = "Create an item", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("""
        Makes a new Task, Project, Problem, Idea or Decision, optionally under a parent: a Task
        serving a Project, a Project resulting in a Goal, and so on. A Problem or Idea also waits
        in Chris's Inbox for him to triage. A Problem titled like one already there is that one.
        It takes the same fields as update_item.
        """)]
    public async Task<CallToolResult> CreateItem(
        [Description("Task, Project, Problem, Idea or Decision.")] string type,
        [Description("The new item's title.")] string title,
        [Description("The id or URN of the item it's made under. Optional.")] string? parent = null,
        [Description("How it links to parent: serves, results_in or supersedes. Leave out for its type's usual link.")] string? relation = null,
        [Description("Its description, at most 20,000 characters.")] string? description = null,
        [Description("A Task's due date, yyyy-MM-dd.")] string? due = null,
        [Description("A Task's scheduled (do) date, yyyy-MM-dd.")] string? scheduled = null,
        [Description("A Task's priority: Low, Medium or High.")] string? priority = null,
        [Description("A Task's estimated duration, such as 45m or 1h 30m.")] string? estimated_duration = null,
        [Description("The id or URN of the Area it belongs to.")] string? area = null,
        [Description("A Project's target date, yyyy-MM-dd.")] string? target_date = null,
        [Description("A new UUID for this item. If you retry, send the same one: LifeOS makes it once.")] string? request_id = null,
        CancellationToken cancellationToken = default)
    {
        var agent = await access.CurrentAsync(cancellationToken);
        var result = await writes.CreateAsync(
            agent, type, title, parent, relation,
            new AgentFields(description, due, scheduled, priority, estimated_duration, area, target_date),
            request_id, cancellationToken);
        var stamp = await reads.NowAsync(cancellationToken);
        var it = result.Item;
        var inbox = it.Type is SubjectTypes.Problem or SubjectTypes.Idea ? " and put it in Chris's Inbox" : "";
        var under = result.Link is { } link ? $", which {Verb(link.Relation)} '{link.To.Title}'," : "";
        return McpResults.Ok(stamp,
            result.AlreadyDone ? $"Already made {it.Type} '{it.Title}' with this request_id; nothing new was written."
                : result.Reused ? $"A Problem titled '{it.Title}' was already there; nothing new was made."
                : $"Made the {it.Type} '{it.Title}'{under}{inbox}.",
            new
            {
                result.Item,
                result.Link,
                Reused = result.Reused ? true : (bool?)null,
                AlreadyDone = result.AlreadyDone ? true : (bool?)null,
            });
    }

    [McpServerTool(Name = "tag", Title = "Tag an item", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Adds tags to an item, removes them, or both. A tag is lower case; one it already has, or doesn't, is left as it is.")]
    public async Task<CallToolResult> Tag(
        [Description(ItemHelp)] string item,
        [Description("Tags to add.")] string[]? add = null,
        [Description("Tags to remove.")] string[]? remove = null,
        CancellationToken cancellationToken = default)
    {
        var agent = await access.CurrentAsync(cancellationToken);
        var result = await writes.TagAsync(agent, item, add, remove, cancellationToken);
        var stamp = await reads.NowAsync(cancellationToken);
        var it = result.Item;
        return McpResults.Ok(stamp,
            result.Added + result.Removed == 0
                ? $"{it.Type} '{it.Title}' is already tagged so; nothing changed."
                : $"Tagged {it.Type} '{it.Title}': added {result.Added}, removed {result.Removed}.",
            new { result.Item, result.Add, result.Remove, result.Added, result.Removed });
    }

    [McpServerTool(Name = "link", Title = "Link two items", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("""
        Links one item to another, or removes that link: from serves to (a Task serves a Project,
        a Project or Goal serves a Goal or identity statement), from results_in to (a Project
        results in a Goal), or from supersedes to. Nothing serves a Task, and a link that would
        close a loop is refused. Linking again leaves the link as it is.
        """)]
    public async Task<CallToolResult> Link(
        [Description("The id or URN of the item the link goes from: the Task, the Project, ...")] string from,
        [Description("serves, results_in or supersedes.")] string relation,
        [Description("The id or URN of the item it goes to: the Project, the Goal, ...")] string to,
        [Description("true removes the link instead.")] bool remove = false,
        CancellationToken cancellationToken = default)
    {
        var agent = await access.CurrentAsync(cancellationToken);
        var result = await writes.LinkAsync(agent, from, relation, to, remove, cancellationToken);
        var stamp = await reads.NowAsync(cancellationToken);
        var (source, verb, target) = (result.Edge.From.Title, Verb(result.Edge.Relation), result.Edge.To.Title);
        return McpResults.Ok(stamp,
            result.Linked ? $"'{source}' {verb} '{target}'."
                : result.Removed ? $"Removed the link: '{source}' no longer {verb} '{target}'."
                : $"'{source}' didn't link to '{target}' that way; nothing changed.",
            new { result.Edge, result.Linked, result.Removed });
    }

    private static string Verb(string relation) => relation switch
    {
        SubjectRelations.ResultsIn => "results in",
        _ => relation,
    };
}
