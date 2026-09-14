namespace LifeOs.Pilot.Reader;

/// <summary>A subject type and how many subjects have it — the left-hand tree.</summary>
public sealed class TypeCount
{
    public string Type { get; set; } = "";
    public long N { get; set; }
}

/// <summary>One row in a subject list (Browse, Goals, Projects, Tasks, …).</summary>
public sealed class SubjectListItem
{
    public Guid Id { get; set; }
    public string Urn { get; set; } = "";
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Status { get; set; }
    public string? Due { get; set; }
    public string? Scheduled { get; set; }
    public string? TargetDate { get; set; }
    public string? Area { get; set; }
    public string? AreaName { get; set; }
    public string? Tags { get; set; }
    public bool Archived { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? ExpectedCadence { get; set; }
    public DateTime? NextReviewAt { get; set; }
    public string? PersonKind { get; set; }
    public int? VisionOrder { get; set; }

    public string DisplayStatus => Shell.PilotVocab.EffectiveStatus(Type, Status);
}

/// <summary>The detail-pane header fields for one subject, plus the raw attributes jsonb.</summary>
public sealed class SubjectDetail
{
    public Guid Id { get; set; }
    public string Urn { get; set; } = "";
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Status { get; set; }
    public string? Due { get; set; }
    public string? ExpectedCadence { get; set; }
    public DateTime? NextReviewAt { get; set; }
    public string? Scope { get; set; }
    public string? Statement { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool Archived { get; set; }
    public string? Area { get; set; }
    public string? AreaName { get; set; }
    public string? Attributes { get; set; }

    public string DisplayStatus => Shell.PilotVocab.EffectiveStatus(Type, Status);
}

/// <summary>One alignment-graph edge, in whichever direction it was queried.</summary>
public sealed class RelationEdge
{
    public string Relation { get; set; } = "";
    public string Urn { get; set; } = "";
    public string Type { get; set; } = "";
    public Guid SubjectId { get; set; }
    public string? Title { get; set; }
}

/// <summary>An event that <c>concerns</c> the selected subject.</summary>
public sealed class ConcerningEvent
{
    public string Kind { get; set; } = "";
    public DateTime OccurredAt { get; set; }
    public Guid EventId { get; set; }
    public string? Content { get; set; }
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

public sealed class AreaRow
{
    public Guid Id { get; set; }
    public string Urn { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class TagUniverseItem
{
    public string Tag { get; set; } = "";
    public long ItemCount { get; set; }
}

public sealed class HabitRow
{
    public Guid Id { get; set; }
    public string Urn { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Cue { get; set; }
    public string? Routine { get; set; }
    public string? Reward { get; set; }
    public string? StartDate { get; set; }
    public string? EndDate { get; set; }
    public bool AllowsPartial { get; set; }
    public string? Recurrence { get; set; }
    public bool Archived { get; set; }
    public DateTime CreatedAt { get; set; }
    public int CurrentStreak { get; set; }
    public string? LastState { get; set; }
}

public sealed class HabitOccurrenceRow
{
    public Guid HabitId { get; set; }
    public string HabitUrn { get; set; } = "";
    public string HabitName { get; set; } = "";
    public DateOnly OccurrenceDate { get; set; }
    public string State { get; set; } = "";
    public bool AllowsPartial { get; set; }
}

public sealed class AppointmentRow
{
    public Guid Id { get; set; }
    public string Urn { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Date { get; set; }
    public string? StartTime { get; set; }
    public string? EndTime { get; set; }
    public string? AllDay { get; set; }
    public string? Location { get; set; }
    public string? MeetingLink { get; set; }
    public string? Area { get; set; }
    public string? SeriesUrn { get; set; }
    public string? Recurrence { get; set; }
    public string Status { get; set; } = "";
    public bool Archived { get; set; }
    public DateTime CreatedAt { get; set; }

    public bool IsSeries => !string.IsNullOrWhiteSpace(Recurrence) && string.IsNullOrWhiteSpace(Date);
}

public sealed class PersonAssociationRow
{
    public Guid SubjectId { get; set; }
    public string SubjectUrn { get; set; } = "";
    public string SubjectType { get; set; } = "";
    public string SubjectTitle { get; set; } = "";
    public string Role { get; set; } = "";
    public Guid PersonId { get; set; }
    public string PersonUrn { get; set; } = "";
    public string PersonName { get; set; } = "";
}

public sealed class PersonRow
{
    public Guid Id { get; set; }
    public string Urn { get; set; } = "";
    public string Title { get; set; } = "";
    public string? PersonKind { get; set; }
    public string? Role { get; set; }
    public bool Archived { get; set; }
}

public sealed class JournalEntry
{
    public Guid EventId { get; set; }
    public DateTime OccurredAt { get; set; }
    public string Content { get; set; } = "";
}
