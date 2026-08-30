-- 0008__terminal_status.sql
-- The terminal-status vocabulary, as one reusable predicate. A subject's status
-- is free text (a state_change payload carries any string); "terminal" is the
-- set of statuses that mean the subject is finished or dead and should no longer
-- count as active. Defined once here so every diagnostic that needs "active vs
-- done" agrees — the Wishes diagnostic's "active Project" (M4.4), and later the
-- neglect done-status exclusion (#25) and unclosed-loops (M4.5).
--
-- Matching is case-insensitive and trims whitespace. A NULL or empty status is
-- NOT terminal — a subject with no recorded status is treated as active.
--
-- The set is intentionally editable: a later migration can CREATE OR REPLACE this
-- function to add or remove a word as real status vocabulary emerges in live use.

-- Not STRICT: it must run on a NULL status and return false (a subject with no
-- recorded status is active). coalesce folds NULL to '' so it falls outside the set.
CREATE OR REPLACE FUNCTION bsk.is_terminal_status(status text)
    RETURNS boolean
    LANGUAGE sql
    IMMUTABLE
    PARALLEL SAFE
AS $$
    SELECT lower(trim(coalesce(status, ''))) IN (
        'done', 'completed', 'resolved', 'closed', 'cancelled',
        'abandoned', 'dropped', 'archived', 'superseded'
    );
$$;

COMMENT ON FUNCTION bsk.is_terminal_status(text) IS
    'True when a status string means the subject is finished/dead (done, completed, resolved, closed, cancelled, abandoned, dropped, archived, superseded). NULL status is active, not terminal.';

-- Let the read-only door use it too, for ad-hoc "is this still active?" queries.
GRANT EXECUTE ON FUNCTION bsk.is_terminal_status(text) TO bsk_reader;
