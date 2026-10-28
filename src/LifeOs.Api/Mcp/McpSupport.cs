using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Dapper;
using LifeOs.Api.Read;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;
using LifeOs.Infrastructure.Diagnostics;
using LifeOs.Infrastructure.Persistence;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using Npgsql;

namespace LifeOs.Api.Mcp;

/// <summary>When a tool answers: the instant, and today in the home zone (<c>Rewards:HomeTimeZone</c>).</summary>
public readonly record struct McpStamp(DateTimeOffset AsOf, DateOnly Today);

/// <summary>
/// Everything the tools read through besides the API's own readers, all on the
/// <c>bsk_reader</c> connection: no tool can write (agent-access plan §4.1). The kernel's own
/// <see cref="DiagnosticRunner"/> runs as the owner, so the tools get their own.
/// </summary>
public sealed class McpReads(string readerConnectionString, string homeTimeZone, IClock clock)
{
    private readonly NpgsqlHomeCalendar calendar = new(readerConnectionString, homeTimeZone);

    public ChangeReader Changes { get; } = new(readerConnectionString);

    public DiagnosticRunner Diagnostics { get; } = new(readerConnectionString);

    /// <summary>The home zone, an IANA name.</summary>
    public string HomeTimeZone { get; } = homeTimeZone;

    public async Task<McpStamp> NowAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        return new McpStamp(now, await calendar.DateAtAsync(now, cancellationToken));
    }

    /// <summary>
    /// The instant a home-zone day starts. Postgres does the zone arithmetic, as everywhere else
    /// in LifeOS: the API runs with invariant globalization, so .NET can't read IANA zones on
    /// every host.
    /// </summary>
    public async Task<DateTimeOffset> StartOfDayAsync(DateOnly day, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(readerConnectionString);
        await connection.OpenAsync(cancellationToken);
        var start = await connection.ExecuteScalarAsync<DateTime>(new CommandDefinition(
            "SELECT (@day::date::timestamp AT TIME ZONE @tz);",
            new { day = McpResults.Day(day), tz = HomeTimeZone },
            cancellationToken: cancellationToken));
        return new DateTimeOffset(DateTime.SpecifyKind(start, DateTimeKind.Utc));
    }
}

/// <summary>
/// A tool's answer (agent-access plan §4.3): one line of summary, then the JSON, as text for any
/// client, and the same JSON as structured content. Every answer starts with <c>as_of</c> and
/// <c>today</c>, so "today" and "overdue" mean what Chris means.
/// </summary>
public static class McpResults
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 200;

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public static CallToolResult Ok(McpStamp stamp, string summary, object body)
    {
        var json = new JsonObject
        {
            ["as_of"] = stamp.AsOf.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            ["today"] = Day(stamp.Today),
        };
        if (JsonSerializer.SerializeToNode(body, Json) is JsonObject fields)
        {
            foreach (var (key, value) in fields.ToList())
            {
                fields.Remove(key);
                json[key] = value;
            }
        }

        var text = json.ToJsonString(Json);
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = summary + "\n\n" + text }],
            StructuredContent = JsonSerializer.SerializeToElement(json, Json),
        };
    }

    /// <summary>A refused call, in words the agent can act on.</summary>
    public static CallToolResult Error(string message)
        => new() { IsError = true, Content = [new TextContentBlock { Text = message }] };

    public static int Limit(int? limit) => Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);

    /// <summary>The first <paramref name="limit"/> rows, and whether any were cut.</summary>
    public static (IReadOnlyList<T> Rows, bool Truncated) Cap<T>(IEnumerable<T> rows, int limit)
    {
        var taken = rows.Take(limit + 1).ToList();
        return taken.Count > limit ? (taken.Take(limit).ToList(), true) : (taken, false);
    }

    public static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>A <c>yyyy-MM-dd</c> argument, or null when left out; anything else is refused in words.</summary>
    public static DateOnly? ParseDay(string? text, string name)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? day
            : throw new McpException($"{name} '{text}' isn't a date: give it as yyyy-MM-dd, e.g. 2026-10-28.");
    }

    /// <summary>A subject type named as the kernel spells it, ignoring case; null when left out.</summary>
    public static string? ParseType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return null;
        }

        var wanted = type.Trim();
        if (string.Equals(wanted, "Identity statement", StringComparison.OrdinalIgnoreCase)
            || string.Equals(wanted, "IdentityStatement", StringComparison.OrdinalIgnoreCase))
        {
            return SubjectTypes.Value;
        }

        return SubjectTypeNames.FirstOrDefault(t => string.Equals(t, wanted, StringComparison.OrdinalIgnoreCase))
               ?? throw new McpException($"'{type}' isn't a type. Use one of: {string.Join(", ", SubjectTypeNames)}.");
    }

    public static readonly IReadOnlyList<string> SubjectTypeNames =
    [
        SubjectTypes.Value, SubjectTypes.Area, SubjectTypes.Goal, SubjectTypes.Project, SubjectTypes.Task,
        SubjectTypes.Habit, SubjectTypes.Commitment, SubjectTypes.Appointment, SubjectTypes.Decision,
        SubjectTypes.Problem, SubjectTypes.Idea, SubjectTypes.Person, SubjectTypes.Constraint,
        SubjectTypes.Season, SubjectTypes.Reward, SubjectTypes.Space,
    ];

    /// <summary>A JSON document stored as text, as JSON; null when there's none or it's malformed.</summary>
    public static JsonNode? ParseJson(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";
}

/// <summary>A compact list row an agent can follow up on with <c>get_item</c>.</summary>
public sealed record ItemRef(Guid Id, string Urn, string Type, string Title)
{
    public string? Status { get; init; }
    public string? Due { get; init; }
    public string? Scheduled { get; init; }
    public string? TargetDate { get; init; }
    public string? Area { get; init; }
    public string? Tags { get; init; }
    public bool? Archived { get; init; }

    /// <summary>True on a Task whose due (or do) date is before today.</summary>
    public bool? Overdue { get; init; }

    public static ItemRef From(SubjectListItem item) => new(item.Id, item.Urn, item.Type, item.Title)
    {
        Status = item.Status,
        Due = item.Due,
        Scheduled = item.Scheduled,
        TargetDate = item.TargetDate,
        Area = item.AreaName ?? item.Area,
        Tags = string.IsNullOrWhiteSpace(item.Tags) ? null : item.Tags,
        Archived = item.Archived ? true : null,
    };
}
