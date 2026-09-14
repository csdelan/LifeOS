-- 0017__habit_type.sql
-- GEN-3 / revised D2: Habit becomes its own subject type (11 -> 13 with Area),
-- rather than being stored as a Commitment. It composes the shared recurrence (D8,
-- attributes.recurrence) and adherence mechanism; its distinct workflow — streak,
-- partial credit, cue/routine/reward, exclusion from the breach report — follows
-- from being a separate type, not from special-casing Commitment.
--
-- All Habit fields are attributes (zero DDL beyond admitting the type): cue,
-- routine, reward, start, end, and allows_partial (whether a partial/half-credit
-- adherence is permitted — binary habits like "brush teeth" stay two-state).
-- Adherence is recorded as activity events (migration comes with the verb); the
-- occurrence + streak projection is the next phase.

DO $$
DECLARE
    check_name text;
BEGIN
    SELECT conname INTO check_name
    FROM pg_constraint
    WHERE conrelid = 'bsk.subject'::regclass
      AND contype = 'c'
      AND pg_get_constraintdef(oid) ILIKE '%Season%';

    IF check_name IS NOT NULL THEN
        EXECUTE format('ALTER TABLE bsk.subject DROP CONSTRAINT %I', check_name);
    END IF;
END
$$;

ALTER TABLE bsk.subject
    ADD CONSTRAINT subject_type_check
    CHECK (type IN (
        'Value', 'Goal', 'Problem', 'Project', 'Task', 'Commitment',
        'Decision', 'Idea', 'Person', 'Constraint', 'Season', 'Area', 'Habit'));

-- A dedicated read for the Habit viewer (GEN-3): the unique fields flattened, plus
-- the structured recurrence and archive flag. Streak/adherence come from the
-- projection built next.
CREATE OR REPLACE VIEW bsk.v_habit AS
SELECT
    s.id,
    s.urn,
    s.title                                          AS name,
    s.attributes->>'cue'                             AS cue,
    s.attributes->>'routine'                         AS routine,
    s.attributes->>'reward'                          AS reward,
    s.attributes->>'start'                           AS start_date,
    s.attributes->>'end'                             AS end_date,
    coalesce((s.attributes->>'allows_partial')::boolean, false) AS allows_partial,
    s.attributes->'recurrence'                       AS recurrence,
    bsk.is_archived(s.id)                            AS archived,
    s.created_at
FROM bsk.subject s
WHERE s.type = 'Habit';

GRANT SELECT ON bsk.v_habit TO bsk_reader;
