using LifeOs.Domain;

namespace LifeOs.Application.Abstractions;

/// <summary>A clock, so services can be tested with a fixed time.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>
/// The calendar in the home zone (<see cref="HomeZone"/>, <c>Rewards:HomeTimeZone</c>): what
/// day it is where Chris lives, whatever zone the server or the database runs in. The
/// solution runs with invariant globalization, so the database does the zone math.
/// </summary>
public interface IHomeCalendar
{
    /// <summary>
    /// The date in the home zone at <paramref name="instant"/>. Throws
    /// <see cref="InvalidOperationException"/> for a zone the database doesn't know.
    /// </summary>
    Task<DateOnly> DateAtAsync(DateTimeOffset instant, CancellationToken cancellationToken = default);
}

/// <summary>
/// Stores raw captured content. Stage 1 is text-only; the store returns the id
/// events reference. Content is immutable once written.
/// </summary>
public interface IArtifactStore
{
    Task<Guid> AddAsync(string content, CancellationToken cancellationToken = default);
}

/// <summary>Appends events to the source stream. The only write path for events.</summary>
public interface IEventStore
{
    Task<Guid> AppendAsync(NewEvent newEvent, CancellationToken cancellationToken = default);
}

/// <summary>
/// Stores and retrieves an artifact's <em>binary</em> payload — the swap seam for
/// where the bytes physically live. The default adapter stores them as Postgres
/// <c>bytea</c> and writes the event + artifact + blob as one transaction, so the
/// bytes commit atomically with their event and the append-only guarantee covers
/// them. A future object-storage adapter (Supabase Storage / S3) implements this
/// same interface — writing bytes to the object store and the artifact / blob-metadata
/// / event rows to Postgres — with no change to anything above it and no schema churn.
/// </summary>
public interface IArtifactBlobStore
{
    /// <summary>
    /// Writes an event, its artifact (text sidecar), and the binary payload as one
    /// atomic unit, deduplicated by content hash: if an event already exists for
    /// (<see cref="NewBinaryCapture.SourceId"/>, <see cref="NewBinaryCapture.Sha256"/>),
    /// nothing is inserted and the pre-existing ids are returned with
    /// <see cref="BinaryCaptureResult.Deduplicated"/> set.
    /// </summary>
    Task<BinaryCaptureResult> PutAsync(NewBinaryCapture capture, CancellationToken cancellationToken = default);

    /// <summary>True when the artifact carries a binary payload.</summary>
    Task<bool> ExistsAsync(Guid artifactId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads an artifact's bytes and metadata for export, or <c>null</c> when the
    /// artifact has no binary payload. The supported way to get bytes back out
    /// byte-for-byte (behind <c>bsk artifact get</c> and, later, the web API).
    /// </summary>
    Task<ArtifactBlob?> GetAsync(Guid artifactId, CancellationToken cancellationToken = default);

    /// <summary>Reads an artifact's binary metadata without the bytes, or <c>null</c> when it has none.</summary>
    Task<ArtifactBlobMetadata?> GetMetadataAsync(Guid artifactId, CancellationToken cancellationToken = default);
}

/// <summary>Reads source events. Read-only; the source stream is never mutated.</summary>
public interface IEventReader
{
    Task<SourceEvent?> FindAsync(Guid id, CancellationToken cancellationToken = default);
}

/// <summary>Reads and creates subjects. Used by resolve-or-create flows.</summary>
public interface ISubjectRepository
{
    Task<SubjectRef?> FindByUrnAsync(string urn, CancellationToken cancellationToken = default);

