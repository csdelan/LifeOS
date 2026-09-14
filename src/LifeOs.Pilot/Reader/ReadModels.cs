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
    public string? Due { get; set; }
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
    public string? Due { get; set; }
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

/// <summary>
/// One flagged item awaiting triage, from <c>bsk.v_inbox</c> (INBOX-1): membership is
/// asserted by a triage flag, not inferred. An item is either an event (a raw capture)
/// or a subject (an Idea/Problem flagged on creation). The Inbox works this list down
/// with a GTD resolution (Promote / Relate / File / Drop).
/// </summary>
public sealed class InboxItem
{
    public Guid ItemId { get; set; }
    public string ItemKind { get; set; } = "";
    public DateTime TriagedAt { get; set; }
    public string? SubjectUrn { get; set; }
    public string? SubjectType { get; set; }
    public string? SubjectTitle { get; set; }
    public string? EventKind { get; set; }
    public string? EventContent { get; set; }

    /// <summary>True when the item is a raw capture event (rather than a subject).</summary>
    public bool IsEvent => string.Equals(ItemKind, "event", StringComparison.Ordinal);

    /// <summary>How to name this item to <c>bsk</c>: an event by its id, a subject by its urn.</summary>
    public string Ref => IsEvent ? ItemId.ToString() : SubjectUrn ?? ItemId.ToString();

    /// <summary>Display kind: the event kind, or the subject type.</summary>
    public string Kind => (IsEvent ? EventKind : SubjectType) ?? "";

    /// <summary>The item's full text for the preview pane: event content, or subject title.</summary>
    public string Content => (IsEvent ? EventContent : SubjectTitle) ?? "";

    /// <summary>A one-line, length-capped preview of the content, for the list.</summary>
    public string Preview
    {
        get
        {
            var text = Content.ReplaceLineEndings(" ").Trim();
            return text.Length == 0 ? "(empty)"
                : text.Length > 100 ? text[..100] + "…"
                : text;
        }
    }
}
