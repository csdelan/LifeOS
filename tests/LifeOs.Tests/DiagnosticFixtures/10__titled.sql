-- title: A titled fixture diagnostic
-- A well-formed, side-effect-free fixture used to prove discovery, ordering,
-- title parsing, and --only filtering end-to-end. Returns the contract columns
-- but no rows, so it does not depend on any seeded state.
SELECT
    s.id    AS subject_id,
    s.urn   AS subject_urn,
    s.type  AS subject_type,
    s.title AS subject_title,
    'unused'    AS summary,
    '[]'::jsonb AS evidence
FROM bsk.subject s
WHERE false;
