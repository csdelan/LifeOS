-- 0068__relation_edge_key.sql
-- Code review CR111 (fifth review R4): an alignment edge is stored once.
--
-- bsk.subject_relation had no unique key, and nothing checked for an edge already there, so a
-- retried or second link (bsk link, POST /api/link, the web's edit form) stored the edge again.
-- Each new repetition of a repeating Task copied the duplicate, the serves lists showed it twice,
-- and an unlink removed both. The table is the mutable layer (an unlink deletes its row), so the
-- duplicates go: the oldest of each is kept, by created_at and then id. Then a unique index on
-- (from_subject, relation, to_subject) keeps it that way, and a link that finds its edge answers
-- that one (NpgsqlRelationRepository).
--
-- The index's (from_subject, relation) prefix serves every lookup subject_relation_from served,
-- so that index goes.

DELETE FROM bsk.subject_relation r
USING bsk.subject_relation kept
WHERE kept.from_subject = r.from_subject
  AND kept.relation = r.relation
  AND kept.to_subject = r.to_subject
  AND (kept.created_at, kept.id) < (r.created_at, r.id);

CREATE UNIQUE INDEX IF NOT EXISTS subject_relation_edge_key
    ON bsk.subject_relation (from_subject, relation, to_subject);

DROP INDEX IF EXISTS bsk.subject_relation_from;
