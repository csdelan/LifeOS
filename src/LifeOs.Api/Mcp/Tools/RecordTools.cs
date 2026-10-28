using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using LifeOs.Api.Read;
using LifeOs.Domain;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace LifeOs.Api.Mcp.Tools;

/// <summary>What was recorded lately, and LifeOS's own checks (agent-access plan §4.3).</summary>
[McpServerToolType]
public sealed partial class RecordTools(McpReads reads, McpOptions options)
{
    /// <summary>How far back <c>recent_changes</c> looks when no <c>since</c> is given.</summary>
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromDays(7);

    /// <summary>The furthest back <c>recent_changes</c> looks.</summary>
    public static readonly TimeSpan MaxWindow = TimeSpan.FromDays(366);

    public static readonly IReadOnlyList<string> EventKindNames =
    [
        EventKinds.Journal, EventKinds.Note, EventKinds.Voice, EventKinds.IdeaSession, EventKinds.Observation,
        EventKinds.Activity, EventKinds.Measurement, EventKinds.Interaction, EventKinds.StateChange,
        EventKinds.ArchiveChange, EventKinds.Triage, EventKinds.Ledger,
    ];

    [McpServerTool(Name = "recent_changes", Title = "Recent changes", ReadOnly = true, Idempotent = true, OpenWorld = false, Destructive = false)]
    [Description("""
        What was recorded since a time, newest first: status changes, captures, notes and journal
        entries, habit check-offs, meals, Oura days and so on, each with the item it's about and
        its text. Use it to catch up since the last conversation (the default, by: "recorded").
        For what happened in a period, such as a week review, use by: "occurred": an Oura backfill
        or a late entry is then placed on the day it happened, not the day it was recorded.
        Some records are written again when they change: Oura re-sends a day, and editing a meal,
        a fast, a space photo or a rating writes it again. Each comes back once, as its newest
        copy, with 'versions' saying how many there were. Reward ledger entries (earns and
        reversals) are left out unless types includes ledger; for coins and XP per day use the
        rewards tool's daily net instead. When there are more events than the limit, the answer
        is cut and gives 'next': call again with cursor set to it, and the same since, types and
        by, for the older ones.
        """)]
    public async Task<CallToolResult> RecentChanges(
        [Description("Since when: an ISO instant (2026-10-27T18:00:00Z), a day (2026-10-27, from its start in Chris's time zone), or a span back from now (24h, 3d, 2w, or ISO P3D). Default 7d.")] string? since = null,
        [Description("Only these event kinds: journal, note, voice, idea_session, observation, activity, measurement, interaction, state_change, archive_change, triage, ledger. Leave out for every kind but ledger.")] string[]? types = null,
        [Description("At most this many events (default 50, at most 200).")] int? limit = null,
        [Description("Which time 'since' and the order go by: recorded (when LifeOS recorded it, the default) or occurred (when it happened; use it for a week review).")] string? by = null,
        [Description("To continue a cut answer: the 'next' it gave. Leave out for the newest events.")] string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var byOccurred = (by?.Trim().ToLowerInvariant()) switch
        {
            null or "" or "recorded" => false,
            "occurred" => true,
            _ => throw new McpException($"by '{by}' isn't recorded or occurred."),
        };
        var stamp = await reads.NowAsync(cancellationToken);
        var from = await SinceAsync(since, stamp.AsOf, cancellationToken);
        var kinds = types?.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim().ToLowerInvariant()).Distinct().ToList();
        var unknown = kinds?.Where(k => !EventKindNames.Contains(k)).ToList();
        if (unknown is { Count: > 0 })
        {
            throw new McpException($"'{string.Join("', '", unknown)}' isn't an event kind. Use any of: {string.Join(", ", EventKindNames)}.");
        }

