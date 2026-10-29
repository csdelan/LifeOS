-- 0049__journal_pointer.sql
-- One pointer per saved SuperJournal version (D19, csdelan/LifeOS#28).
--
-- Journal and review text now lives in SuperJournal, and bsk keeps a `journal` event for every
-- saved version: its payload's `superjournal` object says which journal, entry and version, with
-- an excerpt. The event's source is who saved it (`api`, or `mcp:<client_id>` for an agent), so
-- the (source_id, external_id) key collapses a repeated write from the same door, but not one
-- from another: the reconcile step, run by whichever door comes next, writes the pointers a
-- save couldn't. This index keeps one pointer per entry version whoever writes it, so a version
-- can never be counted twice.

CREATE UNIQUE INDEX IF NOT EXISTS event_journal_pointer_key
    ON bsk.event ((payload->'superjournal'->>'entry_id'), (payload->'superjournal'->>'version'))
    WHERE kind = 'journal' AND (payload->'superjournal'->>'entry_id') IS NOT NULL;
