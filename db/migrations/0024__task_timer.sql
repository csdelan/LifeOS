-- 0024__task_timer.sql
-- TASKS-2: an optional, Toggl-style timer on a Task. Nothing is stored about a timer
-- except the append-only actions taken on it: each Start / Pause / Resume / Stop is
-- an `activity` event whose payload is {subject_id, timer: start|pause|resume|stop},
-- with a `concerns` edge to the Task (so timed work also counts as touching it). The
-- timer's state and the time it has recorded are folded here, read-only — the same
-- shape as the habit projection (0018). A task that was never timed has no row.
--
-- Time is the sum of the running segments: every start / resume runs until the next
-- timer action on the same task. A segment still running (the newest action is a
-- start or resume) is NOT in total_seconds; readers add (now - running_since) so the
-- clock ticks without a write. A session runs from a start to its stop and may hold
-- several segments separated by pauses; total_seconds spans every session.
--
-- The fold never double-counts a task's time, even if two starts land back to back
-- (a double-click race): the first segment simply ends where the second begins.

CREATE OR REPLACE VIEW bsk.v_task_timer AS
WITH timer_event AS (
    SELECT
        (e.payload->>'subject_id')::uuid AS subject_id,
        e.payload->>'timer'              AS action,
        e.occurred_at,
        e.recorded_at,
        e.id
    FROM bsk.event e
    WHERE e.kind = 'activity'
      AND jsonb_exists(e.payload, 'timer')
),
sequenced AS (
    SELECT
        t.subject_id,
        t.action,
        t.occurred_at,
        t.recorded_at,
        t.id,
        lead(t.occurred_at) OVER w                        AS next_at,
        -- Running count of starts: which session each action belongs to.
        count(*) FILTER (WHERE t.action = 'start') OVER w AS session_no,
        row_number() OVER (
            PARTITION BY t.subject_id
            ORDER BY t.occurred_at DESC, t.recorded_at DESC, t.id DESC) AS rn_desc
    FROM timer_event t
    -- Deterministic order, tie-broken like the status / archive / triage folds.
    WINDOW w AS (PARTITION BY t.subject_id ORDER BY t.occurred_at, t.recorded_at, t.id)
),
segment AS (
    SELECT subject_id, session_no, extract(epoch FROM next_at - occurred_at) AS seconds
    FROM sequenced
    WHERE action IN ('start', 'resume')
      AND next_at IS NOT NULL
),
totals AS (
    SELECT subject_id, sum(seconds) AS seconds
    FROM segment
    GROUP BY subject_id
),
latest AS (
    SELECT subject_id, action, occurred_at, session_no
    FROM sequenced
    WHERE rn_desc = 1
),
session_start AS (
    SELECT DISTINCT ON (subject_id) subject_id, occurred_at
    FROM sequenced
    WHERE action = 'start'
    ORDER BY subject_id, occurred_at DESC, recorded_at DESC, id DESC
)
SELECT
    l.subject_id,
    s.urn                                              AS subject_urn,
    s.type                                             AS subject_type,
    s.title                                            AS subject_title,
    bsk.is_archived(s.id)                              AS archived,
    CASE l.action
        WHEN 'start'  THEN 'running'
        WHEN 'resume' THEN 'running'
        WHEN 'pause'  THEN 'paused'
        ELSE 'stopped'
    END                                                AS state,
    CASE WHEN l.action IN ('start', 'resume') THEN l.occurred_at END AS running_since,
    floor(coalesce(tot.seconds, 0))::bigint            AS total_seconds,
    -- Closed time in the newest session (the one a stop ended, when stopped).
    floor(coalesce((
        SELECT sum(g.seconds) FROM segment g
        WHERE g.subject_id = l.subject_id AND g.session_no = l.session_no), 0))::bigint
                                                       AS session_seconds,
    ss.occurred_at                                     AS session_started_at,
    l.session_no                                       AS sessions,
    l.occurred_at                                      AS last_changed_at
FROM latest l
JOIN bsk.subject s ON s.id = l.subject_id
LEFT JOIN totals tot ON tot.subject_id = l.subject_id
LEFT JOIN session_start ss ON ss.subject_id = l.subject_id;

COMMENT ON VIEW bsk.v_task_timer IS
    'TASKS-2: per-task timer state (running / paused / stopped) and recorded time, folded from append-only timer activity events. total_seconds excludes a still-running segment; add now() - running_since.';

-- Timer reads filter on these events constantly (every task detail, the running-timer
-- indicator); keep them off a full scan of the activity stream.
CREATE INDEX IF NOT EXISTS event_timer_subject_idx
    ON bsk.event ((payload->>'subject_id'), occurred_at)
    WHERE kind = 'activity' AND jsonb_exists(payload, 'timer');

GRANT SELECT ON bsk.v_task_timer TO bsk_reader;
