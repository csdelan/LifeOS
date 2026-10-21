namespace LifeOs.Domain;

/// <summary>
/// An event to append to the source stream. Provenance, timestamps and source
/// are always set; payload is a JSON object; derived events carry their sources.
/// </summary>
public sealed record NewEvent(
    string Kind,
    string Provenance,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt,
    string SourceId,
    string? ExternalId = null,
    string PayloadJson = "{}",
    IReadOnlyList<Guid>? DerivedFrom = null,
    Guid? ArtifactId = null);

/// <summary>A subject to create. Attributes are a JSON object.</summary>
public sealed record NewSubject(
    string Urn,
    string Type,
    string Title,
    string AttributesJson = "{}",
    Guid? OriginEventId = null);

/// <summary>A lightweight read view of a subject, used for resolve-or-create.</summary>
public sealed record SubjectRef(Guid Id, string Urn, string Type, string Title);

/// <summary>
/// A lightweight read view of a source event, used by promotion. <c>SubjectId</c>
/// is the subject the event's payload references (e.g. the Problem an
/// <c>idea_session</c> is about), or <c>null</c> when the event names no subject.
/// </summary>
public sealed record SourceEvent(Guid Id, string Kind, Guid? SubjectId);

/// <summary>
/// An <c>event → subject</c> edge to write alongside its event: which subject, with
/// which relation (concerns/evidences/violates) and provenance. The event id is
/// supplied by the writer that creates the event in the same transaction.
/// </summary>
public sealed record SubjectEventEdge(Guid SubjectId, string Relation, string Provenance);

/// <summary>
/// A binary capture to write as one atomic unit: an event, its artifact (whose text
/// <c>content</c> is the transcript/description sidecar — empty when there is none),
/// and the binary payload with its metadata. <see cref="Sha256"/> is the lowercase-hex
/// content hash; it doubles as the event's <c>external_id</c>, so re-capturing an
/// identical file from the same source collapses to one event (idempotency, inv. 7).
/// </summary>
public sealed record NewBinaryCapture(
    string Kind,
    string Provenance,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt,
    string SourceId,
    string Sha256,
    byte[] Bytes,
    string ContentType,
    long ByteSize,
    string? Filename = null,
    string TextContent = "",
    string PayloadJson = "{}");

/// <summary>
/// The outcome of a binary capture: the event and artifact written (or, on a dedup
/// hit, the pre-existing ones). <see cref="Deduplicated"/> is <c>true</c> when an
/// event for this (source, hash) already existed and nothing new was inserted.
/// </summary>
public sealed record BinaryCaptureResult(Guid EventId, Guid ArtifactId, bool Deduplicated);

/// <summary>An artifact's binary payload plus its metadata — the export read model.</summary>
public sealed record ArtifactBlob(
    Guid ArtifactId, byte[] Bytes, string ContentType, long ByteSize, string Sha256, string? Filename);

/// <summary>
/// A Task's timer as <c>bsk.v_task_timer</c> folds it (TASKS-2). <see cref="TotalSeconds"/>
/// and <see cref="SessionSeconds"/> count closed segments only; while the timer is
/// running, the live figure adds <c>now − RunningSince</c>.
/// </summary>
public sealed record TimerSnapshot(
    Guid SubjectId,
    string Urn,
    string Type,
    string Title,
    string State,
    DateTimeOffset? RunningSince,
    long TotalSeconds,
    long SessionSeconds);

/// <summary>
/// One timer action to append: its event, the task it concerns, and the state the
/// timer must still be in when the write lands (so two windows racing on one timer
/// cannot both win).
/// </summary>
public sealed record TimerWrite(Guid SubjectId, string ExpectedState, NewEvent Event);

/// <summary>An artifact's binary metadata without the bytes — for list/metadata reads.</summary>
public sealed record ArtifactBlobMetadata(
    Guid ArtifactId, string ContentType, long ByteSize, string Sha256, string? Filename);

/// <summary>
/// One currency's totals as <c>bsk.v_wallet</c> folds them (REWARD-1 / D11). Earned,
/// reversed and spent are positive totals; <see cref="Balance"/> is the signed sum of
/// every ledger entry in that currency.
/// </summary>
public sealed record WalletBalance(string Currency, long Earned, long Reversed, long Spent, long Balance);

