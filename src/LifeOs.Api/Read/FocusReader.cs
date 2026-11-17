using LifeOs.Application.Abstractions;
using LifeOs.Domain;

namespace LifeOs.Api.Read;

/// <summary>
/// Today, as Focus (<c>GET /api/focus</c>) and an agent's <c>life_snapshot</c> both see it (code
/// review CR106): the open Tasks due or in progress, each tracked habit's day and the Inbox count,
/// worked out once. The web had put Focus together itself from five reads, every subject and every
/// edge among them, with its own copy of these rules.
/// </summary>
public sealed class FocusReader(SubjectReader subjects, IHomeCalendar calendar, IFocusRepository chosen)
{
    /// <summary>How many In progress Tasks Focus offers as next actions.</summary>
    public const int NextActionLimit = 4;

    /// <summary>How many projects Focus lists under its goal.</summary>
    public const int ObjectiveLimit = 3;

    /// <summary>The types <see cref="FocusDay.Open"/> holds: the goals, projects and Tasks Focus shows.</summary>
    public static readonly IReadOnlyList<string> Types = [SubjectTypes.Goal, SubjectTypes.Project, SubjectTypes.Task];

    /// <summary>
    /// What's open on <paramref name="today"/>, leaving out <paramref name="hiddenTypes"/> (an
    /// agent's switched-off areas).
    /// </summary>
    public async Task<FocusDay> DayAsync(
        DateOnly today, IReadOnlyCollection<string>? hiddenTypes = null, CancellationToken cancellationToken = default)
    {
        var day = today.ToString("yyyy-MM-dd");

        // Only what's open, in SQL (code review CR54): a finished item, such as each repetition of
        // a daily chore Chris has done, isn't read. And only the types Focus and life_snapshot
        // show (code review CR119): an Appointment's occurrences two years ahead, and every
        // Person, Area, Value and Metric, which have no status and so are always open, aren't.
        var open = await subjects.ListSubjectsAsync(
            new SubjectFilter { Types = Types, Status = SubjectFilter.Open, HiddenTypes = hiddenTypes ?? [] }, cancellationToken);

        // TASKS-1: due today or late, or its do-date has come (the Tasks page's Overdue and Today).
        var openTasks = open.Where(s => s.Type == SubjectTypes.Task && !StatusVocabulary.IsTerminal(s.Status)).ToList();
        var due = openTasks
            .Where(s => IsOnOrBefore(s.Due, day) || IsOnOrBefore(s.Scheduled, day))
            .OrderBy(s => s.Due ?? s.Scheduled, StringComparer.Ordinal)
            .ToList();
        var dueIds = due.Select(s => s.Id).ToHashSet();
        var inProgress = openTasks.Where(s => !dueIds.Contains(s.Id) && Is(s.Status, "In progress")).ToList();

        // Each tracked habit's day as the kernel lists it (code review CR105), or a day to log a
        // session done anyway on. A Planned habit isn't tracked yet, and an Abandoned one has left.
        var habits = await subjects.GetHabitsAsync(cancellationToken: cancellationToken);
        var rows = (await subjects.GetHabitOccurrencesAsync(on: today, cancellationToken: cancellationToken))
            .ToDictionary(o => o.HabitId);
        List<FocusHabit> Days(Func<HabitRow, bool> stage) =>
        [
            .. habits.Where(stage).Select(h => new FocusHabit(h, HabitDay.Of(h, today, rows.GetValueOrDefault(h.Id)))),
        ];

        return new FocusDay(
            day,
            open,
            due,
            inProgress,
            Days(IsBuilding),
            Days(h => Is(h.Status, "Established")),
            await subjects.CountInboxAsync(cancellationToken));
    }

