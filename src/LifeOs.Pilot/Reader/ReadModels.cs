namespace LifeOs.Pilot.Reader;

/// <summary>A subject type and how many subjects have it — the left-hand tree.</summary>
public sealed class TypeCount
{
    public string Type { get; set; } = "";
    public long N { get; set; }
}

/// <summary>One row in the middle list: a subject of the selected type.</summary>
public sealed class SubjectListItem
{
    public Guid Id { get; set; }
    public string Urn { get; set; } = "";
    public string Title { get; set; } = "";
    public string Status { get; set; } = "";
    public string? ExpectedCadence { get; set; }
    public DateTime? NextReviewAt { get; set; }
}

/// <summary>The detail-pane header fields for one subject.</summary>
public sealed class SubjectDetail
{
    public Guid Id { get; set; }
    public string Urn { get; set; } = "";
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
    public string Status { get; set; } = "";
    public string? ExpectedCadence { get; set; }
    public DateTime? NextReviewAt { get; set; }
    public string? Scope { get; set; }
    public string? Statement { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>One alignment-graph edge, in whichever direction it was queried.</summary>
public sealed class RelationEdge
{
    public string Relation { get; set; } = "";
    public string Urn { get; set; } = "";
    public string Type { get; set; } = "";
    public Guid SubjectId { get; set; }
}

/// <summary>An event that <c>concerns</c> the selected subject.</summary>
public sealed class ConcerningEvent
{
    public string Kind { get; set; } = "";
    public DateTime OccurredAt { get; set; }
    public Guid EventId { get; set; }
}

/// <summary>One recorded <c>state_change</c> in the subject's status history.</summary>
public sealed class StatusHistoryEntry
{
    public string Status { get; set; } = "";
    public DateTime OccurredAt { get; set; }
    public Guid Id { get; set; }
}
