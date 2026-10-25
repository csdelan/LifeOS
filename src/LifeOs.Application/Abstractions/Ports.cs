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
/// where the bytes physically live. Without object storage the adapter stores them as
/// Postgres <c>bytea</c> and writes the event + artifact + blob as one transaction, so the
/// bytes commit atomically with their event and the append-only guarantee covers them.
/// With an <see cref="IObjectStorage"/> (Supabase Storage, migration 0035) it uploads the
/// bytes first, then writes the artifact, where the object is and the event as one
/// transaction; nothing above it changes.
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

/// <summary>
/// An object store for artifact bytes (Supabase Storage, binary-artifacts.md). Objects are
/// written once, under content-addressed keys, and never replaced or deleted.
/// </summary>
public interface IObjectStorage
{
    /// <summary>The store's name, recorded with each object (<c>supabase</c>).</summary>
    string Store { get; }

    /// <summary>The bucket new objects go in.</summary>
    string Bucket { get; }

    /// <summary>
    /// Writes <paramref name="bytes"/> at <paramref name="key"/> in <see cref="Bucket"/>, creating
    /// the bucket (private) the first time. An object already at the key is left as it is: keys
    /// are content-addressed, so it holds the same bytes. Throws
    /// <see cref="ObjectStorageException"/> when the store refuses or can't be reached.
    /// </summary>
    Task PutAsync(string key, byte[] bytes, string contentType, CancellationToken cancellationToken = default);

    /// <summary>
    /// The object's bytes, or <c>null</c> when the bucket or the object isn't there. Throws
    /// <see cref="ObjectStorageException"/> when the store refuses or can't be reached.
    /// </summary>
    Task<byte[]?> GetAsync(string bucket, string key, CancellationToken cancellationToken = default);
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
    /// <para>
    /// Each day splits out its passive base XP (<see cref="LifeDomains.PassiveTriggers"/>), and
    /// the answer carries the days marked as exceptions (<c>bsk.v_exception_day</c>) and the
    /// last settled day, all read together (xp-domains plan §7.4.2), so every fold sees the
    /// same inputs.
    /// </para>
    /// </summary>
    Task<DomainCredit> GetDomainDaysAsync(
        IReadOnlyCollection<string> currencies, CancellationToken cancellationToken = default);

    /// <summary>
    /// Today's date in the home zone and the days marked as exceptions among the last
    /// <paramref name="days"/> (today included), newest first (<c>bsk.v_exception_day</c>,
    /// REWARD-8).
    /// </summary>
    Task<(DateOnly Today, IReadOnlyList<ExceptionDay> Days)> GetExceptionDaysAsync(
        int days, CancellationToken cancellationToken = default);

