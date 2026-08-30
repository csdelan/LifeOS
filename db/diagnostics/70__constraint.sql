-- title: Constraint check — capacity Constraints exceeded by reality
--
-- Subtraction requires a stated limit. A Constraint with scope=capacity declares a
-- ceiling on how much you can carry; this checks the ceiling against what is actually
-- on your plate. Two capacity dimensions are understood, read from the limit's units
-- (the same "interpret the free-text attribute, skip what you can't" approach the
-- neglect diagnostic takes with cadence):
--
--   * projects — a limit mentioning "project" (e.g. "2 active projects"). Observed is
--     the count of active (non-terminal) Projects; the offending subjects are those
--     Projects.
--   * hours — a limit mentioning "hour" (e.g. "20 focused hours"). The limit states the
--     available focused hours; observed is the sum of the `committed_hours` attribute
--     over active (non-terminal) subjects that declare it; the offending subjects are
--     those that booked the hours. (`committed_hours` is a plain jsonb attribute — set
--     it with `bsk new … --attr committed_hours=5`; nothing else in the kernel writes it.)
--
-- The limit's number is the first integer in the text; a limit with no number, or units
-- that are neither projects nor hours, is uninterpretable and skipped rather than
-- guessed at. Only scope=capacity is counted: scope=interaction Constraints are policy,
-- enforced on the surfacing path later, and must not read as capacity violations here.
-- A finding fires only when observed strictly exceeds the limit, and cites the limit vs.
-- the observed value plus every contributing subject.
--
-- Status is folded from source (bsk_derived.subject_current_source), so "active" needs
-- no `bsk rebuild`.

WITH capacity_constraints AS (
    SELECT
        s.id,
        s.urn,
        s.type,
        s.title,
        trim(s.attributes->>'limit')                     AS limit_text,
        (regexp_match(s.attributes->>'limit', '\d+'))[1]::int AS limit_value,
        -- Dimension from the limit's units. Check "hour" before "project" so a mixed
        -- phrase resolves deterministically; neither present => uninterpretable (NULL).
        CASE
            WHEN s.attributes->>'limit' ~* 'hour'    THEN 'hours'
            WHEN s.attributes->>'limit' ~* 'project' THEN 'projects'
            ELSE NULL
        END AS dimension
    FROM bsk.subject s
    WHERE s.type = 'Constraint'
      -- Only capacity limits are counted; interaction constraints are policy, not capacity.
      AND lower(trim(s.attributes->>'scope')) = 'capacity'
      AND coalesce(trim(s.attributes->>'limit'), '') <> ''
),
-- Active Projects: the pool the "projects" dimension counts.
active_projects AS (
    SELECT s.id, s.urn, s.type, s.title
    FROM bsk.subject s
    LEFT JOIN bsk_derived.subject_current_source scs ON scs.subject_id = s.id
    WHERE s.type = 'Project'
      AND NOT bsk.is_terminal_status(scs.status)
),
-- Active subjects that have booked focused hours: the pool the "hours" dimension sums.
-- Guard the numeric cast with a regex so a malformed committed_hours is ignored, not fatal.
committed_hours AS (
    SELECT s.id, s.urn, s.type, s.title,
           (s.attributes->>'committed_hours')::numeric AS hours
    FROM bsk.subject s
    LEFT JOIN bsk_derived.subject_current_source scs ON scs.subject_id = s.id
    WHERE NOT bsk.is_terminal_status(scs.status)
      AND s.attributes->>'committed_hours' ~ '^\s*\d+(\.\d+)?\s*$'
),
observed AS (
    SELECT
        c.*,
        CASE c.dimension
            WHEN 'projects' THEN (SELECT count(*) FROM active_projects)::numeric
            WHEN 'hours'    THEN coalesce((SELECT sum(hours) FROM committed_hours), 0)
        END AS observed_value
    FROM capacity_constraints c
    WHERE c.dimension IS NOT NULL   -- units we understand
      AND c.limit_value IS NOT NULL -- a number to compare against
)
SELECT
    o.id    AS subject_id,
    o.urn   AS subject_urn,
    o.type  AS subject_type,
    o.title AS subject_title,
    CASE o.dimension
        WHEN 'projects' THEN format(
            'Over capacity — %s active Project(s) against a limit of %s.',
            o.observed_value, o.limit_value)
        WHEN 'hours' THEN format(
            'Over capacity — %s committed hour(s) against %s focused hour(s) available.',
            o.observed_value, o.limit_value)
    END AS summary,
    -- The limit vs. observed, then the contributing subjects that add up to the overage.
    jsonb_build_array(
        jsonb_build_object(
            'kind', 'limit',
            'scope', 'capacity',
            'dimension', o.dimension,
            'limit', o.limit_value,
            'limit_text', o.limit_text,
            'observed', o.observed_value
        )
    )
    || CASE o.dimension
        WHEN 'projects' THEN coalesce((
            SELECT jsonb_agg(jsonb_build_object(
                'kind', 'subject',
                'id', ap.id,
                'subject_type', 'Project',
                'urn', ap.urn
            ) ORDER BY ap.urn)
            FROM active_projects ap), '[]'::jsonb)
        WHEN 'hours' THEN coalesce((
            SELECT jsonb_agg(jsonb_build_object(
                'kind', 'subject',
                'id', ch.id,
                'subject_type', ch.type,
                'urn', ch.urn,
                'committed_hours', ch.hours
            ) ORDER BY ch.urn)
            FROM committed_hours ch), '[]'::jsonb)
    END AS evidence
FROM observed o
WHERE o.observed_value > o.limit_value
ORDER BY o.urn;
