using System.ComponentModel;
using LifeOs.Api.Read;
using LifeOs.Domain;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace LifeOs.Api.Mcp.Tools;

/// <summary>Habits, the calendar, the inbox and people (agent-access plan §4.3).</summary>
[McpServerToolType]
public sealed class PlanTools(SubjectReader subjects, McpReads reads, McpOptions options)
{
    /// <summary>The longest range <c>calendar</c> answers.</summary>
    public const int MaxCalendarDays = 92;

    /// <summary>The longest capture text <c>inbox</c> passes whole.</summary>
    public const int MaxCaptureLength = 1000;

    /// <summary>The most items <c>people</c> lists under each person.</summary>
    public const int InvolvementLimit = 20;

    [McpServerTool(Name = "habits", Title = "Habits", ReadOnly = true, Idempotent = true, OpenWorld = false, Destructive = false)]
    [Description("""
        Chris's habits with each one's state on a day and the 6 days before it, its streak, and,
        while it's Building, its progress toward Established. A Planned habit isn't tracked yet, so
        it has no scheduled days. A day's state is followed, partial, missed, unrecorded
        (scheduled, nothing logged yet) or absent (not scheduled that day).
        """)]
    public async Task<CallToolResult> Habits(
        [Description("The day (yyyy-MM-dd). Default today.")] string? on = null,
        [Description("Include archived habits. Default false.")] bool include_archived = false,
        CancellationToken cancellationToken = default)
    {
        var stamp = await reads.NowAsync(cancellationToken);
        var day = McpResults.ParseDay(on, "on") ?? stamp.Today;
        var week = Enumerable.Range(0, 7).Select(i => day.AddDays(-i)).ToList();
        var states = week
            .SelectMany(d => subjects.GetHabitOccurrences(on: d))
            .ToLookup(o => o.HabitId);
        var habits = subjects.GetHabits(include_archived).Select(h => new
        {
            h.Id, h.Urn, h.Name, Status = h.Status ?? "Building", h.Cue, h.Routine, h.Reward,
            h.StartDate, h.EndDate, h.AllowsPartial,
            Recurrence = McpResults.ParseJson(h.Recurrence),
            Archived = h.Archived ? true : (bool?)null,
            h.CurrentStreak, h.LastState,
            On = states[h.Id].FirstOrDefault(o => o.OccurrenceDate == day)?.State,
            Week = states[h.Id]
                .OrderByDescending(o => o.OccurrenceDate)
                .Select(o => new { Day = McpResults.Day(o.OccurrenceDate), o.State })
                .ToList(),
            h.Establishment,
        }).ToList();
        var scheduled = habits.Count(h => h.On is not null);
        return McpResults.Ok(stamp,
            $"{McpResults.Plural(habits.Count, "habit")}; {scheduled} scheduled on {McpResults.Day(day)}.",
            new { On = McpResults.Day(day), Habits = habits });
    }

