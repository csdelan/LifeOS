-- title: Drift — Projects serving no Goal
--
-- A Project is drifting when it works toward no Goal: work is happening, but it is not
-- attached to anything you've said you're trying to achieve. A Project works toward a Goal
-- by `results_in`, the link LifeOS makes when a Project is created under its Goal (GEN-7,
-- ParentChildRelations), or by `serves`; the alignment tree follows both, and so does this.
-- Tasks are excluded by construction — a standalone Task legitimately serves nothing (a
-- Task is a leaf, §5); only Projects are held to "should serve a Goal".
--
-- A Project that serves a Value or a Commitment but no Goal is still drift by the
-- letter of this rule — the evidence shows what it does serve, so the reason is
-- visible. Status is not considered here: the rule is about the shape of the
-- alignment graph, not whether the Project is active (that qualifier is on the
-- Wishes side).
--
-- Evidence is the absent/expected relation (a Project expects to result in a Goal),
-- plus whatever the Project does serve or result in, so a reader sees the difference
-- between "serves nothing" and "serves something, but not a Goal".

WITH up AS (
    -- Each Project's alignment edges upward, by either relation.
    SELECT r.from_subject AS project_id, r.relation, t.id, t.type, t.urn
    FROM bsk.subject_relation r
    JOIN bsk.subject t ON t.id = r.to_subject
    WHERE r.relation IN ('serves', 'results_in')
)
SELECT
    p.id    AS subject_id,
    p.urn   AS subject_urn,
    p.type  AS subject_type,
    p.title AS subject_title,
    CASE
        WHEN EXISTS (SELECT 1 FROM up WHERE up.project_id = p.id)
        THEN 'This Project serves no Goal (it serves other things, but no Goal).'
        ELSE 'This Project serves nothing.'
    END AS summary,
    jsonb_build_array(
        jsonb_build_object(
            'kind', 'expected_relation',
            'relation', 'results_in',
            'expected_to_type', 'Goal',
            'note', 'a Project is expected to result in (or serve) a Goal'
        )
    )
    || coalesce((
        SELECT jsonb_agg(
            jsonb_build_object(
                'kind', 'subject',
                'id', up.id,
                'subject_type', up.type,
                'urn', up.urn,
                'relation', up.relation
            ) ORDER BY up.urn, up.relation)
        FROM up
        WHERE up.project_id = p.id
    ), '[]'::jsonb) AS evidence
FROM bsk.subject p
WHERE p.type = 'Project'
  -- Archived subjects are hidden from every default view (D9, migration 0012).
  AND NOT bsk.is_archived(p.id)
  AND NOT EXISTS (
      SELECT 1 FROM up
      WHERE up.project_id = p.id AND up.type = 'Goal'
  )
ORDER BY p.urn;
