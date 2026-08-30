-- No title header on purpose: the slug ('untitled') is used as the title.
SELECT
    s.id    AS subject_id,
    s.urn   AS subject_urn,
    s.type  AS subject_type,
    s.title AS subject_title,
    'unused'    AS summary,
    '[]'::jsonb AS evidence
FROM bsk.subject s
WHERE false;
