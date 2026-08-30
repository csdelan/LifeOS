-- title: Drift — Projects serving no Goal
--
-- A Project is drifting when it serves no Goal: work is happening, but it is not
-- attached to anything you've said you're trying to achieve. Tasks are excluded
-- by construction — a standalone Task legitimately serves nothing (a Task is a
-- leaf, §5); only Projects are held to "should serve a Goal".
--
-- A Project that serves a Value or a Commitment but no Goal is still drift by the
-- letter of this rule — the evidence shows what it does serve, so the reason is
-- visible. Status is not considered here: the rule is about the shape of the
-- alignment graph, not whether the Project is active (that qualifier is on the
-- Wishes side).
--
-- Evidence is the absent/expected relation (a Project expects to serve a Goal),
-- plus whatever the Project does serve, so a reader sees the difference between
-- "serves nothing" and "serves something, but not a Goal".

SELECT
    p.id    AS subject_id,
    p.urn   AS subject_urn,
    p.type  AS subject_type,
    p.title AS subject_title,
    CASE
        WHEN EXISTS (
            SELECT 1 FROM bsk.subject_relation r
            WHERE r.relation = 'serves' AND r.from_subject = p.id
        )
        THEN 'This Project serves no Goal (it serves other things, but no Goal).'
        ELSE 'This Project serves nothing.'
    END AS summary,
    jsonb_build_array(
        jsonb_build_object(
            'kind', 'expected_relation',
            'relation', 'serves',
            'expected_to_type', 'Goal',
            'note', 'a Project is expected to serve a Goal'
        )
    )
    || coalesce((
        SELECT jsonb_agg(
            jsonb_build_object(
                'kind', 'subject',
                'id', t.id,
                'subject_type', t.type,
                'urn', t.urn,
                'relation', 'serves'
            ) ORDER BY t.urn)
        FROM bsk.subject_relation r
        JOIN bsk.subject t ON t.id = r.to_subject
        WHERE r.relation = 'serves' AND r.from_subject = p.id
    ), '[]'::jsonb) AS evidence
FROM bsk.subject p
WHERE p.type = 'Project'
  AND NOT EXISTS (
      SELECT 1
      FROM bsk.subject_relation r
      JOIN bsk.subject g ON g.id = r.to_subject AND g.type = 'Goal'
      WHERE r.relation = 'serves' AND r.from_subject = p.id
  )
ORDER BY p.urn;
