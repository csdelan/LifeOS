-- title: Wishes — Goals with no open Project or Task working toward them
--
-- A Goal is a wish when nothing is actually being done to reach it: no open Project or
-- Task works toward it. A Project works toward a Goal by `results_in`, the link LifeOS
-- makes when a Project is created under its Goal (GEN-7, ParentChildRelations), or by
-- `serves`; a Task created right under a Goal `serves` it. The alignment tree follows
-- both relations, and so does this. A Goal worked toward only by finished Projects or
-- Tasks (done, abandoned, …) is still a wish — that work is over. "Open" is defined by
-- bsk.is_terminal_status (migration 0008); an item with no recorded status counts as open.
--
-- Status is read from bsk_derived.subject_current_source, which recomputes the
-- current status straight from the source event stream, so this does not depend
-- on `bsk rebuild` having run.
--
-- Evidence is the absent/expected relation (a Goal expects an open Project resulting in
-- it), plus any finished Projects or Tasks that worked toward it — so the finding shows
-- whether the Goal has nothing at all, or only spent work.

WITH toward AS (
    -- What works toward each Goal: a Project or Task linked to it by either relation, with
    -- its current status.
    SELECT r.to_subject AS goal_id, w.id, w.type, w.urn, r.relation, scs.status
    FROM bsk.subject_relation r
    JOIN bsk.subject w ON w.id = r.from_subject AND w.type IN ('Project', 'Task')
    LEFT JOIN bsk_derived.subject_current_source scs ON scs.subject_id = w.id
    WHERE r.relation IN ('serves', 'results_in')
)
SELECT
    g.id    AS subject_id,
    g.urn   AS subject_urn,
    g.type  AS subject_type,
    g.title AS subject_title,
    CASE
        WHEN EXISTS (SELECT 1 FROM toward t WHERE t.goal_id = g.id)
        THEN 'Nothing open works toward this Goal — the Project(s) or Task(s) that did are finished.'
        ELSE 'No Project or Task works toward this Goal.'
    END AS summary,
    -- The expected-but-absent relation, then any finished work as context.
    jsonb_build_array(
        jsonb_build_object(
            'kind', 'expected_relation',
            'relation', 'results_in',
            'expected_from_type', 'Project',
            'note', 'a Goal expects an open Project resulting in it, or an open Task serving it'
        )
    )
    || coalesce((
        SELECT jsonb_agg(
            jsonb_build_object(
                'kind', 'subject',
                'id', t.id,
                'subject_type', t.type,
                'urn', t.urn,
                'relation', t.relation,
                'status', t.status
            ) ORDER BY t.urn, t.relation)
        FROM toward t
        WHERE t.goal_id = g.id
          AND bsk.is_terminal_status(t.status)
    ), '[]'::jsonb) AS evidence
FROM bsk.subject g
WHERE g.type = 'Goal'
  -- Archived subjects are hidden from every default view (D9, migration 0012).
  AND NOT bsk.is_archived(g.id)
  AND NOT EXISTS (
      SELECT 1 FROM toward t
      WHERE t.goal_id = g.id
        AND NOT bsk.is_terminal_status(t.status)
  )
ORDER BY g.urn;
