using System.ComponentModel;
using LifeOs.Api.Read;
using LifeOs.Domain;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace LifeOs.Api.Mcp.Tools;

/// <summary>The call an agent starts with (agent-access plan §4.3).</summary>
[McpServerToolType]
public sealed class SnapshotTools(SubjectReader subjects, RewardReader rewards, McpReads reads, McpOptions options)
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

        var all = subjects.QuerySubjects(null).Where(s => options.AllowsType(s.Type)).ToList();
        var byId = all.ToDictionary(s => s.Id);
        var serves = subjects.GetAlignmentEdges()
            .Where(e => AlignmentForest.Relations.Contains(e.Relation) && byId.ContainsKey(e.ToId))
            .ToLookup(e => e.FromId);

        IReadOnlyList<T> Section<T>(string name, IEnumerable<T> rows)
        {
            var (kept, cut) = McpResults.Cap(rows, SectionLimit);
            if (cut)
            {
                truncated.Add(name);
            }

            return kept;
        }

        object WithServes(SubjectListItem s) => new
        {
            s.Id, s.Urn, s.Type, s.Title, s.Status, s.TargetDate,
            Area = s.AreaName ?? s.Area,
            Serves = serves[s.Id].Select(e => new { byId[e.ToId].Id, byId[e.ToId].Type, byId[e.ToId].Title }).ToList(),
        };

        // Open as list_items status=open counts it: anything not Completed or Abandoned, a goal or
        // project with no status set included. Active ones first.
        IEnumerable<SubjectListItem> Open(string type) => all
            .Where(s => s.Type == type && !StatusVocabulary.IsTerminal(s.Status))
            .OrderBy(s => Is(s.Status, "Active") ? 0 : 1)
            .ThenBy(s => s.TargetDate ?? "9999", StringComparer.Ordinal)
            .ThenBy(s => s.Title, StringComparer.OrdinalIgnoreCase);
        var openGoals = Open(SubjectTypes.Goal).ToList();

        var openTasks = all.Where(s => s.Type == SubjectTypes.Task && !StatusVocabulary.IsTerminal(s.Status)).ToList();
        var due = openTasks
            .Where(s => IsOnOrBefore(s.Due, today) || IsOnOrBefore(s.Scheduled, today))
            .OrderBy(s => s.Due ?? s.Scheduled, StringComparer.Ordinal)
            .ToList();
        var dueIds = due.Select(s => s.Id).ToHashSet();
        var inProgress = openTasks.Where(s => !dueIds.Contains(s.Id) && Is(s.Status, "In progress"));

        var habits = subjects.GetHabits().ToList();
        var todayStates = subjects.GetHabitOccurrences(on: stamp.Today).ToDictionary(o => o.HabitId, o => o.State);
        var building = habits
            .Where(h => !StatusVocabulary.IsTerminal(h.Status) && !StatusVocabulary.IsPlanned(h.Status)
                        && todayStates.ContainsKey(h.Id))
            .Select(h => new { h.Id, h.Urn, h.Name, Today = todayStates[h.Id], h.CurrentStreak });
        var maintained = habits
            .Where(h => Is(h.Status, "Established"))
            .Select(h => new { h.Id, h.Urn, h.Name, Today = todayStates.GetValueOrDefault(h.Id) });

        var weekEnd = McpResults.Day(stamp.Today.AddDays(6));
        var appointments = subjects.GetAppointments()
            .Where(a => a.Date is { } d
                        && string.CompareOrdinal(d, today) >= 0
                        && string.CompareOrdinal(d, weekEnd) <= 0)
            .Select(a => new { a.Id, a.Urn, a.Title, a.Date, a.StartTime, a.EndTime, a.Location, a.Status });

        var timers = subjects.GetTimers()
            .Where(t => t.State is "running" or "paused")
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

        var body = new
        {
            IdentityStatements = Section("identity_statements",
                all.Where(s => s.Type == SubjectTypes.Value).Select(s => new { s.Id, s.Urn, s.Title })),
            Areas = Section("areas", subjects.GetAreas().Select(a => new { a.Id, a.Urn, a.Name })),
            OpenGoals = Section("open_goals", openGoals.Select(WithServes)),
            OpenProjects = Section("open_projects", Open(SubjectTypes.Project).Select(WithServes)),
            DueAndOverdue = Section("due_and_overdue", due.Select(s => ItemRef.From(s) with
            {
                Overdue = string.CompareOrdinal(s.Due ?? s.Scheduled, today) < 0 ? true : null,
            })),
            InProgress = Section("in_progress", inProgress.Select(ItemRef.From)),
            HabitsToday = Section("habits_today", building),
            HabitsMaintained = Section("habits_maintained", maintained),
            AppointmentsNext7Days = Section("appointments_next_7_days", appointments),
            InboxCount = subjects.GetInboxCount(),
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
                      + $"({openGoals.Count(s => Is(s.Status, "Active"))} Active), "
                      + $"{McpResults.Plural(due.Count, "task")} due or overdue, "
                      + $"{McpResults.Plural(todayStates.Count, "habit")} scheduled today, "
                      + $"{McpResults.Plural(body.InboxCount, "inbox item")}.";
        return McpResults.Ok(stamp, summary, body);
    }

    internal static bool IsOnOrBefore(string? day, string today)
        => !string.IsNullOrEmpty(day) && string.CompareOrdinal(day, today) <= 0;

    internal static bool Is(string? status, string wanted)
        => string.Equals(status?.Trim(), wanted, StringComparison.OrdinalIgnoreCase);
}
