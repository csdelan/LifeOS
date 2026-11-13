-- 0058__deny_truncate.sql
-- Code review CR30: TRUNCATE is refused on the append-only tables, as UPDATE and DELETE are.
--
-- The deny triggers fire for each row an UPDATE or DELETE touches. TRUNCATE fires no row
-- triggers, so `TRUNCATE bsk.artifact_blob` would have emptied every stored photo and recording,
-- and `TRUNCATE bsk.event CASCADE` the whole log with everything that references it. A statement
-- trigger on each table refuses it, with the same message and error code.

CREATE TRIGGER event_deny_truncate BEFORE TRUNCATE ON bsk.event
    FOR EACH STATEMENT EXECUTE FUNCTION bsk.deny_mutation();

CREATE TRIGGER artifact_deny_truncate BEFORE TRUNCATE ON bsk.artifact
    FOR EACH STATEMENT EXECUTE FUNCTION bsk.deny_mutation();

CREATE TRIGGER artifact_blob_deny_truncate BEFORE TRUNCATE ON bsk.artifact_blob
    FOR EACH STATEMENT EXECUTE FUNCTION bsk.deny_mutation();

CREATE TRIGGER artifact_object_deny_truncate BEFORE TRUNCATE ON bsk.artifact_object
    FOR EACH STATEMENT EXECUTE FUNCTION bsk.deny_mutation();
