-- title: Neglect — cadenced subjects with no concerning activity within their window
--
-- Staleness detection: "nothing has happened here in a while." Any subject that
-- declares an expected_cadence but has had no `concerns`-ing event within that
-- window is neglected. The clock starts at the last concerning event, or at the
-- subject's creation when it has never been concerned, so a freshly-created
-- subject is not flagged on day one.
--
-- Season awareness: when a Season is active (its `ends` is absent or not yet
-- past), a subject whose `focus` tag does not match any active Season's focus is
-- legitimately parked — dormancy is expected, not neglect — and is excluded.
-- Subjects with no focus tag are never parked; with no active Season, nothing is
-- parked.
--
-- Finished subjects are excluded: a subject whose current status is terminal
-- (bsk.is_terminal_status — done, abandoned, …) has nothing left to tend, so its
-- silence is not neglect. Status is folded from source, so no rebuild is needed.
--
-- This is information, not confrontation: it reports what has gone quiet, not a
-- rule you broke (that is the breach diagnostic). Cadence words are mapped to
-- windows below; an explicit interval like "10 days" is also accepted, and an
-- uninterpretable cadence is skipped rather than guessed at.

WITH active_season AS (
    SELECT DISTINCT lower(trim(s.attributes->>'focus')) AS focus
    FROM bsk.subject s
    WHERE s.type = 'Season'
      AND coalesce(trim(s.attributes->>'focus'), '') <> ''
      AND (
          s.attributes->>'ends' IS NULL
          -- An unparseable end date is treated as open-ended (still active).
          OR s.attributes->>'ends' !~ '^\d{4}-\d{2}-\d{2}'
          OR (s.attributes->>'ends')::date >= current_date
      )
),
cadenced AS (
    SELECT
        s.id,
        s.urn,
        s.type,
        s.title,
        s.created_at,
        trim(s.attributes->>'expected_cadence')      AS cadence_text,
        nullif(lower(trim(s.attributes->>'focus')), '') AS focus,
        CASE lower(trim(s.attributes->>'expected_cadence'))
            WHEN 'daily'       THEN interval '1 day'
            WHEN 'weekly'      THEN interval '7 days'
            WHEN 'fortnightly' THEN interval '14 days'
            WHEN 'biweekly'    THEN interval '14 days'
            WHEN 'monthly'     THEN interval '1 month'
            WHEN 'quarterly'   THEN interval '3 months'
            WHEN 'yearly'      THEN interval '1 year'
            WHEN 'annually'    THEN interval '1 year'
            -- Explicit Postgres-style intervals ("10 days", "2 weeks") pass through;
            -- anything else yields NULL and is excluded (its window is unknowable).
            ELSE CASE
                WHEN s.attributes->>'expected_cadence'
                     ~* '^\s*\d+\s+(hour|hours|day|days|week|weeks|month|months|year|years)\s*$'
                THEN (s.attributes->>'expected_cadence')::interval
                ELSE NULL
            END
        END AS window
    FROM bsk.subject s
    -- Current status folded from source (no rebuild needed), to skip finished subjects.
    LEFT JOIN bsk_derived.subject_current_source scs ON scs.subject_id = s.id
    WHERE jsonb_exists(s.attributes, 'expected_cadence')
      AND coalesce(trim(s.attributes->>'expected_cadence'), '') <> ''
      -- A done/abandoned/… subject is not neglected — there is nothing left to tend.
      -- (bsk.is_terminal_status, migration 0008; NULL status counts as active.)
      AND NOT bsk.is_terminal_status(scs.status)
),
-- The single most recent concerning event per subject, for the staleness clock
-- and the evidence row.
last_concern AS (
    SELECT DISTINCT ON (se.subject_id)
        se.subject_id,
        se.event_id,
        e.occurred_at
    FROM bsk.subject_event se
    JOIN bsk.event e ON e.id = se.event_id
    WHERE se.relation = 'concerns'
    ORDER BY se.subject_id, e.occurred_at DESC, e.recorded_at DESC, e.id DESC
),
neglected AS (
    SELECT
        c.*,
        lc.event_id      AS last_event_id,
        lc.occurred_at   AS last_occurred_at,
        coalesce(lc.occurred_at, c.created_at) AS since,
        floor(extract(epoch FROM now() - coalesce(lc.occurred_at, c.created_at)) / 86400)::int AS elapsed_days
    FROM cadenced c
    LEFT JOIN last_concern lc ON lc.subject_id = c.id
    WHERE c.window IS NOT NULL
      AND now() - coalesce(lc.occurred_at, c.created_at) > c.window
      -- Season parking: exclude out-of-focus subjects while a Season is active.
      AND NOT (
          c.focus IS NOT NULL
          AND EXISTS (SELECT 1 FROM active_season)
          AND NOT EXISTS (SELECT 1 FROM active_season a WHERE a.focus = c.focus)
      )
)
SELECT
    n.id    AS subject_id,
    n.urn   AS subject_urn,
    n.type  AS subject_type,
    n.title AS subject_title,
    CASE
        WHEN n.last_event_id IS NULL THEN
            format('No concerning activity in %s day(s) since it was created (cadence: %s).',
                   n.elapsed_days, n.cadence_text)
        ELSE
            format('No concerning activity in %s day(s), past its %s cadence.',
                   n.elapsed_days, n.cadence_text)
    END AS summary,
    -- Evidence: the window that was exceeded, plus the last concerning event if any.
    jsonb_build_array(
        jsonb_build_object(
            'kind', 'window',
            'cadence', n.cadence_text,
            'window', n.window::text,
            'elapsed_days', n.elapsed_days,
            'since', to_char(n.since AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"'),
            'since_kind', CASE WHEN n.last_event_id IS NULL THEN 'created_at' ELSE 'last_concern' END
        )
    )
    || CASE
        WHEN n.last_event_id IS NOT NULL THEN
            jsonb_build_array(jsonb_build_object(
                'kind', 'event',
                'id', n.last_event_id,
                'relation', 'concerns',
                'occurred_at', to_char(n.last_occurred_at AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"')
            ))
        ELSE '[]'::jsonb
    END AS evidence
FROM neglected n
ORDER BY n.elapsed_days DESC, n.urn;
