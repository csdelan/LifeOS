using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace LifeOs.Api.Mcp;

/// <summary>What the access log (agent-access plan §4.5) keeps of a tool call.</summary>
public static class McpCallLog
{
    /// <summary>A call's arguments as JSON, or null when it had none; the store trims it to 1 KB.</summary>
    public static string? Arguments(IDictionary<string, JsonElement>? arguments)
        => arguments is null || arguments.Count == 0 ? null : JsonSerializer.Serialize(arguments);

    /// <summary>The items in an answer's top-level lists, or null when it has no list.</summary>
    public static int? CountItems(CallToolResult result)
    {
        if (result.StructuredContent is not { ValueKind: JsonValueKind.Object } json)
        {
            return null;
        }

        int? count = null;
        foreach (var property in json.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Array)
            {
                count = (count ?? 0) + property.Value.GetArrayLength();
            }
        }

        return count;
    }

    /// <summary>Logs a call; a failure goes to the JSON log only.</summary>
    public static async Task RecordAsync(AgentAccessStore access, AgentCallRecord call, ILogger? logger)
    {
        try
        {
            // Not the call's token: a call cancelled halfway is still logged.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await access.RecordCallAsync(call, timeout.Token);
        }
        catch (Exception e)
        {
            logger?.LogError(e, "Couldn't log MCP {Tool} for {ClientId}", call.Tool, call.ClientId);
        }
    }
}
