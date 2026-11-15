using System.ComponentModel;
using LifeOs.Api.Read;
using LifeOs.Domain;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace LifeOs.Api.Mcp.Tools;

/// <summary>The call an agent starts with (agent-access plan §4.3).</summary>
[McpServerToolType]
public sealed class SnapshotTools(SubjectReader subjects, FocusReader focus, RewardReader rewards, McpReads reads, McpOptions options)
{
    /// <summary>The most rows each list in the snapshot carries.</summary>
    public const int SectionLimit = 25;

    [McpServerTool(Name = "life_snapshot", Title = "Life snapshot", ReadOnly = true, Idempotent = true, OpenWorld = false, Destructive = false)]
    [Description("""
        Start here. Chris's life as it stands today: his identity statements, areas, open goals
        and projects (not Completed or Abandoned; Active first, each with its status, which may
        not be set) with what each serves, tasks due today or overdue, tasks in progress, habits
        due today with streaks, appointments in the next 7 days, the inbox count, running timers,
        today's domain XP, and the top findings from LifeOS's own checks. Every list is capped;
        "truncated" names any that were cut. Follow up with get_item, list_items or alignment.
        """)]
    public async Task<CallToolResult> LifeSnapshot(CancellationToken cancellationToken)
    {
        var stamp = await reads.NowAsync(cancellationToken);
        var today = McpResults.Day(stamp.Today);
        var truncated = new List<string>();

        // Today as Focus sees it (code review CR106): what's open, the Tasks due and in progress,
        // each tracked habit's day and the Inbox count.
        var day = await focus.DayAsync(stamp.Today, options.HiddenTypes, cancellationToken);
        var all = day.Open;

        IReadOnlyList<T> Section<T>(string name, IEnumerable<T> rows)
        {
            var (kept, cut) = McpResults.Cap(rows, SectionLimit);
            if (cut)
            {
                truncated.Add(name);
            }

            return kept;
        }

        ILookup<Guid, AlignmentTarget> serves = Enumerable.Empty<AlignmentTarget>().ToLookup(t => t.FromId);
        object WithServes(SubjectListItem s) => new
        {
            s.Id, s.Urn, s.Type, s.Title, s.Status, s.TargetDate,
            Area = s.AreaName ?? s.Area,
            Serves = serves[s.Id].Select(t => new { t.Id, t.Type, t.Title }).ToList(),
        };

        // Open as list_items status=open counts it: anything not Completed or Abandoned, a goal or
        // project with no status set included. Active ones first.
        IEnumerable<SubjectListItem> Open(string type) => all
            .Where(s => s.Type == type && !StatusVocabulary.IsTerminal(s.Status))
            .OrderBy(s => FocusReader.Is(s.Status, "Active") ? 0 : 1)
            .ThenBy(s => s.TargetDate ?? "9999", StringComparer.Ordinal)
            .ThenBy(s => s.Title, StringComparer.OrdinalIgnoreCase);
        var openGoals = Open(SubjectTypes.Goal).ToList();
        var openProjects = Open(SubjectTypes.Project).ToList();
        serves = (await subjects.GetAlignmentTargetsAsync(
                [.. openGoals.Select(s => s.Id), .. openProjects.Select(s => s.Id)], AlignmentForest.Relations, cancellationToken))
            .Where(t => options.AllowsType(t.Type))
            .ToLookup(t => t.FromId);

        var due = day.DueAndOverdue;

        // The habits due today: a Building habit's day the kernel lists. An Established one is
        // never due, and shows today's check-off if it has one.
        var dueHabits = day.Building.Where(h => h.Day.Listed).ToList();
        var building = dueHabits.Select(h => new { h.Habit.Id, h.Habit.Urn, h.Habit.Name, Today = h.Day.State, h.Habit.CurrentStreak });
        var maintained = day.Maintaining
            .Select(h => new { h.Habit.Id, h.Habit.Urn, h.Habit.Name, Today = h.Day.Listed ? h.Day.State : null });

        var weekEnd = McpResults.Day(stamp.Today.AddDays(6));
        var appointments = (await subjects.GetAppointmentsBetweenAsync(today, weekEnd, cancellationToken))
            .Select(a => new { a.Id, a.Urn, a.Title, a.Date, a.StartTime, a.EndTime, a.Location, a.Status });

        var timers = (await subjects.GetTimersAsync(active: true, cancellationToken: cancellationToken))
            .Select(t => new { t.SubjectId, t.SubjectUrn, t.SubjectTitle, t.State, t.RunningSince, t.TotalSeconds });

        object? domains = null;
        if (options.Allows(McpDomains.Rewards))
        {
            domains = (await rewards.GetDomainsAsync(cancellationToken)).Domains
                .Where(d => d.Configured)
                .Select(d => new
                {
                    d.Key, d.Name, d.TotalXp,
                    Level = d.Shown?.Level,
                    RankTitle = d.Shown?.RankTitle,
                    TodayXp = d.Current?.TodayBase,
                    TodayActive = d.Current?.TodayActive,
                })
                .ToList();
        }

        var report = await reads.Diagnostics.RunAsync(cancellationToken: cancellationToken);
        var findings = report.Diagnostics
            .SelectMany(d => d.Findings.Select(f => (Diagnostic: d, Finding: f)))
            .Where(x => options.AllowsType(x.Finding.Subject.Type))
            .ToList();

        // Every unarchived value, whatever its status, as before: there are only a few.
        var values = options.AllowsType(SubjectTypes.Value)
            ? await subjects.QuerySubjectsAsync(SubjectTypes.Value, cancellationToken: cancellationToken)
            : [];

        var body = new
        {
            IdentityStatements = Section("identity_statements", values.Select(s => new { s.Id, s.Urn, s.Title })),
            Areas = Section("areas", (await subjects.GetAreasAsync(cancellationToken: cancellationToken)).Select(a => new { a.Id, a.Urn, a.Name })),
            OpenGoals = Section("open_goals", openGoals.Select(WithServes)),
            OpenProjects = Section("open_projects", openProjects.Select(WithServes)),
            DueAndOverdue = Section("due_and_overdue", due.Select(s => ItemRef.From(s) with
            {
                Overdue = string.CompareOrdinal(s.Due ?? s.Scheduled, today) < 0 ? true : null,
            })),
            InProgress = Section("in_progress", day.InProgress.Select(ItemRef.From)),
            HabitsToday = Section("habits_today", building),
            HabitsMaintained = Section("habits_maintained", maintained),
            AppointmentsNext7Days = Section("appointments_next_7_days", appointments),
            InboxCount = day.InboxCount,
            RunningTimers = Section("running_timers", timers),
            DomainXp = domains,
            Diagnostics = new
            {
                Findings = findings.Count,
                ByCheck = findings
                    .GroupBy(x => x.Diagnostic.Name)
                    .Select(g => new { Check = g.Key, g.First().Diagnostic.Title, Count = g.Count() })
                    .ToList(),
                Top = findings.Take(5).Select(x => new
                {
                    Check = x.Diagnostic.Name,
                    Item = new { x.Finding.Subject.Id, x.Finding.Subject.Type, x.Finding.Subject.Title },
                    x.Finding.Summary,
                }).ToList(),
            },
            Truncated = truncated.Count > 0 ? truncated : null,
        };

        var summary = $"Snapshot for {today}: {McpResults.Plural(openGoals.Count, "open goal")} "
                      + $"({openGoals.Count(s => FocusReader.Is(s.Status, "Active"))} Active), "
                      + $"{McpResults.Plural(due.Count, "task")} due or overdue, "
                      + $"{McpResults.Plural(dueHabits.Count, "habit")} scheduled today, "
                      + $"{McpResults.Plural(body.InboxCount, "inbox item")}.";
        return McpResults.Ok(stamp, summary, body);
    }
}
