-- 0061__ledger_read_indexes.sql
-- Code review CR34: indexes for the ledger reads that scanned every ledger entry.
--
--   * By subject. bsk.v_ledger's subject_id is the subject joined through
--     NULLIF(payload->>'subject_id', '')::uuid, so "WHERE subject_id = @id" (a subject's paid
--     awards, read on every reconcile) filters on that uuid expression. event_ledger_subject_idx
--     (0028) is on the text payload->>'subject_id', which the planner can't match to it, so it
--     read every ledger entry. This index is on the view's own expression. The text index stays:
--     the views that compare the text use it.
--   * By trigger. Every earning write and Character page asks which rank-up awards are paid, and
--     the food and activity settles read their award keys, by payload->>'trigger'. No index
--     covered it.
--
-- Every ledger entry the kernel wrote names its subject by a uuid or not at all, or v_ledger,
-- which casts every one, would fail to read. The log is small enough today that a plain
-- CREATE INDEX is fine inside the migration's transaction (as in 0056).

CREATE INDEX IF NOT EXISTS event_ledger_subject_uuid_idx
    ON bsk.event ((NULLIF(payload->>'subject_id', '')::uuid))
    WHERE kind = 'ledger';

CREATE INDEX IF NOT EXISTS event_ledger_trigger_idx
    ON bsk.event ((payload->>'trigger'))
    WHERE kind = 'ledger';
