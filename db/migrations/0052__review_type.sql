-- 0052__review_type.sql
-- REVIEW-0 / REVIEW-1 / REVIEW-2, D3 + D8 (reviews plan §2, phase R1): daily and weekly reviews.
--
--   * A Review is the 18th subject type. attributes.kind is 'daily' or 'weekly';
--     attributes.period is the day a daily review is for, or the Sunday that names a weekly one
--     (yyyy-MM-dd; the week is the Monday to Sunday ending that day). Both are written only by
--     ReviewService when the review is opened. attributes.keep_focus (true unless false) is the
--     review's "keep my Primary focus" choice.
--   * One review per kind and period: the unique index below makes opening it twice, or from two
--     tabs at once, land on the same subject.
--   * Status moves by state_change like any subject: Open until completed. Missed is worked out on
--     every read in C# (Review.StatusAsOf), from the day and when it was first completed, so
--     nothing writes it on a schedule; it can also be set by hand. first_completed_at stays put
--     when a review is completed late or reopened, so a late completion never hides the miss
--     (REVIEW-2).
--   * The answers are not here. Where they live is reviews plan §4.

-- ---------------------------------------------------------------------------
-- 1. Admit the Review subject type
-- ---------------------------------------------------------------------------
ALTER TABLE bsk.subject DROP CONSTRAINT IF EXISTS subject_type_check;

ALTER TABLE bsk.subject
    ADD CONSTRAINT subject_type_check
    CHECK (type IN (
        'Value', 'Goal', 'Problem', 'Project', 'Task', 'Commitment',
        'Decision', 'Idea', 'Person', 'Constraint', 'Season', 'Area', 'Habit', 'Appointment',
        'Reward', 'Space', 'Metric', 'Review'));

-- ---------------------------------------------------------------------------
-- 2. One review per kind and period
-- ---------------------------------------------------------------------------
CREATE UNIQUE INDEX IF NOT EXISTS subject_review_period_key
    ON bsk.subject ((attributes->>'kind'), (attributes->>'period'))
    WHERE type = 'Review';

-- ---------------------------------------------------------------------------
-- 3. Reviews
-- ---------------------------------------------------------------------------
-- A review whose kind or period doesn't parse is left out. status is the folded stored status
-- (null when none was set, which reads as Open). completed_at is when the current Completed was
-- set (null unless it's Completed now); first_completed_at is the first Completed ever, and
-- first_completed_on that instant's day in the home zone.
CREATE OR REPLACE VIEW bsk.v_review AS
SELECT
    r.id,
    r.urn,
    r.title,
    r.attributes->>'kind'                                              AS kind,
    bsk.try_to_date(r.attributes->>'period')                           AS period,
    scs.status,
    (r.attributes->>'keep_focus') IS DISTINCT FROM 'false'             AS keep_focus,
    CASE WHEN lower(scs.status) = 'completed' THEN scs.status_occurred_at END AS completed_at,
    fc.first_completed_at,
    (fc.first_completed_at AT TIME ZONE bsk.home_zone())::date         AS first_completed_on,
    bsk.is_archived(r.id)                                              AS archived,
    r.created_at
FROM bsk.subject r
LEFT JOIN bsk_derived.subject_current_source scs ON scs.subject_id = r.id
LEFT JOIN LATERAL (
    SELECT min(e.occurred_at) AS first_completed_at
    FROM bsk.event e
    WHERE e.kind = 'state_change'
      AND e.payload->>'subject_id' = r.id::text
      AND lower(e.payload->>'status') = 'completed'
) fc ON true
WHERE r.type = 'Review'
  AND r.attributes->>'kind' IN ('daily', 'weekly')
  AND bsk.try_to_date(r.attributes->>'period') IS NOT NULL;

COMMENT ON VIEW bsk.v_review IS
    'REVIEW-1/REVIEW-2 (D3, D8): one row per Review — its kind (daily, weekly), its period (the day, or the Sunday naming the Monday-to-Sunday week), the folded status, keep_focus, when the current Completed was set and when it was first completed (first_completed_on in the home zone). Missed is computed from these on read.';

GRANT SELECT ON bsk.v_review TO bsk_reader;