    [McpServerTool(Name = "calendar", Title = "Calendar", ReadOnly = true, Idempotent = true, OpenWorld = false, Destructive = false)]
    [Description("Appointments, and open Tasks due or scheduled, in a range of days. Overdue Tasks before the range aren't included: life_snapshot has them.")]
    public async Task<CallToolResult> Calendar(
        [Description("The first day (yyyy-MM-dd). Default today.")] string? from = null,
        [Description("The last day (yyyy-MM-dd), at most 92 days after the first. Default 6 days after the first.")] string? to = null,
        CancellationToken cancellationToken = default)
    {
        var stamp = await reads.NowAsync(cancellationToken);
        var start = McpResults.ParseDay(from, "from") ?? stamp.Today;
        var end = McpResults.ParseDay(to, "to") ?? start.AddDays(6);
        if (end < start)
        {
            throw new McpException($"to ({McpResults.Day(end)}) is before from ({McpResults.Day(start)}).");
        }

        if (end.DayNumber - start.DayNumber >= MaxCalendarDays)
        {
            throw new McpException($"That's more than {MaxCalendarDays} days: ask for a shorter range.");
        }

        var first = McpResults.Day(start);
        var last = McpResults.Day(end);
        bool InRange(string? d) => d is not null && string.CompareOrdinal(d, first) >= 0 && string.CompareOrdinal(d, last) <= 0;

        var appointments = subjects.GetAppointments()
            .Where(a => InRange(a.Date))
            .Select(a => new
            {
                a.Id, a.Urn, a.Title, a.Date, a.StartTime, a.EndTime,
                AllDay = a.AllDay is "true" ? true : (bool?)null,
                a.Location, a.MeetingLink, a.Status, a.SeriesUrn,
            })
            .ToList();
        var tasks = subjects.QuerySubjects(SubjectTypes.Task)
            .Where(s => !StatusVocabulary.IsTerminal(s.Status) && (InRange(s.Due) || InRange(s.Scheduled)))
            .OrderBy(s => InRange(s.Due) ? s.Due : s.Scheduled, StringComparer.Ordinal)
            .ThenBy(s => s.Title, StringComparer.CurrentCultureIgnoreCase)
            .Select(ItemRef.From)
            .ToList();
        return McpResults.Ok(stamp,
            $"{first} to {last}: {McpResults.Plural(appointments.Count, "appointment")}, {McpResults.Plural(tasks.Count, "task")} due or scheduled.",
            new { From = first, To = last, Appointments = appointments, Tasks = tasks });
    }

    [McpServerTool(Name = "inbox", Title = "Inbox", ReadOnly = true, Idempotent = true, OpenWorld = false, Destructive = false)]
    [Description("Captures and flagged items waiting for Chris to triage, newest first. flagged_at is when each one came into the inbox.")]
    public async Task<CallToolResult> Inbox(
        [Description("At most this many items (default 50, at most 200).")] int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var stamp = await reads.NowAsync(cancellationToken);
        var rows = subjects.GetInbox()
            .Where(i => i.SubjectType is null || options.AllowsType(i.SubjectType))
            .Select(i => new
            {
                i.ItemId, i.ItemKind,
                // The view names it triaged_at (its newest triage marker), but an item here is still
                // waiting: say when it was flagged.
                FlaggedAt = i.TriagedAt,
                Item = i.SubjectUrn is null ? null : new { Urn = i.SubjectUrn, Type = i.SubjectType, Title = i.SubjectTitle },
                i.EventKind,
                Text = i.EventContent is { Length: > MaxCaptureLength } c ? c[..MaxCaptureLength] : i.EventContent,
                Tags = i.Tags.Length > 0 ? i.Tags : null,
            });
        var (items, truncated) = McpResults.Cap(rows, McpResults.Limit(limit));
        return McpResults.Ok(stamp, $"{McpResults.Plural(items.Count, "item")} in the inbox{(truncated ? " (more not shown)" : "")}.",
            new { Items = items, Truncated = truncated });
    }

    [McpServerTool(Name = "people", Title = "People", ReadOnly = true, Idempotent = true, OpenWorld = false, Destructive = false)]
    [Description("The people in Chris's life and the AI agents he works with, each with the items they're on and their role there.")]
    public async Task<CallToolResult> People(
        [Description("human or agent. Leave out for both.")] string? kind = null,
        [Description("At most this many people (default 50, at most 200).")] int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var stamp = await reads.NowAsync(cancellationToken);
        var take = McpResults.Limit(limit);
        var (kept, truncated) = McpResults.Cap(subjects.GetPeople(kind: string.IsNullOrWhiteSpace(kind) ? null : kind.Trim()), take);
        var people = kept.Select(p => new
        {
            p.Id, p.Urn, Name = p.Title, Kind = p.PersonKind ?? "human", p.Role,
            On = subjects.GetInvolvements(p.Id)
                .Where(i => options.AllowsType(i.SubjectType))
                .Take(InvolvementLimit)
                .Select(i => new { i.SubjectId, i.SubjectType, i.SubjectTitle, i.Role })
                .ToList(),
        }).ToList();
        return McpResults.Ok(stamp, people.Count == 1 ? "1 person." : $"{people.Count} people.",
            new { People = people, Truncated = truncated });
    }
}
