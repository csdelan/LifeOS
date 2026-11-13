-- 0065__habit_allows_partial_text.sql
-- Code review CR70: v_habit reads a habit's allows_partial as the kernel does, and never fails.
--
-- v_habit cast attributes->>'allows_partial' to boolean, so one habit whose flag was any other text
-- ("maybe", "yes ", "") made every read of the view raise: the Habits page, /mcp habits and
-- life_snapshot. And the cast took "yes", "on", "1" and "t" as true, while AdherenceService allows
-- partial credit only for "true", so the page could show a habit as taking partial credit that the
-- kernel refused it. The view now says what the kernel says (AdherenceResults.PartialAllowed): the
-- text "true", in any case, spaces trimmed; anything else, or no flag, is false.

CREATE OR REPLACE VIEW bsk.v_habit AS
 SELECT id,
    urn,
    title AS name,
    attributes ->> 'cue'::text AS cue,
    attributes ->> 'routine'::text AS routine,
    attributes ->> 'reward'::text AS reward,
    attributes ->> 'start'::text AS start_date,
    attributes ->> 'end'::text AS end_date,
    COALESCE(lower(btrim(attributes ->> 'allows_partial'::text)) = 'true'::text, false) AS allows_partial,
    attributes -> 'recurrence'::text AS recurrence,
    bsk.is_archived(id) AS archived,
    created_at
   FROM bsk.subject s
  WHERE type = 'Habit'::text;
