-- title: Decorative identity — Values with no Goal beneath them
--
-- A Value is the top of the alignment graph — an enduring principle, who you've chosen
-- to be. It earns its place only if something is aimed at it: a Goal `serves` a Value
-- (§4.3). A Value with no Goal beneath it is decorative — stated, but nothing you are
-- actually trying to achieve points at it.
--
-- This is the mirror of Wishes one level up: Wishes asks "is this Goal backed by an
-- active Project?"; decorative identity asks "is this Value backed by any Goal at all?".
-- It is a purely structural check on the graph — any Goal serving the Value clears it,
-- whatever that Goal's status. (The active-vs-finished distinction is Wishes' concern,
-- on the work below the Goal; a Value with even a finished Goal beneath it has been
-- aimed at, so it is not merely decorative.)
--
-- The cause is an absence, so there is no row to cite: the evidence names the
-- expected-but-missing relation and the summary carries the whole explanation.

SELECT
    v.id    AS subject_id,
    v.urn   AS subject_urn,
    v.type  AS subject_type,
    v.title AS subject_title,
    'No Goal serves this Value — it is stated, but nothing is aimed at it.' AS summary,
    jsonb_build_array(
        jsonb_build_object(
            'kind', 'expected_relation',
            'relation', 'serves',
            'expected_from_type', 'Goal',
            'note', 'a Value expects a Goal serving it'
        )
    ) AS evidence
FROM bsk.subject v
WHERE v.type = 'Value'
  AND NOT EXISTS (
      SELECT 1
      FROM bsk.subject_relation r
      JOIN bsk.subject g ON g.id = r.from_subject AND g.type = 'Goal'
      WHERE r.relation = 'serves' AND r.to_subject = v.id
  )
ORDER BY v.urn;