    /// <summary>The Focus page for today.</summary>
    public async Task<DashboardRead> DashboardAsync(CancellationToken cancellationToken = default)
    {
        var focus = await DayAsync(await calendar.TodayAsync(cancellationToken), cancellationToken: cancellationToken);

        List<SubjectListItem> Active(string type) =>
            [.. focus.Open.Where(s => s.Type == type && Is(s.Status, "Active"))];
        var goals = Active(SubjectTypes.Goal);
        var projects = Active(SubjectTypes.Project);

        // The goal Chris chose leads, or the one with the nearest target date; the projects that
        // result in it are its objectives, or any Active ones when none do.
        var pick = await PrimaryAsync(goals, cancellationToken);
        var primary = pick.Goal;
        var serving = primary is null
            ? []
            : (await subjects.GetAlignmentTargetsAsync(
                    [.. projects.Select(p => p.Id)], [SubjectRelations.ResultsIn], cancellationToken))
                .Where(t => t.Id == primary.Id)
                .Select(t => t.FromId)
                .ToHashSet();
        var objectives = projects.Where(p => serving.Contains(p.Id)).ToList();

        var lapsed = pick.Choice is { Lapsed: true } l ? l : null;
        return new DashboardRead
        {
            PrimaryFocus = new FocusGoal
            {
                Title = primary?.Title ?? "No active goal yet",
                SubjectId = primary?.Id.ToString() ?? "",
                Kind = "goal",
                Cover = primary?.Cover,
                Chosen = pick.Chosen,
                ChosenAt = pick.Chosen ? pick.Choice!.ChosenAt : null,
                ChosenIn = pick.Chosen ? pick.Choice!.ReviewTitle : null,
                LapsedTitle = lapsed?.GoalTitle,
                LapsedAs = lapsed?.LapsedAs,
            },
            Objectives =
            [
                .. (objectives.Count > 0 ? objectives : projects)
                    .Take(ObjectiveLimit)
                    .Select(p => new FocusObjective { Id = p.Id, Title = p.Title, SubjectId = p.Id }),
            ],
            DueAndOverdue = focus.DueAndOverdue,
            NextActions = [.. focus.InProgress.Take(NextActionLimit)],
            InboxCount = focus.InboxCount,
            TodayHabits = [.. focus.Building.Select(h => h.Day)],
            MaintainingHabits = [.. focus.Maintaining.Select(h => h.Day)],
            ActiveGoals = goals,
            ActiveProjects = projects,
        };
    }

    /// <summary>
    /// The Primary focus among <paramref name="activeGoals"/> (TODAY-1, D6, focus plan G1): the Goal
    /// Chris last chose while it's still Active, else the one with the nearest target date (ties by
    /// title), offered as a suggestion; null when there's no Active goal. <see cref="FocusPick.Choice"/>
    /// is his last choice either way, so a lapsed one can be named.
    /// </summary>
    public async Task<FocusPick> PrimaryAsync(
        IReadOnlyList<SubjectListItem> activeGoals, CancellationToken cancellationToken = default)
    {
        var choice = await chosen.GetAsync(PrimaryFocus.Weekly, cancellationToken);
        if (choice is { Holds: true, GoalId: { } id } && activeGoals.FirstOrDefault(g => g.Id == id) is { } held)
        {
            return new FocusPick(held, Chosen: true, choice);
        }

        var suggested = activeGoals
            .OrderBy(s => s.TargetDate ?? "9999", StringComparer.Ordinal)
            .ThenBy(s => s.Title, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        return new FocusPick(suggested, Chosen: false, choice);
    }

    /// <summary>A habit being built: tracked, not Planned, not yet Established or Abandoned.</summary>
    public static bool IsBuilding(HabitRow habit)
        => !StatusVocabulary.IsTerminal(habit.Status) && !StatusVocabulary.IsPlanned(habit.Status);

    internal static bool IsOnOrBefore(string? day, string today)
        => !string.IsNullOrEmpty(day) && string.CompareOrdinal(day, today) <= 0;

    internal static bool Is(string? status, string wanted)
        => string.Equals(status?.Trim(), wanted, StringComparison.OrdinalIgnoreCase);
}

/// <summary>What's open on <see cref="Today"/>, as <see cref="FocusReader.DayAsync"/> works it out.</summary>
/// <param name="Today">The home day, <c>yyyy-MM-dd</c>.</param>
/// <param name="Open">Every open, unarchived Goal, Project and Task (<see cref="FocusReader.Types"/>).</param>
/// <param name="DueAndOverdue">Open Tasks due or scheduled on or before today, soonest first.</param>
/// <param name="InProgress">Open Tasks In progress that aren't due.</param>
/// <param name="Building">Each Building habit and its day.</param>
/// <param name="Maintaining">Each Established habit and its day.</param>
/// <param name="InboxCount">How many items wait in the Inbox.</param>
public sealed record FocusDay(
    string Today,
    IReadOnlyList<SubjectListItem> Open,
    IReadOnlyList<SubjectListItem> DueAndOverdue,
    IReadOnlyList<SubjectListItem> InProgress,
    IReadOnlyList<FocusHabit> Building,
    IReadOnlyList<FocusHabit> Maintaining,
    int InboxCount);

/// <summary>A tracked habit and its day.</summary>
public sealed record FocusHabit(HabitRow Habit, HabitDay Day);

/// <summary>
/// The Primary focus: the Goal (null when there's no Active goal), whether Chris chose it or it's
/// the suggestion, and his last choice, if he ever made one.
/// </summary>
public sealed record FocusPick(SubjectListItem? Goal, bool Chosen, ChosenFocus? Choice);