    Task<SubjectRef?> FindByTypeAndTitleAsync(
        string type, string title, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds subjects whose URN carries the given short id (the minted hex tail).
    /// Returns every match so the caller can disambiguate; short ids are unique by
    /// construction, so this is normally zero or one.
    /// </summary>
    Task<IReadOnlyList<SubjectRef>> FindByShortIdAsync(
        string shortId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds subjects whose title contains <paramref name="fragment"/>
    /// (case-insensitive), for fuzzy resolution. An exact case-insensitive title
    /// match, when present, sorts first so the resolver can prefer it.
    /// </summary>
    Task<IReadOnlyList<SubjectRef>> FindByTitleContainsAsync(
        string fragment, CancellationToken cancellationToken = default);

    Task<Guid> CreateAsync(NewSubject newSubject, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a subject and a parent edge (child <paramref name="relation"/> parent,
    /// child = the edge's <c>from</c>) in one transaction, so parent-first creation is
    /// all-or-nothing: if the edge cannot be written, the child is not left orphaned
    /// (GEN-7). Returns the new child id and edge id.
    /// </summary>
    Task<(Guid ChildId, Guid EdgeId)> CreateWithParentEdgeAsync(
        NewSubject child, string relation, Guid parentId, string provenance,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a subject and an edge pointing <em>into</em> it (existing
    /// <paramref name="fromId"/> <paramref name="relation"/> new subject) in one
    /// transaction — the shape promotion needs: an Idea <c>results_in</c> the new work
    /// it produced. All-or-nothing, so a failed edge leaves no orphan. Returns the new
    /// subject id and edge id.
    /// </summary>
    Task<(Guid NewId, Guid EdgeId)> CreateWithIncomingEdgeAsync(
        NewSubject newSubject, string relation, Guid fromId, string provenance,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Merges a jsonb patch into a subject's <c>attributes</c> and removes the named
    /// keys, in one update. Returns whether a row was affected (<c>false</c> = no such
    /// subject). Only the attributes bag is touched — type, title, and status are
    /// unaffected; status still moves solely by a <c>state_change</c> event.
    /// </summary>
    Task<bool> UpdateAttributesAsync(
        Guid id, string patchJson, IReadOnlyList<string> removeKeys,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes a subject's <c>title</c> in place. Only the title column is touched:
    /// the URN is an immutable handle (its slug is a birth-time convenience), and
    /// edges/tags reference the subject by <c>id</c>, so a rename never breaks a link.
    /// Returns whether a row was affected (<c>false</c> = no such subject). Renaming a
    /// reuse-by-title subject (e.g. a Problem) onto a title that already exists trips
    /// the store's uniqueness guarantee, surfaced as <see cref="DuplicateSubjectException"/>.
    /// </summary>
    Task<bool> RenameAsync(
        Guid id, string newTitle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a single top-level attribute value as text (<c>attributes-&gt;&gt;key</c>),
    /// or <c>null</c> when the subject or the key is absent.
    /// </summary>
    Task<string?> GetAttributeValueAsync(
        Guid id, string key, CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads which occurrence dates of a recurring appointment series still need to be
/// materialized (CAL-1 / D10). Expands the series' recurrence in the database and
/// subtracts the dates already materialized as occurrence subjects.
/// </summary>
public interface IAppointmentRepository
{
    Task<IReadOnlyList<DateOnly>> PendingOccurrenceDatesAsync(
        Guid seriesId, string seriesUrn, DateOnly through, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames an Appointment and, in the same transaction, the occurrences it has
    /// materialized that are dated on or after <paramref name="fromDate"/> and still
    /// carry <paramref name="previousTitle"/> (each occurrence copied the series title
    /// when it was created). Past occurrences and ones renamed individually keep their
    /// titles. For a one-time appointment or an occurrence nothing else matches.
    /// Returns whether the appointment existed and how many occurrences followed it.
    /// </summary>
    Task<(bool Updated, int OccurrencesRenamed)> RenameWithUpcomingOccurrencesAsync(
        Guid id, string urn, string previousTitle, string newTitle, DateOnly fromDate,
        CancellationToken cancellationToken = default);
}

/// <summary>Creates directed subject → subject edges in <c>bsk.subject_relation</c>.</summary>
public interface IRelationRepository
{
    Task<Guid> CreateAsync(
        Guid fromSubject, string relation, Guid toSubject, string provenance,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a directed edge (from → relation → to). An alignment edge is a
    /// structural fact, not a lifecycle event, so removal is a hard delete rather
    /// than a tombstone (unlike status / archive, whose history matters). Returns the
    /// number of edges removed — 0 when none matched.
    /// </summary>
    Task<int> DeleteAsync(
        Guid fromSubject, string relation, Guid toSubject,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Writes an event and its <c>event → subject</c> edges as one atomic unit, so an
/// activity and the commitments it evidences/violates are never half-recorded. The
/// event stream is append-only, so a partial write cannot be corrected after the
/// fact — atomicity here is a correctness requirement, not an optimisation.
/// </summary>
public interface IActivityWriter
{
    Task<Guid> WriteAsync(
        NewEvent activityEvent, IReadOnlyList<SubjectEventEdge> edges,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads and appends Task timer actions (TASKS-2). Nothing about a timer is stored
/// except its append-only actions; state and recorded time are folded by
/// <c>bsk.v_task_timer</c>.
/// </summary>
public interface ITimerRepository
{
    /// <summary>The task's timer, or <c>null</c> when it has never been started.</summary>
    Task<TimerSnapshot?> GetAsync(Guid subjectId, CancellationToken cancellationToken = default);

    /// <summary>Every timer that is running right now — normally none or one.</summary>
    Task<IReadOnlyList<TimerSnapshot>> GetRunningAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends timer actions, each an event plus a <c>concerns</c> edge to its task, as
    /// one transaction serialized against every other timer write. Each task's state is
    /// re-read inside that transaction; if any no longer matches its
    /// <see cref="TimerWrite.ExpectedState"/>, nothing is written and
    /// <see cref="TimerConflictException"/> is thrown.
    /// </summary>
    Task AppendAsync(IReadOnlyList<TimerWrite> writes, CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads and appends the rewards ledger (REWARD-1 / D11). Nothing about a wallet is
/// stored except its append-only <c>ledger</c> events; balances and history are folded
/// by <c>bsk.v_wallet</c> / <c>bsk.v_ledger</c>. Every write is serialized under one
/// wallet-wide lock, so a balance check and the redemption it guards cannot interleave
/// with another write.
/// </summary>
public interface ILedgerRepository
{
    /// <summary>The totals of every currency the ledger has touched; untouched currencies have no row.</summary>
    Task<IReadOnlyList<WalletBalance>> GetBalancesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The entries that carry amounts, newest first, optionally within
    /// [<paramref name="from"/>, <paramref name="to"/>) and capped at <paramref name="limit"/>.
    /// </summary>
    Task<IReadOnlyList<LedgerEntry>> GetEntriesAsync(
        DateTimeOffset? from, DateTimeOffset? to, int? limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Each local day's totals per currency for the days [<paramref name="from"/>,
    /// <paramref name="to"/>), read in <paramref name="timeZone"/> (an IANA name such as
    /// <c>America/Chicago</c>): an entry falls on the date its <c>occurred_at</c> reads there,
    /// so an evening completion is filed on its own evening, not the next UTC day. Days and
    /// currencies without entries have no row. Throws <see cref="ArgumentException"/> for a
    /// time zone the database doesn't know.
    /// </summary>
    Task<IReadOnlyList<LedgerDayTotal>> GetDailyTotalsAsync(
        DateOnly from, DateOnly to, string timeZone, CancellationToken cancellationToken = default);

    /// <summary>
    /// The credit of each of <paramref name="currencies"/> per local day (<c>bsk.v_award_credit</c>,
    /// xp-domains plan §3.4): every currently paid award counts on its latest earn's credited
    /// day, or, for an earn from before credited days, on its instant's date in the home zone.
    /// Days without credit have no row. Also answers today's date in the home zone, so a fold
    /// and its data agree on what today is, and the day the wallet opened there.
    /// </summary>
    Task<DomainCredit> GetDomainDaysAsync(
        IReadOnlyCollection<string> currencies, CancellationToken cancellationToken = default);

    /// <summary>
    /// The award keys with <paramref name="trigger"/> the ledger currently holds (keys that net
    /// to non-zero) — what a subject-less award, a rank-up, has been paid.
    /// </summary>
    Task<IReadOnlySet<string>> GetPaidKeysAsync(string trigger, CancellationToken cancellationToken = default);

    /// <summary>
    /// The earn behind the latest credit of <paramref name="currency"/> on or before
    /// <paramref name="day"/> (<c>bsk.v_award_credit</c>), or null — what a rank-up cites.
    /// </summary>
    Task<Guid?> GetLatestCreditEventAsync(
        string currency, DateOnly day, CancellationToken cancellationToken = default);

    /// <summary>When the wallet was opened (its earliest <c>open</c> entry), or <c>null</c> before launch.</summary>
    Task<DateTimeOffset?> GetOpenedAtAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes <paramref name="openEvent"/> unless the wallet is already open, under the
    /// wallet lock. Returns when the wallet opened and whether this call opened it.
    /// </summary>
    Task<(DateTimeOffset OpenedAt, bool Created)> OpenAsync(
        NewEvent openEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends ledger entries, each event with its edges, as one transaction under the
    /// wallet lock. If a <see cref="LedgerWrite.GuardBalance"/> entry would leave any of
    /// its currencies below zero, nothing is written and
    /// <see cref="InsufficientBalanceException"/> is thrown. If an entry's
    /// <see cref="LedgerWrite.AwardKey"/> is no longer in its
    /// <see cref="LedgerWrite.ExpectPaid"/> state, nothing is written and
    /// <see cref="LedgerConflictException"/> is thrown. Returns the new event ids.
    /// </summary>
    Task<IReadOnlyList<Guid>> AppendAsync(
        IReadOnlyList<LedgerWrite> writes, CancellationToken cancellationToken = default);

    /// <summary>The awards <c>bsk.v_reward_due</c> says the subject is due right now.</summary>
    Task<IReadOnlyList<DueAward>> GetDueAsync(Guid subjectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every award the record supports for the subject, whatever the wallet's start line
    /// (<c>bsk.v_reward_trigger</c>) — read only to open the wallet at the first trigger
    /// it pays.
    /// </summary>
    Task<IReadOnlyList<DueAward>> GetTriggersAsync(Guid subjectId, CancellationToken cancellationToken = default);

    /// <summary>The awards the ledger currently holds for the subject (keys that net to non-zero).</summary>
    Task<IReadOnlyList<PaidAward>> GetPaidAsync(Guid subjectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every subject reconcile could change: those with an award due, and those with an
    /// award paid — the sweep behind <c>bsk rewards reconcile</c>.
    /// </summary>
    Task<IReadOnlyList<SubjectRef>> GetReconcileCandidatesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The newest non-ledger event about the subject (a status change, an adherence
    /// recording, a timer action), or <c>null</c> — what a reversal cites when no
    /// triggering write is at hand.
    /// </summary>
    Task<Guid?> GetLatestSubjectEventAsync(Guid subjectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The reward catalog (<c>bsk.v_reward</c>, REWARD-3) by name, with what has been
    /// redeemed against each Reward; archived Rewards only when asked.
    /// </summary>
    Task<IReadOnlyList<CatalogReward>> GetCatalogAsync(
        bool includeArchived, CancellationToken cancellationToken = default);

    /// <summary>One Reward's catalog row, archived or not, or <c>null</c> when no Reward has that id.</summary>
    Task<CatalogReward?> GetCatalogRewardAsync(Guid rewardId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Creates <c>event → subject</c> edges (concerns/evidences/violates) for events that
/// already exist — the write path behind <c>bsk relate</c>. Distinct from
/// <see cref="IActivityWriter"/>, which writes a new event and its edges together.
/// </summary>
public interface ISubjectEventRepository
{
    /// <summary>
    /// Records one edge, idempotently: an edge that already exists is left untouched.
    /// Returns whether a new edge was created.
    /// </summary>
    Task<bool> CreateEdgeAsync(
        Guid eventId, Guid subjectId, string relation, string provenance,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Adds and removes tags on an item (a subject or an event) in <c>bsk.item_tag</c>
/// (GEN-1). Tags arrive already normalized (see <see cref="LifeOs.Domain.Tags"/>).
/// Adding is idempotent; removing a tag that is absent is a no-op.
/// </summary>
public interface ITagRepository
{
    /// <summary>Adds tags to the item; returns how many were newly added.</summary>
    Task<int> AddAsync(
        bool isEvent, Guid itemId, IReadOnlyCollection<string> tags,
        CancellationToken cancellationToken = default);

    /// <summary>Removes tags from the item; returns how many were removed.</summary>
    Task<int> RemoveAsync(
        bool isEvent, Guid itemId, IReadOnlyCollection<string> tags,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Copies every tag on the source item onto a subject (promotion carries tags to the
    /// new subject); returns how many were newly added. The source keeps its tags.
    /// </summary>
    Task<int> CopyToSubjectAsync(
        bool fromIsEvent, Guid fromItemId, Guid toSubjectId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames a tag everywhere it is used, in one transaction. An item that already
    /// carries <paramref name="newTag"/> keeps that row and drops the old one, so
    /// renaming onto an existing tag merges the two. Returns how many rows were
    /// relabelled and how many were folded into an existing tag.
    /// </summary>
    Task<(int Renamed, int Merged)> RenameAsync(
        string oldTag, string newTag, CancellationToken cancellationToken = default);
}