    /// <summary>
    /// The award keys with <paramref name="trigger"/> the ledger currently holds (keys that net
    /// to non-zero) — what a subject-less award, a rank-up, has been paid.
    /// </summary>
    Task<IReadOnlySet<string>> GetPaidKeysAsync(string trigger, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every award key with one of <paramref name="triggers"/> the ledger has ever written,
    /// paid now or not, with its net and latest earn — what a re-pricing reconcile compares
    /// against (food days, food-log plan §3.3).
    /// </summary>
    Task<IReadOnlyList<KeyedAward>> GetKeyedAwardsAsync(
        IReadOnlyCollection<string> triggers, CancellationToken cancellationToken = default);

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
    /// <see cref="LedgerWrite.ExpectPaid"/> state, or its latest earn is no longer
    /// <see cref="LedgerWrite.ExpectLatestEarn"/>, nothing is written and
    /// <see cref="LedgerConflictException"/> is thrown. Entries are checked in order, each
    /// against the ledger with the entries before it applied. Returns the new event ids.
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
/// Reads and appends the Food Log (FOOD-1..4, food-log plan §3.1–§3.2). Nothing is stored
/// but append-only events: food entries, fasts and day closes, folded by <c>bsk.v_meal</c>,
/// <c>bsk.v_fast</c> and <c>bsk.v_food_day</c>. Days and wall-clock times are in the
/// database's home zone (<c>bsk.home_zone()</c>), the same one those views read.
/// </summary>
public interface IFoodLogRepository
{
    /// <summary>The live entry with that id, or null (never logged, or deleted).</summary>
    Task<Meal?> GetMealAsync(Guid mealId, CancellationToken cancellationToken = default);

    /// <summary>Live entries whose id starts with <paramref name="prefix"/> (hex), newest first.</summary>
    Task<IReadOnlyList<Meal>> FindMealsAsync(string prefix, CancellationToken cancellationToken = default);

    /// <summary>Live entries eaten on the home-zone days [<paramref name="from"/>, <paramref name="to"/>], oldest first.</summary>
    Task<IReadOnlyList<Meal>> GetMealsAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>
    /// The newest live entries, at most <paramref name="limit"/>; only those with calories when
    /// <paramref name="withCalories"/> is set.
    /// </summary>
    Task<IReadOnlyList<Meal>> GetLatestMealsAsync(
        int limit, bool withCalories, CancellationToken cancellationToken = default);

    /// <summary>The live fast with that id, or null.</summary>
    Task<Fast?> GetFastAsync(Guid fastId, CancellationToken cancellationToken = default);

    /// <summary>Live fasts whose id starts with <paramref name="prefix"/> (hex), newest first.</summary>
    Task<IReadOnlyList<Fast>> FindFastsAsync(string prefix, CancellationToken cancellationToken = default);

    /// <summary>The fast running now, or null.</summary>
    Task<Fast?> GetRunningFastAsync(CancellationToken cancellationToken = default);

    /// <summary>Live fasts that ended on the home-zone days [<paramref name="from"/>, <paramref name="to"/>], oldest first.</summary>
    Task<IReadOnlyList<Fast>> GetFastsEndedAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>The live fasts that overlap <paramref name="span"/>, other than its <c>Except</c>.</summary>
    Task<IReadOnlyList<Fast>> GetOverlappingFastsAsync(FastSpan span, CancellationToken cancellationToken = default);

    /// <summary>The days with food, fasts or closes in [<paramref name="from"/>, <paramref name="to"/>], oldest first.</summary>
    Task<IReadOnlyList<FoodDay>> GetDaysAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>
    /// What the sobriety streak reads (FOOD-6): the Food Log's first day and the days up to
    /// <paramref name="to"/> with a drink.
    /// </summary>
    Task<DrinkHistory> GetDrinkHistoryAsync(DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>
    /// The drinks drunk in [<paramref name="from"/>, <paramref name="to"/>), counted by the
    /// home-zone day they were drunk on, oldest first (FOOD-6).
    /// </summary>
    Task<IReadOnlyList<(DateOnly Day, int Drinks)>> CountDrinksAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    /// <summary>Today in the home zone.</summary>
    Task<DateOnly> TodayAsync(CancellationToken cancellationToken = default);

    /// <summary>The home-zone day and wall-clock time of <paramref name="instant"/>.</summary>
    Task<DateTime> LocalAtAsync(DateTimeOffset instant, CancellationToken cancellationToken = default);

    /// <summary>The instant a home-zone wall-clock <paramref name="local"/> time names.</summary>
    Task<DateTimeOffset> InstantAtAsync(DateTime local, CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends food events as one transaction under the food lock, after re-checking
    /// <paramref name="guard"/> under it. When the guard no longer holds, nothing is written
    /// and <see cref="FoodConflictException"/> is thrown. Returns the new event ids.
    /// </summary>
    Task<IReadOnlyList<Guid>> AppendAsync(
        IReadOnlyList<NewEvent> events, FoodGuard guard, CancellationToken cancellationToken = default);
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

/// <summary>
/// Tokens from an OAuth token endpoint (activity plan §3.1). Secrets: never logged and never
/// answered by the API, so <see cref="ToString"/> leaves them out.
/// </summary>
/// <param name="Scopes">The scopes granted, without Oura's <c>extapi:</c> prefix.</param>
public sealed record OAuthTokens(
    string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt, IReadOnlyList<string> Scopes)
{
    public override string ToString()
        => $"OAuthTokens {{ ExpiresAt = {ExpiresAt:O}, Scopes = [{string.Join(' ', Scopes)}] }}";
}

/// <summary>
/// Oura's API v2 (activity plan §3.1, §3.3): the OAuth calls and the collections LifeOS keeps
/// (§3.9 added the tags). Errors: <see cref="OuraGrantException"/> when Oura refuses a grant,
/// <see cref="OuraAccessDeniedException"/> when it refuses an access token (401) or access
/// (403), <see cref="OuraUnavailableException"/> when it can't be reached or fails.
/// <para>
/// A collection read asks for the days [<c>start</c>, <c>end</c>] as Oura filters them —
/// which differs by collection — and pages through <c>next_token</c>; the caller widens the
/// window and filters on each record's day. A record without what identifies it (its day, or
/// a workout's, sleep period's or tag's id) is skipped.
/// </para>
/// </summary>
public interface IOuraClient
{
    /// <summary>
    /// Where to send the browser to approve LifeOS: Oura's authorize URL with this client,
    /// the scopes LifeOS reads, <paramref name="state"/> and the S256 PKCE challenge.
    /// </summary>
    Uri AuthorizeUrl(string state, string codeChallenge, Uri redirectUri);

    /// <summary>Trades the code Oura sent back for tokens.</summary>
    Task<OAuthTokens> ExchangeCodeAsync(
        string code, string codeVerifier, Uri redirectUri, CancellationToken cancellationToken = default);

    /// <summary>Spends <paramref name="refreshToken"/> (single-use) for a new pair.</summary>
    Task<OAuthTokens> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>Asks Oura to revoke the grant behind <paramref name="accessToken"/>.</summary>
    Task RevokeAsync(string accessToken, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OuraActivityDay>> GetDailyActivityAsync(
        string accessToken, DateOnly start, DateOnly end, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OuraWorkout>> GetWorkoutsAsync(
        string accessToken, DateOnly start, DateOnly end, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OuraSleepScore>> GetDailySleepAsync(
        string accessToken, DateOnly start, DateOnly end, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OuraReadiness>> GetDailyReadinessAsync(
        string accessToken, DateOnly start, DateOnly end, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OuraSleepPeriod>> GetSleepPeriodsAsync(
        string accessToken, DateOnly start, DateOnly end, CancellationToken cancellationToken = default);

    /// <summary>The tags Chris added in the Oura app (<c>enhanced_tag</c>); needs the <c>tag</c> scope.</summary>
    Task<IReadOnlyList<OuraTag>> GetTagsAsync(
        string accessToken, DateOnly start, DateOnly end, CancellationToken cancellationToken = default);
}

/// <summary>
/// Unsplash's API (cover-images plan §3.2): search, one photo, and the download counter. Only
/// photos whose image is on <c>images.unsplash.com</c> come back, so nothing can be chosen
/// that LifeOS won't show. Errors are <see cref="UnsplashException"/>, in words, never with
/// the key.
/// </summary>
public interface IUnsplashClient
{
    /// <summary>The <c>utm_source</c> on every link back to Unsplash (<c>Unsplash:AppName</c>).</summary>
    string AppName { get; }

    /// <summary>
    /// Landscape photos matching <paramref name="query"/>, a page at a time (1-based), with
    /// Unsplash's strict content filter.
    /// </summary>
    Task<UnsplashSearchPage> SearchAsync(
        string query, int page, int perPage, CancellationToken cancellationToken = default);

    /// <summary>One photo by its id; <see cref="UnsplashFailure.NotFound"/> when Unsplash no longer has it.</summary>
    Task<UnsplashPhoto> GetPhotoAsync(string photoId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tells Unsplash a photo was used (its <c>download_location</c>), as its guidelines ask
    /// when a photo becomes a header. A counter for the photographer, nothing more.
    /// </summary>
    Task TrackDownloadAsync(Uri downloadLocation, CancellationToken cancellationToken = default);
}

/// <summary>
/// A photo as Unsplash describes it, trimmed to what a cover needs. <see cref="RawUrl"/> is
/// <c>urls.raw</c>; <see cref="DownloadLocation"/> is <c>links.download_location</c>.
/// </summary>
public sealed record UnsplashPhoto(
    string Id,
    string RawUrl,
    string? Color,
    int Width,
    int Height,
    string? Description,
    string? AltDescription,
    string Photographer,
    string PhotographerUrl,
    string PhotoUrl,
    Uri? DownloadLocation);

/// <summary>One page of a search: how many photos and pages Unsplash has, and this page's photos.</summary>
public sealed record UnsplashSearchPage(int Total, int TotalPages, IReadOnlyList<UnsplashPhoto> Photos);

/// <summary>
/// A connect in progress (activity plan §3.1): the OAuth <c>state</c> sent to Oura, the PKCE
/// verifier and redirect URI the code exchange needs, and when it lapses.
/// </summary>
public sealed record PendingConnect(string State, string CodeVerifier, Uri RedirectUri, DateTimeOffset Until)
{
    public override string ToString() => $"PendingConnect {{ RedirectUri = {RedirectUri}, Until = {Until:O} }}";
}

/// <summary>
/// A connection's state as stored (<c>bsk_ops.integration</c>, migration 0036), without its
/// tokens.
/// </summary>
/// <param name="Connected">Tokens are stored (they may still need reconnecting).</param>
/// <param name="HistoryFrom">The earliest day the history backfill has reached.</param>
/// <param name="HistoryDone">The backfill found where the history starts.</param>
public sealed record IntegrationState(
    string Service,
    bool Connected,
    DateTimeOffset? ConnectedAt,
    DateTimeOffset? ExpiresAt,
    IReadOnlyList<string> Scopes,
    bool NeedsReconnect,
    string? ReconnectReason,
    DateTimeOffset? LastSyncedAt,
    string? LastSyncError,
    DateOnly? HistoryFrom,
    bool HistoryDone);

/// <summary>
/// Connections to outside services (<c>bsk_ops.integration</c>, migration 0036). Tokens are
/// encrypted at rest; the repository encrypts and decrypts them, so callers only ever see
/// <see cref="OAuthTokens"/>.
/// </summary>
public interface IIntegrationRepository
{
    /// <summary>The connection's state, or null when the service has never been connected.</summary>
    Task<IntegrationState?> GetAsync(string service, CancellationToken cancellationToken = default);

    /// <summary>
    /// Keeps <paramref name="pending"/> as the connect in progress, replacing any earlier one.
    /// Stored tokens are left alone until the new ones arrive.
    /// </summary>
    Task BeginConnectAsync(string service, PendingConnect pending, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes the connect in progress whose state is <paramref name="state"/>, clearing it so it
    /// can't be used twice: null when there's none with that state. The caller checks
    /// <see cref="PendingConnect.Until"/>.
    /// </summary>
    Task<PendingConnect?> TakePendingAsync(string service, string state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores newly granted tokens: connected as of <paramref name="connectedAt"/>, no longer
    /// needing to reconnect. A grant with a scope the stored one didn't have starts the history
    /// backfill over, so the new collection's past comes in (activity plan §3.9).
    /// </summary>
    Task SaveConnectionAsync(
        string service, OAuthTokens tokens, DateTimeOffset connectedAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Locks the connection's row until the lock commits or is disposed (rolled back), so a
    /// single-use refresh token is spent once: null when the service has no row.
    /// </summary>
    Task<IIntegrationLock?> LockAsync(string service, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a sync that finished at <paramref name="syncedAt"/>, clearing the last error. The
    /// history only grows: <paramref name="historyFrom"/> moves it earlier, never later, and
    /// once done it stays done. Nothing happens when the service has no row.
    /// </summary>
    Task RecordSyncAsync(
        string service, DateTimeOffset syncedAt, DateOnly? historyFrom, bool historyDone,
        CancellationToken cancellationToken = default);

    /// <summary>Records why the last sync failed, in words; the last good sync stays as it was.</summary>
    Task RecordSyncErrorAsync(string service, string error, CancellationToken cancellationToken = default);
}

/// <summary>
/// A connection locked for a refresh or a disconnect (<see cref="IIntegrationRepository.LockAsync"/>).
/// Changes take effect at <see cref="CommitAsync"/>; disposing without it rolls them back.
/// </summary>
public interface IIntegrationLock : IAsyncDisposable
{
    /// <summary>The stored tokens, or null when there are none or they can't be read.</summary>
    OAuthTokens? Tokens { get; }

    /// <summary>Tokens are stored but can't be decrypted with this host's token key.</summary>
    bool TokensUnreadable { get; }

    bool NeedsReconnect { get; }

    /// <summary>Replaces the stored tokens with a refreshed pair.</summary>
    Task SaveTokensAsync(OAuthTokens tokens, CancellationToken cancellationToken = default);

    /// <summary>Marks the connection as needing to reconnect, saying why.</summary>
    Task MarkReconnectAsync(string reason, CancellationToken cancellationToken = default);

    /// <summary>Forgets the tokens and any connect in progress (a disconnect). What was synced stays.</summary>
    Task ClearTokensAsync(CancellationToken cancellationToken = default);

    Task CommitAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The newest version LifeOS holds of one Oura record (D14): what identifies it, its day, when
/// it happened, its content hash (from its <c>external_id</c>), and whether it's voided.
/// </summary>
public sealed record OuraHead(string Key, DateOnly? Day, DateTimeOffset OccurredAt, string Hash, Guid EventId, bool Voided);

/// <summary>
/// A version of an Oura record to append: an <c>observed</c> event from source <c>oura</c>,
/// stamped with the database's clock as it's written, under the sync lock, so versions written
/// from different hosts order correctly.
/// </summary>
public sealed record OuraVersion(string Kind, DateTimeOffset OccurredAt, string ExternalId, string PayloadJson);

/// <summary>
/// Oura's records and Chris's workout notes as LifeOS holds them (ACTIVITY-1..4, D14, migration
/// 0037): versioned observed events, folded by <c>bsk.v_activity_day</c>, <c>bsk.v_workout</c>,
/// <c>bsk.v_sleep_period</c>, <c>bsk.v_sleep_day</c>, <c>bsk.v_readiness_day</c> and, since
/// migration 0039, <c>bsk.v_oura_tag</c>. Days are Oura's own.
/// </summary>
public interface IActivityRepository
{
    /// <summary>
    /// Starts writing a sync's versions under a lock every sync shares, so two syncs (the API
    /// and the CLI, say) take turns: the second sees what the first wrote.
    /// </summary>
    Task<IOuraSyncLock> BeginSyncAsync(CancellationToken cancellationToken = default);

    /// <summary>Days of activity in [<paramref name="from"/>, <paramref name="to"/>], oldest first.</summary>
    Task<IReadOnlyList<ActivityDay>> GetActivityDaysAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>Live workouts on the days [<paramref name="from"/>, <paramref name="to"/>], newest first.</summary>
    Task<IReadOnlyList<Workout>> GetWorkoutsAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>The live workout with that Oura id, or null.</summary>
    Task<Workout?> GetWorkoutAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Live workouts whose Oura id starts with <paramref name="prefix"/>, newest first.</summary>
    Task<IReadOnlyList<Workout>> FindWorkoutsAsync(string prefix, CancellationToken cancellationToken = default);

    /// <summary>Days with a sleep score or a sleep period in [<paramref name="from"/>, <paramref name="to"/>], oldest first.</summary>
    Task<IReadOnlyList<SleepDay>> GetSleepDaysAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>Days with a readiness score in [<paramref name="from"/>, <paramref name="to"/>], oldest first.</summary>
    Task<IReadOnlyList<ReadinessDay>> GetReadinessDaysAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>Live sleep periods counting on the days [<paramref name="from"/>, <paramref name="to"/>], earliest first.</summary>
    Task<IReadOnlyList<SleepPeriod>> GetSleepPeriodsAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>Live tags starting on the days [<paramref name="from"/>, <paramref name="to"/>], newest first.</summary>
    Task<IReadOnlyList<ActivityTag>> GetTagsAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}

/// <summary>
/// A sync's writes, under the lock every sync shares (<see cref="IActivityRepository.BeginSyncAsync"/>).
/// They take effect at <see cref="CommitAsync"/>; disposing without it rolls them back.
/// </summary>
public interface IOuraSyncLock : IAsyncDisposable
{
    /// <summary>
    /// The newest version of each record in <paramref name="collection"/> that is one of
    /// <paramref name="keys"/>, or whose newest version's day is in [<paramref name="from"/>,
    /// <paramref name="to"/>].
    /// </summary>
    Task<IReadOnlyList<OuraHead>> GetHeadsAsync(
        OuraCollection collection, IReadOnlyCollection<string> keys, DateOnly from, DateOnly to,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends the versions; one whose <c>external_id</c> is already held is skipped. Answers
    /// how many were written.
    /// </summary>
    Task<int> AppendAsync(IReadOnlyList<OuraVersion> versions, CancellationToken cancellationToken = default);

    Task CommitAsync(CancellationToken cancellationToken = default);
}
