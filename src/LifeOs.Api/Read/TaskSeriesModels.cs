namespace LifeOs.Api.Read;

/// <summary>
/// A Task's place in a repeating Task (TASKS-3, D16), on its detail: the series it belongs to
/// (or is), the pattern, and the series' repetitions counted. Dates are <c>yyyy-MM-dd</c>.
/// </summary>
public sealed class TaskRepeatRead
{
    /// <summary>True on the series itself; false on one of its repetitions.</summary>
    public bool IsSeries { get; set; }

    public Guid SeriesId { get; set; }
    public string SeriesUrn { get; set; } = "";
    public string SeriesTitle { get; set; } = "";

    /// <summary>The series' pattern, the JSON object D8 stores (<c>{"freq":"weekly","on":["wed"]}</c>).</summary>
    public string Recurrence { get; set; } = "";

    /// <summary>The date the series repeats from.</summary>
    public string? Start { get; set; }

    /// <summary>The series' own status: Not started while it repeats, Cancelled once stopped.</summary>
    public string? SeriesStatus { get; set; }

    /// <summary>Cancelled or archived: the series makes no more repetitions.</summary>
    public bool Stopped { get; set; }

    /// <summary>The date a repetition stands for; null on the series.</summary>
    public string? Occurrence { get; set; }

    public long OpenCount { get; set; }
    public long CompletedCount { get; set; }
    public long MissedCount { get; set; }
    public long CancelledCount { get; set; }
    public string? LastCompletedOn { get; set; }

    /// <summary>The earliest due date among its open repetitions.</summary>
    public string? NextDue { get; set; }

    /// <summary>The latest date it has made a repetition for.</summary>
    public string? LatestOccurrence { get; set; }
}

/// <summary>One repeating Task (<c>GET /api/tasks/series</c>): its pattern and its repetitions counted.</summary>
public sealed class TaskSeriesRow
{
    public Guid Id { get; set; }
    public string Urn { get; set; } = "";
    public string Title { get; set; } = "";

    /// <summary>The pattern, the JSON object D8 stores.</summary>
    public string Recurrence { get; set; } = "";

    public string? Start { get; set; }
    public string? Status { get; set; }
    public bool Archived { get; set; }

    /// <summary>Cancelled or archived: it makes no more repetitions.</summary>
    public bool Stopped { get; set; }

    public string? Area { get; set; }
    public string? AreaName { get; set; }
    public long OpenCount { get; set; }
    public long CompletedCount { get; set; }
    public long MissedCount { get; set; }
    public long CancelledCount { get; set; }
    public string? LastCompletedOn { get; set; }
    public string? NextDue { get; set; }
    public string? LatestOccurrence { get; set; }

    /// <summary>What completing each repetition pays (its own price or the Task default).</summary>
    public IReadOnlyDictionary<string, long>? Bounty { get; set; }
}

/// <summary>One repetition in a repeating Task's history.</summary>
public sealed class TaskRepetitionRow
{
    public Guid Id { get; set; }
    public string Urn { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Occurrence { get; set; }
    public string? Due { get; set; }
    public string? Scheduled { get; set; }
    public string? Status { get; set; }

    /// <summary>Completed, Missed or Cancelled.</summary>
    public bool Closed { get; set; }

    /// <summary>The home-zone day it closed.</summary>
    public string? ClosedOn { get; set; }

    public bool Archived { get; set; }
}

/// <summary><c>GET /api/tasks/series/{id}</c>: a repeating Task and every repetition it has made, latest first.</summary>
public sealed class TaskSeriesHistory
{
    public TaskSeriesRow Series { get; set; } = new();
    public IReadOnlyList<TaskRepetitionRow> Repetitions { get; set; } = [];
}

/// <summary><c>POST /api/tasks/settle</c>: how many repeating Tasks it looked at and the repetitions it made.</summary>
public sealed class TaskSettleRead
{
    public int Series { get; set; }
    public IReadOnlyList<CreatedSubject> Made { get; set; } = [];
}
