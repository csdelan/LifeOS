-- 0019__person_association.sql
-- The People-association link: a non-alignment way to attach a Person to an item
-- (attendee / owner / assignee / waiting-for / involves), needed across Appointments
-- (CAL-1 attendees), Commitments (GEN-12 owner), Inbox delegation (INBOX-3) and Task
-- "waiting for" (GEN-10). Deliberately NOT a serves/results_in edge — those wire the
-- alignment graph about work; involvement is a property, like Area (D1) and Tags.
--
-- Stored as a soft-reference attribute: attributes.people = a JSON array of
-- {"person": "<person-urn>", "role": "<role>"}. This keeps it in the typed-jsonb
-- subject table (zero DDL) and supports several people per item (e.g. multiple
-- attendees). This view flattens it and joins the Person so "everything involving
-- person X" — and the Person detail screen's grouped Commitments / Tasks /
-- Appointments (GEN-15) — are ordinary reads.

CREATE OR REPLACE VIEW bsk.v_person_association AS
SELECT
    s.id      AS subject_id,
    s.urn     AS subject_urn,
    s.type    AS subject_type,
    s.title   AS subject_title,
    assoc->>'role' AS role,
    p.id      AS person_id,
    p.urn     AS person_urn,
    p.title   AS person_name
FROM bsk.subject s
CROSS JOIN LATERAL jsonb_array_elements(s.attributes->'people') AS assoc
JOIN bsk.subject p ON p.urn = assoc->>'person' AND p.type = 'Person'
WHERE jsonb_typeof(s.attributes->'people') = 'array'
  -- Archived items are hidden from default views (D9); the involving item drops out,
  -- but the Person themselves is unaffected.
  AND NOT bsk.is_archived(s.id);

COMMENT ON VIEW bsk.v_person_association IS
    'Flattened People-association links (attributes.people): which Person is involved in which subject, and in what role. A property, not an alignment edge.';

GRANT SELECT ON bsk.v_person_association TO bsk_reader;
