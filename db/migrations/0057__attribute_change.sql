-- 0057__attribute_change.sql
-- Code review CR24: a rename or an attribute change leaves an event.
--
-- A subject's title and attributes are updated in place, and until now nothing recorded when.
-- The habit views rebuild the past from the current recurrence, start and target, so changing a
-- habit from daily to weekdays changed its past too, with no trace of the change. Now the write
-- that changes them appends an `attribute_change` event in the same transaction, linked to the
-- subject (`concerns`), with what changed, from and to:
--
--   {"subject_id": "...", "title": {"from": "Run", "to": "Run 5k"}}
--   {"subject_id": "...", "attributes": {"recurrence": {"from": {...}, "to": {...}},
--                                        "cover": {"from": {...}}}}
--
-- A key that was added has no "from"; one that was removed has no "to". A write that changes
-- nothing appends nothing. Current reads don't change: the views still read the subject row.
-- The events make "what was this on a date" answerable later.

ALTER TABLE bsk.event DROP CONSTRAINT event_kind_check;

ALTER TABLE bsk.event
    ADD CONSTRAINT event_kind_check
    CHECK (kind IN (
        'journal', 'note', 'voice', 'idea_session', 'observation',
        'activity', 'measurement', 'interaction', 'state_change', 'archive_change', 'triage',
        'ledger', 'attribute_change'));
