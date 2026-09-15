namespace LifeOs.Pilot.Shell;

/// <summary>
/// Pilot-local mirrors of the kernel vocabularies. The Pilot must not reference
/// the kernel assemblies (invariant 9 / "no ProjectReference"), so these strings
/// are duplicated here and kept in lockstep with <c>StatusVocabulary</c> /
/// <c>ParentChildRelations</c> by convention.
/// </summary>
internal static class PilotVocab
{
    public const string Value = "Value";
    public const string Goal = "Goal";
    public const string Problem = "Problem";
    public const string Project = "Project";
    public const string Task = "Task";
    public const string Commitment = "Commitment";
    public const string Decision = "Decision";
    public const string Idea = "Idea";
    public const string Person = "Person";
    public const string Constraint = "Constraint";
    public const string Season = "Season";
    public const string Area = "Area";
    public const string Habit = "Habit";
    public const string Appointment = "Appointment";

    public const string Serves = "serves";
    public const string ResultsIn = "results_in";
    public const string Supersedes = "supersedes";

    /// <summary>User-facing label for a subject type (Value reads as Identity Statement).</summary>
    public static string Label(string type) => type switch
    {
        Value => "Identity Statement",
        Person => "Person / Agent",
        _ => type
    };

    /// <summary>Types offered by global New (GEN-6). Ideas still have a form (U2) even though Capture is the usual entry.</summary>
    public static readonly string[] CreatableTypes =
    [
        Value, Goal, Project, Task, Problem, Decision, Idea, Person, Area, Habit, Appointment, Commitment
    ];

    public static readonly string[] AlignmentRelations = [Serves, ResultsIn, Supersedes];

    public static readonly string[] PersonRoles = ["attendee", "owner", "assignee", "waiting_for", "involves"];

    private static readonly Dictionary<string, string[]> StatusByType = new(StringComparer.OrdinalIgnoreCase)
    {
        [Goal] = ["New", "Active", "Completed", "Abandoned"],
        [Project] = ["New", "Active", "Completed", "Abandoned"],
        [Task] = ["Not started", "In progress", "Waiting", "Completed", "Cancelled"],
        [Commitment] = ["Open", "Fulfilled", "Missed", "Cancelled"],
        [Decision] = ["Open", "Implementing", "Cancelled", "Closed"],
        [Problem] = ["Open", "Working", "Resolved"],
        [Appointment] = ["Scheduled", "Completed", "Cancelled", "Missed"],
        [Idea] = ["New", "Promoted", "Rejected"],
    };

    private static readonly HashSet<string> Terminal = new(StringComparer.OrdinalIgnoreCase)
    {
        "done", "completed", "resolved", "closed", "cancelled",
        "abandoned", "dropped", "archived", "superseded",
        "fulfilled", "missed", "promoted", "rejected",
    };

    // (childType, parentType) → relation. Dominant hierarchy Value→Goal→Project→Task, plus Task under Goal.
    private static readonly Dictionary<(string Child, string Parent), string> ParentMap = new()
    {
        [(Goal, Value)] = Serves,
        [(Project, Goal)] = ResultsIn,
        [(Task, Project)] = Serves,
        [(Task, Goal)] = Serves,
    };

    public static IReadOnlyList<string> StatusesFor(string type)
        => StatusByType.TryGetValue(type, out var statuses) ? statuses : [];

    public static bool HasStatus(string type) => StatusByType.ContainsKey(type);

    public static bool IsTerminal(string? status)
        => !string.IsNullOrWhiteSpace(status) && Terminal.Contains(status.Trim());

    public static bool IsActiveWorkStatus(string type, string? status)
    {
        var effective = EffectiveStatus(type, status);
        return !IsTerminal(effective);
    }

    /// <summary>
    /// The default status for a type — the first status in its vocabulary (D7). A
    /// status-less type has no default and returns "".
    /// </summary>
    public static string DefaultStatusFor(string type)
        => StatusesFor(type) is [var first, ..] ? first : "";

    /// <summary>Folded status, or the type's default when the projection is still null (D7).</summary>
    public static string EffectiveStatus(string type, string? status)
    {
        if (!string.IsNullOrWhiteSpace(status))
        {
            return CanonicalStatus(type, status);
        }

        var fallback = DefaultStatusFor(type);
        return string.IsNullOrEmpty(fallback) ? "open" : fallback;
    }

    /// <summary>
    /// Canonical casing for a status. Status is free text in the kernel (D7 validates
    /// app-side), so the same status can end up stored in mixed case — e.g. a manual
    /// <c>bsk status … active</c> versus the UI's "Active". Fold a recognized status
    /// back to its documented spelling so every screen (history, lists, overview)
    /// reads consistently; an unrecognized value passes through trimmed.
    /// </summary>
    public static string CanonicalStatus(string type, string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return "";
        }

        var trimmed = status.Trim();
        foreach (var known in StatusesFor(type))
        {
            if (string.Equals(known, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return known;
            }
        }

        return trimmed;
    }

    public static string? InferChildRelation(string childType, string parentType)
        => ParentMap.TryGetValue((childType, parentType), out var relation) ? relation : null;

    public static IReadOnlyList<string> ChildTypesFor(string parentType)
        => ParentMap.Keys.Where(k => k.Parent == parentType).Select(k => k.Child).Distinct().ToList();

    public static bool AreasArePermanent(string type) => type == Area;

    public static string DestinationForType(string type) => type switch
    {
        Task => Destinations.Tasks,
        Goal => Destinations.Goals,
        Project => Destinations.Projects,
        Habit => Destinations.Habits,
        Area => Destinations.Areas,
        Person => Destinations.People,
        Value => Destinations.Vision,
        Appointment => Destinations.Browse,
        _ => Destinations.Browse
    };
}

/// <summary>NAV-1 destination ids — the tab strip order.</summary>
internal static class Destinations
{
    public const string Dashboard = "Dashboard";
    public const string Inbox = "Inbox";
    public const string Tasks = "Tasks";
    public const string Projects = "Projects";
    public const string Goals = "Goals";
    public const string Habits = "Habits";
    public const string Reviews = "Reviews";
    public const string Vision = "Vision";
    public const string Browse = "Browse";
    public const string Areas = "Areas";
    public const string People = "People / Agents";

    public static readonly string[] All =
    [
        Dashboard, Inbox, Tasks, Projects, Goals, Habits, Reviews, Vision, Browse, Areas, People
    ];
}