        var take = McpResults.Limit(limit);
        var before = cursor is null ? null : ReadCursor(cursor, byOccurred);
        var rows = reads.Changes.GetChanges(from, kinds, options.Excluded, take, byOccurred, before);
        var (kept, truncated) = McpResults.Cap(rows, take);
        var events = kept.Select(e => new
        {
            e.Id, e.Kind, e.Source, e.Provenance,
            OccurredAt = Utc(e.OccurredAt),
            RecordedAt = Utc(e.RecordedAt),
            Item = e.SubjectId is { } id ? new { Id = id, Urn = e.SubjectUrn, Type = e.SubjectType, Title = e.SubjectTitle } : null,
            e.Status, e.Text,
            Payload = McpResults.ParseJson(e.Payload),
            Versions = e.Versions > 1 ? e.Versions : (long?)null,
        }).ToList();
        var verb = byOccurred ? "that happened" : "recorded";
        var next = truncated ? WriteCursor(kept[^1], byOccurred) : null;
        var more = before is null ? "" : " older than the cursor";
        return McpResults.Ok(stamp,
            $"{McpResults.Plural(events.Count, "event")} {verb} since {Utc(from.UtcDateTime)}{more}{(truncated ? " (more not shown: call again with cursor set to next)" : "")}.",
            new { Since = Utc(from.UtcDateTime), By = byOccurred ? "occurred" : "recorded", Events = events, Truncated = truncated, Next = next });
    }

    /// <summary>
    /// Where a cut answer stopped, for <c>cursor</c>: which time it went by, that time in ticks and
    /// the last event's id, as <c>o:638977…:3f2a…</c>.
    /// </summary>
    private static string WriteCursor(ChangeRow last, bool byOccurred)
    {
        var at = DateTime.SpecifyKind(byOccurred ? last.OccurredAt : last.RecordedAt, DateTimeKind.Utc);
        return string.Create(CultureInfo.InvariantCulture, $"{(byOccurred ? 'o' : 'r')}:{at.Ticks}:{last.Id:N}");
    }

    private static ChangeCursor ReadCursor(string cursor, bool byOccurred)
    {
        var parts = cursor.Trim().Split(':');
        if (parts.Length != 3
            || parts[0] is not ("o" or "r")
            || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks
            || !Guid.TryParseExact(parts[2], "N", out var id))
        {
            throw new McpException($"cursor '{cursor}' isn't one this tool gave; pass the 'next' of a cut answer as it is.");
        }

        if ((parts[0] == "o") != byOccurred)
        {
            throw new McpException($"That cursor is for by: {(parts[0] == "o" ? "occurred" : "recorded")}; call again with the same by as the answer that gave it.");
        }

        return new ChangeCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id);
    }

    [McpServerTool(Name = "diagnostics", Title = "Diagnostics", ReadOnly = true, Idempotent = true, OpenWorld = false, Destructive = false)]
    [Description("""
        LifeOS's own checks of the record (bsk check): items that look stuck, orphaned,
        inconsistent or overdue for review, each with its evidence. Leave out 'only' for every check.
        """)]
    public async Task<CallToolResult> Diagnostics(
        [Description("One check's name, from a previous answer's 'check' field. Leave out for every check.")] string? only = null,
        CancellationToken cancellationToken = default)
    {
        var stamp = await reads.NowAsync(cancellationToken);
        var name = string.IsNullOrWhiteSpace(only) ? null : only.Trim();
        if (name is not null && !reads.Diagnostics.DiagnosticNames().Contains(name, StringComparer.Ordinal))
        {
            throw new McpException($"'{only}' isn't a check. Use any of: {string.Join(", ", reads.Diagnostics.DiagnosticNames())}.");
        }

        var report = await reads.Diagnostics.RunAsync(name, cancellationToken);
        var checks = report.Diagnostics.Select(d => new
        {
            Check = d.Name,
            d.Title,
            Findings = d.Findings
                .Where(f => options.AllowsType(f.Subject.Type))
                .Select(f => new { Item = f.Subject, f.Summary, f.Evidence })
                .ToList(),
        }).ToList();
        var count = checks.Sum(c => c.Findings.Count);
        return McpResults.Ok(stamp, $"{McpResults.Plural(count, "finding")} from {McpResults.Plural(checks.Count, "check")}.",
            new { Checks = checks });
    }

    /// <summary>The instant <paramref name="since"/> names, back from <paramref name="now"/>.</summary>
    public async Task<DateTimeOffset> SinceAsync(string? since, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var text = since?.Trim();
        DateTimeOffset from;
        if (string.IsNullOrEmpty(text))
        {
            from = now - DefaultWindow;
        }
        else if (Span().Match(text) is { Success: true } span)
        {
            var n = int.Parse(span.Groups[1].Value, CultureInfo.InvariantCulture);
            from = now - (span.Groups[2].Value.ToLowerInvariant() switch
            {
                "m" => TimeSpan.FromMinutes(n),
                "h" => TimeSpan.FromHours(n),
                "d" => TimeSpan.FromDays(n),
                _ => TimeSpan.FromDays(7 * n),
            });
        }
        else if (text.StartsWith('P') || text.StartsWith('p'))
        {
            try
            {
                from = now - XmlConvert.ToTimeSpan(text.ToUpperInvariant());
            }
            catch (FormatException)
            {
                throw Unreadable(since!);
            }
        }
        else if (McpResultsDay(text) is { } day)
        {
            from = await reads.StartOfDayAsync(day, cancellationToken);
        }
        else if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var instant))
        {
            from = instant;
        }
        else
        {
            throw Unreadable(since!);
        }

        return from < now - MaxWindow ? now - MaxWindow : from;
    }

    private static DateOnly? McpResultsDay(string text)
        => DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;

    private static McpException Unreadable(string since)
        => new($"since '{since}' isn't a time: give an ISO instant (2026-10-27T18:00:00Z), a day (2026-10-27), or a span such as 24h, 3d or 2w.");

    private static string Utc(DateTime value)
        => DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^(\d{1,4})\s*([mhdw])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Span();
}
