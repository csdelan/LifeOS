namespace LifeOs.Domain;

/// <summary>Event kinds, matching the CHECK constraint on <c>bsk.event.kind</c>.</summary>
public static class EventKinds
{
    public const string Journal = "journal";
    public const string Note = "note";
    public const string Voice = "voice";
    public const string IdeaSession = "idea_session";
    public const string Observation = "observation";
    public const string Activity = "activity";
    public const string Measurement = "measurement";
    public const string Interaction = "interaction";
    public const string StateChange = "state_change";
}

/// <summary>Provenance values, matching the CHECK on <c>provenance</c> columns.</summary>
public static class Provenances
{
    public const string Declared = "declared";
    public const string Observed = "observed";
    public const string Derived = "derived";
}

/// <summary>Subject types, matching the CHECK on <c>bsk.subject.type</c>.</summary>
public static class SubjectTypes
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
}

/// <summary>Relation kinds, matching the CHECK constraint on <c>bsk.relation.relation</c>.</summary>
public static class RelationKinds
{
    public const string Serves = "serves";
    public const string ResultsIn = "results_in";
    public const string Evidences = "evidences";
    public const string Violates = "violates";
    public const string Concerns = "concerns";
    public const string Supersedes = "supersedes";
    public const string PromotedFrom = "promoted_from";

    public static readonly IReadOnlyList<string> All =
    [
        Serves, ResultsIn, Evidences, Violates, Concerns, Supersedes, PromotedFrom
    ];
}

/// <summary>Identifiers for where an event originated (the event's <c>source_id</c>).</summary>
public static class KernelSources
{
    /// <summary>Events written by the <c>bsk</c> command-line interface.</summary>
    public const string Cli = "cli";
}