/// <summary>
/// One local day's totals in one currency (REWARD-5): what that day's ledger entries —
/// dated in the viewer's time zone — earned, reversed and spent (positive totals), and
/// <see cref="Net"/>, the signed sum of every entry that day in that currency.
/// </summary>
public sealed record LedgerDayTotal(DateOnly Date, string Currency, long Earned, long Reversed, long Spent, long Net);

/// <summary>
/// One currency's credit on one local day (xp-domains plan §3.4): what the ledger pays now for
/// the awards credited to that day in the home zone — <see cref="Base"/> before any
/// life-domain multiplier, <see cref="Paid"/> as paid.
/// </summary>
public sealed record CreditDay(string Currency, DateOnly Day, long Base, long Paid);

/// <summary>
/// The life-domain credit per currency and day, with today's date in the home zone and the
/// day the wallet opened there (null before it has), before which no rank-up pays.
/// </summary>
public sealed record DomainCredit(DateOnly Today, IReadOnlyList<CreditDay> Days, DateOnly? OpenedOn = null);

/// <summary>
/// One ledger entry as <c>bsk.v_ledger</c> reads it (REWARD-5): what it was
/// (<see cref="LedgerEntries"/>), its signed amounts per currency, and what it concerns —
/// the item that earned it, or the reward it bought.
/// </summary>
public sealed record LedgerEntry(
    Guid EventId,
    DateTimeOffset OccurredAt,
    string Entry,
    string Provenance,
    IReadOnlyDictionary<string, long> Amounts,
    string? Trigger,
    string? AwardKey,
    SubjectRef? Subject,
    string? Note,
    decimal? Dollars,
    decimal? CoinRate);

/// <summary>
/// A ledger entry to append: its event and <c>event → subject</c> edges. When
/// <see cref="GuardBalance"/> is set (a redemption), the write is refused if it would
/// leave any of its currencies below zero — there is no overdraft (REWARD-3).
/// <para>
/// An earn or reversal names its <see cref="AwardKey"/> and whether that award must
/// still be paid (<see cref="ExpectPaid"/>) when the write lands, so two reconciles
/// racing on one item cannot both pay it or both give it back.
/// </para>
/// </summary>
public sealed record LedgerWrite(
    NewEvent Event,
    IReadOnlyList<SubjectEventEdge> Edges,
    bool GuardBalance,
    string? AwardKey = null,
    bool? ExpectPaid = null);

/// <summary>
/// An award <c>bsk.v_reward_due</c> says an item is due (rewards plan §3.2): its key,
/// its trigger, the events that make it due (cited by the earn), and when it became due.
/// A <c>timed</c> award also carries the seconds the timer recorded before the completion.
/// <see cref="CreditedOn"/> is the local day it counts on (xp-domains plan §3.3): a habit
/// day's own date, a streak's last day, else the day <see cref="TriggeredAt"/> falls on in
/// the home time zone.
/// </summary>
public sealed record DueAward(
    string AwardKey, string Trigger, IReadOnlyList<Guid> SourceEventIds, DateTimeOffset TriggeredAt,
    long? TimedSeconds = null, DateOnly? CreditedOn = null);

/// <summary>
/// An award the ledger currently holds for an item: its key, its trigger, what it nets
/// to per currency (never all zero — a key that nets to zero is not paid), and its
/// newest ledger entry.
/// </summary>
public sealed record PaidAward(
    string AwardKey, string? Trigger, IReadOnlyDictionary<string, long> Net, Guid LastEventId);

/// <summary>
/// One Reward as <c>bsk.v_reward</c> reads it (REWARD-3): its kind
/// (<see cref="RewardKinds"/>), an instant reward's price (spendable currencies only, in
/// display order; empty when it has none), a savings goal's target, and what has been
/// redeemed against it. <see cref="DollarsRedeemed"/> sums the dollars stamped on each
/// redemption, so a savings goal's progress never moves when the coin rate does.
/// <see cref="PercentOfTarget"/> and <see cref="RemainingUsd"/> are set only for a
/// savings goal with a target.
/// </summary>
public sealed record CatalogReward(
    SubjectRef Reward,
    string Kind,
    IReadOnlyDictionary<string, long> Price,
    decimal? TargetUsd,
    long Redemptions,
    long CoinsRedeemed,
    decimal DollarsRedeemed,
    DateTimeOffset? LastRedeemedAt,
    decimal? PercentOfTarget,
    decimal? RemainingUsd,
    string? Description,
    bool Archived);
