-- title: Wishes — Goals with no active Project serving them
--
-- A Goal is a wish when nothing is actually being done to reach it: no active
-- Project `serves` it. A Goal served only by finished Projects (done, abandoned,
-- …) is still a wish — the work that served it is over. "Active" is defined by
-- bsk.is_terminal_status (migration 0008); a Project with no recorded status
-- counts as active.
--
-- Status is read from bsk_derived.subject_current_source, which recomputes the
-- current status straight from the source event stream, so this does not depend
-- on `bsk rebuild` having run.
--
-- Evidence is the absent/expected relation (a Goal expects a serving active
-- Project), plus any finished Projects that serve it — so the finding shows
-- whether the Goal has no Project at all, or only spent ones.

SELECT
    g.id    AS subject_id,
    g.urn   AS subject_urn,
    g.type  AS subject_type,
    g.title AS subject_title,
    CASE
        WHEN EXISTS (
            SELECT 1
            FROM bsk.subject_relation r
            JOIN bsk.subject p ON p.id = r.from_subject AND p.type = 'Project'
            WHERE r.relation = 'serves' AND r.to_subject = g.id
        )
        THEN 'No active Project serves this Goal — the Project(s) that do are finished.'
        ELSE 'No Project serves this Goal.'
    END AS summary,
    -- The expected-but-absent relation, then any finished serving Projects as context.
    jsonb_build_array(
        jsonb_build_object(
            'kind', 'expected_relation',
            'relation', 'serves',
            'expected_from_type', 'Project',
            'note', 'a Goal expects an active Project serving it'
        )
    )
    || coalesce((
        SELECT jsonb_agg(
            jsonb_build_object(
                'kind', 'subject',
                'id', p.id,
                'subject_type', 'Project',
                'urn', p.urn,
                'relation', 'serves',
                'status', scs.status
            ) ORDER BY p.urn)
        FROM bsk.subject_relation r
        JOIN bsk.subject p ON p.id = r.from_subject AND p.type = 'Project'
        LEFT JOIN bsk_derived.subject_current_source scs ON scs.subject_id = p.id
        WHERE r.relation = 'serves'
          AND r.to_subject = g.id
          AND bsk.is_terminal_status(scs.status)
    ), '[]'::jsonb) AS evidence
FROM bsk.subject g
WHERE g.type = 'Goal'
  AND NOT EXISTS (
      SELECT 1
      FROM bsk.subject_relation r
      JOIN bsk.subject p ON p.id = r.from_subject AND p.type = 'Project'
      LEFT JOIN bsk_derived.subject_current_source scs ON scs.subject_id = p.id
      WHERE r.relation = 'serves'
        AND r.to_subject = g.id
        AND NOT bsk.is_terminal_status(scs.status)
  )
ORDER BY g.urn;
