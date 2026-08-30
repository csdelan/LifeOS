-- title: Unclosed loops — Decisions past their review date with no recorded outcome
--
-- A Decision carries a prediction and a next_review_at: the date you promised to come
-- back and judge whether the call was right. The loop is *closed* by recording the
-- outcome — closing the Decision out into a terminal status (bsk.is_terminal_status,
-- migration 0008: done, resolved, closed, superseded, …; a superseded Decision has been
-- reversed, which is an outcome too). Until then, a Decision whose review date has
-- passed is an open loop: a prediction you committed to grade and haven't.
--
-- Not flagged: a Decision whose outcome is recorded (terminal status); one whose review
-- date is still in the future; one with no next_review_at, or one whose value is
-- unparseable (there is no loop to close if you never set a real date). Date
-- granularity: a review due *today* is not yet overdue — it fires the day after.
-- next_review_at is free-text jsonb, so bsk.try_to_date (migration 0009) turns a
-- malformed value into NULL and it is skipped rather than guessed at or aborting the run.
--
-- Status is folded straight from source (bsk_derived.subject_current_source), so this
-- does not depend on `bsk rebuild` having run. The cause is an absence (no outcome), so
-- the evidence cites the concrete fact that fired it — the review date that passed —
-- and the current (non-terminal) status, and the summary carries the explanation.

WITH decisions AS (
    SELECT
        s.id,
        s.urn,
        s.type,
        s.title,
        s.attributes->>'next_review_at'                  AS review_text,
        bsk.try_to_date(s.attributes->>'next_review_at') AS review_date,
        scs.status                                       AS status
    FROM bsk.subject s
    -- Current status folded from source (no rebuild needed), to skip closed-out Decisions.
    LEFT JOIN bsk_derived.subject_current_source scs ON scs.subject_id = s.id
    WHERE s.type = 'Decision'
      AND jsonb_exists(s.attributes, 'next_review_at')
      -- An outcome recorded = the Decision is closed out (terminal status). NULL is active.
      AND NOT bsk.is_terminal_status(scs.status)
)
SELECT
    d.id    AS subject_id,
    d.urn   AS subject_urn,
    d.type  AS subject_type,
    d.title AS subject_title,
    format(
        'Open loop — review was due %s (%s day(s) ago) and no outcome has been recorded.',
        to_char(d.review_date, 'YYYY-MM-DD'),
        (current_date - d.review_date)
    ) AS summary,
    jsonb_build_array(
        jsonb_build_object(
            'kind', 'review',
            'next_review_at', d.review_text,
            'review_date', to_char(d.review_date, 'YYYY-MM-DD'),
            'days_overdue', (current_date - d.review_date),
            'status', d.status
        )
    ) AS evidence
FROM decisions d
WHERE d.review_date IS NOT NULL
  -- Strictly before today: a review due today is not yet overdue; a future date is not a loop.
  AND d.review_date < current_date
ORDER BY d.review_date, d.urn;
