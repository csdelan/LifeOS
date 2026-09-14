-- 0020__appointment_type.sql
-- CAL-1 / D10: Appointment becomes a durable subject type (the pilot's last net-new
-- type). Time fields (date / start / end / all_day / location / meeting_link), area
-- and recurrence (D8) are attributes; attendees are People-associations (migration
-- 0019, role = attendee).
--
-- The D10 divergence, recorded deliberately: unlike Habit's *projected* occurrences,
-- each Appointment occurrence is MATERIALIZED as its own Appointment subject so it
-- carries its own editable status (Scheduled / Completed / Cancelled / Missed, moved
-- by state_change like any subject — status stays event-driven, no mutable-status
-- table). A recurring appointment is a series subject (it holds the recurrence); its
-- occurrences reference it via attributes.series = <series urn> and inherit the
-- series' template fields unless they override them. A one-time appointment is a
-- single Appointment subject with a date and no series.

DO $$
DECLARE
    check_name text;
BEGIN
    SELECT conname INTO check_name
    FROM pg_constraint
    WHERE conrelid = 'bsk.subject'::regclass
      AND contype = 'c'
      AND pg_get_constraintdef(oid) ILIKE '%Season%';

    IF check_name IS NOT NULL THEN
        EXECUTE format('ALTER TABLE bsk.subject DROP CONSTRAINT %I', check_name);
    END IF;
END
$$;

ALTER TABLE bsk.subject
    ADD CONSTRAINT subject_type_check
    CHECK (type IN (
        'Value', 'Goal', 'Problem', 'Project', 'Task', 'Commitment',
        'Decision', 'Idea', 'Person', 'Constraint', 'Season', 'Area', 'Habit', 'Appointment'));

-- The appointment reader: occurrences and one-time appointments, with template fields
-- inherited from the series when an occurrence does not override them, and status
-- folded from source (default Scheduled). A row is a series template when it carries a
-- recurrence and no date; an actual calendar entry has a date (filter on it).
CREATE OR REPLACE VIEW bsk.v_appointment AS
SELECT
    a.id,
    a.urn,
    a.title,
    a.attributes->>'date'                                            AS date,
    coalesce(a.attributes->>'start', ser.attributes->>'start')       AS start_time,
    coalesce(a.attributes->>'end', ser.attributes->>'end')           AS end_time,
    coalesce(a.attributes->>'all_day', ser.attributes->>'all_day')   AS all_day,
    coalesce(a.attributes->>'location', ser.attributes->>'location') AS location,
    coalesce(a.attributes->>'meeting_link', ser.attributes->>'meeting_link') AS meeting_link,
    coalesce(a.attributes->>'area', ser.attributes->>'area')         AS area,
    a.attributes->>'series'                                          AS series_urn,
    a.attributes->'recurrence'                                       AS recurrence,
    coalesce(scs.status, 'Scheduled')                                AS status,
    bsk.is_archived(a.id)                                            AS archived,
    a.created_at
FROM bsk.subject a
LEFT JOIN bsk.subject ser
       ON ser.urn = a.attributes->>'series' AND ser.type = 'Appointment'
LEFT JOIN bsk_derived.subject_current_source scs ON scs.subject_id = a.id
WHERE a.type = 'Appointment';

COMMENT ON VIEW bsk.v_appointment IS
    'CAL-1/D10: Appointments — one-time, series templates, and materialized occurrences (attributes.series). Occurrences inherit template fields; status folded (default Scheduled).';

GRANT SELECT ON bsk.v_appointment TO bsk_reader;
