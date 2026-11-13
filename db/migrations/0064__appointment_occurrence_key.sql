-- Code review CR65: one occurrence per (series, slot) for a repeating Appointment.
--
-- Materialize made each date of a series' pattern that no occurrence was dated on. Two runs at
-- once (the web's button and bsk materialize, or a double click) could both find a date free
-- and both make it, and an occurrence Chris moved to another day left its old date free, so the
-- next run made it again. An occurrence now carries attributes.occurrence, the date it stands
-- for, apart from attributes.date, the day it's on. AppointmentService makes occurrences under
-- an advisory lock per series; this index is the store's guarantee, as
-- subject_task_repetition_key is for repeating Tasks (migration 0042).
--
-- Occurrences made before this have no occurrence key. Their (series, NULL) pairs never clash,
-- so the index builds whatever is already there, and Materialize treats each as standing for
-- its date. Nothing already made is changed.
CREATE UNIQUE INDEX IF NOT EXISTS subject_appointment_occurrence_key
    ON bsk.subject ((attributes->>'series'), (attributes->>'occurrence'))
    WHERE type = 'Appointment' AND (attributes->>'series') IS NOT NULL;
