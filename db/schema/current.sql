-- The LifeOS schema as migrations 0001 to 0060 leave it. GENERATED: don't edit it.
-- Change the schema with a new migration in db/migrations, then run
-- ./scripts/update-schema.ps1 to regenerate this file. CI fails while it is stale.
--
-- Read this to see what a view or function is now; read the migrations for why.

CREATE SCHEMA bsk;

CREATE SCHEMA bsk_derived;

CREATE SCHEMA bsk_ops;

-- ---------------------------------------------------------------- tables

-- Raw captured content (text-only in Stage 1); immutable, referenced by events.
CREATE TABLE bsk.artifact (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    content text NOT NULL,
    recorded_at timestamp with time zone NOT NULL DEFAULT now(),
    CONSTRAINT artifact_pkey PRIMARY KEY (id)
);

-- Binary payload + metadata for an artifact (1:1); immutable, append-only, never mutated.
CREATE TABLE bsk.artifact_blob (
    artifact_id uuid NOT NULL,
    bytes bytea NOT NULL,
    content_type text NOT NULL,
    byte_size bigint NOT NULL,
    sha256 text NOT NULL,
    filename text,
    recorded_at timestamp with time zone NOT NULL DEFAULT now(),
    CONSTRAINT artifact_blob_artifact_id_fkey FOREIGN KEY (artifact_id) REFERENCES bsk.artifact(id),
    CONSTRAINT artifact_blob_byte_size_check CHECK ((byte_size >= 0)),
    CONSTRAINT artifact_blob_pkey PRIMARY KEY (artifact_id),
    CONSTRAINT artifact_blob_sha256_check CHECK ((sha256 ~ '^[0-9a-f]{64}$'::text)),
    CONSTRAINT artifact_blob_size_matches CHECK ((octet_length(bytes) = byte_size))
);

-- Where an artifact's bytes live in object storage, with their metadata (1:1); append-only, never mutated.
CREATE TABLE bsk.artifact_object (
    artifact_id uuid NOT NULL,
    store text NOT NULL,
    bucket text NOT NULL,
    object_key text NOT NULL,
    content_type text NOT NULL,
    byte_size bigint NOT NULL,
    sha256 text NOT NULL,
    filename text,
    recorded_at timestamp with time zone NOT NULL DEFAULT now(),
    CONSTRAINT artifact_object_artifact_id_fkey FOREIGN KEY (artifact_id) REFERENCES bsk.artifact(id),
    CONSTRAINT artifact_object_bucket_check CHECK ((bucket <> ''::text)),
    CONSTRAINT artifact_object_byte_size_check CHECK ((byte_size >= 0)),
    CONSTRAINT artifact_object_object_key_check CHECK ((object_key <> ''::text)),
    CONSTRAINT artifact_object_pkey PRIMARY KEY (artifact_id),
    CONSTRAINT artifact_object_sha256_check CHECK ((sha256 ~ '^[0-9a-f]{64}$'::text)),
    CONSTRAINT artifact_object_store_check CHECK ((store ~ '^[a-z][a-z0-9_-]*$'::text))
);

-- Append-only, provenance-tagged, bitemporal event stream. Never updated or deleted.
CREATE TABLE bsk.event (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    kind text NOT NULL,
    provenance text NOT NULL,
    occurred_at timestamp with time zone NOT NULL,
    recorded_at timestamp with time zone NOT NULL DEFAULT now(),
    source_id text NOT NULL,
    external_id text,
    payload jsonb NOT NULL DEFAULT '{}'::jsonb,
    derived_from uuid[] NOT NULL DEFAULT '{}'::uuid[],
    artifact_id uuid,
    CONSTRAINT event_artifact_id_fkey FOREIGN KEY (artifact_id) REFERENCES bsk.artifact(id),
    CONSTRAINT event_derived_has_sources CHECK (((provenance <> 'derived'::text) OR (cardinality(derived_from) >= 1))),
    CONSTRAINT event_kind_check CHECK ((kind = ANY (ARRAY['journal'::text, 'note'::text, 'voice'::text, 'idea_session'::text, 'observation'::text, 'activity'::text, 'measurement'::text, 'interaction'::text, 'state_change'::text, 'archive_change'::text, 'triage'::text, 'ledger'::text, 'attribute_change'::text]))),
    CONSTRAINT event_pkey PRIMARY KEY (id),
    CONSTRAINT event_provenance_check CHECK ((provenance = ANY (ARRAY['declared'::text, 'observed'::text, 'derived'::text])))
);

-- GEN-1: universal tags on items (subjects or events). Classification, distinct from relations. Mutable (add=insert, remove=delete).
CREATE TABLE bsk.item_tag (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    subject_id uuid,
    event_id uuid,
    tag text NOT NULL,
    created_at timestamp with time zone NOT NULL DEFAULT now(),
    CONSTRAINT item_tag_event_id_fkey FOREIGN KEY (event_id) REFERENCES bsk.event(id),
    CONSTRAINT item_tag_exactly_one_item CHECK (((subject_id IS NOT NULL) <> (event_id IS NOT NULL))),
    CONSTRAINT item_tag_pkey PRIMARY KEY (id),
    CONSTRAINT item_tag_subject_id_fkey FOREIGN KEY (subject_id) REFERENCES bsk.subject(id),
    CONSTRAINT item_tag_tag_check CHECK (((tag = lower(TRIM(BOTH FROM tag))) AND (length(tag) > 0)))
);

-- Single typed-jsonb table for all subject types; source-side definition.
CREATE TABLE bsk.subject (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    urn text NOT NULL,
    type text NOT NULL,
    title text NOT NULL,
    attributes jsonb NOT NULL DEFAULT '{}'::jsonb,
    origin_event_id uuid,
    created_at timestamp with time zone NOT NULL DEFAULT now(),
    CONSTRAINT subject_origin_event_id_fkey FOREIGN KEY (origin_event_id) REFERENCES bsk.event(id),
    CONSTRAINT subject_pkey PRIMARY KEY (id),
    CONSTRAINT subject_type_check CHECK ((type = ANY (ARRAY['Value'::text, 'Goal'::text, 'Problem'::text, 'Project'::text, 'Task'::text, 'Commitment'::text, 'Decision'::text, 'Idea'::text, 'Person'::text, 'Constraint'::text, 'Season'::text, 'Area'::text, 'Habit'::text, 'Appointment'::text, 'Reward'::text, 'Space'::text, 'Metric'::text, 'Review'::text]))),
    CONSTRAINT subject_urn_key UNIQUE (urn),
    CONSTRAINT subject_value_has_statement CHECK (((type <> 'Value'::text) OR (length(TRIM(BOTH FROM COALESCE((attributes ->> 'statement'::text), ''::text))) > 0)))
);

-- SOURCE: event -> subject edges (concerns/evidences/violates); provenance-tagged, never rebuilt.
CREATE TABLE bsk.subject_event (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    event_id uuid NOT NULL,
    subject_id uuid NOT NULL,
    relation text NOT NULL,
    provenance text NOT NULL,
    created_at timestamp with time zone NOT NULL DEFAULT now(),
    CONSTRAINT subject_event_event_id_fkey FOREIGN KEY (event_id) REFERENCES bsk.event(id),
    CONSTRAINT subject_event_pkey PRIMARY KEY (id),
    CONSTRAINT subject_event_provenance_check CHECK ((provenance = ANY (ARRAY['declared'::text, 'observed'::text, 'derived'::text]))),
    CONSTRAINT subject_event_relation_check CHECK ((relation = ANY (ARRAY['concerns'::text, 'evidences'::text, 'violates'::text]))),
    CONSTRAINT subject_event_subject_id_fkey FOREIGN KEY (subject_id) REFERENCES bsk.subject(id)
);

-- Directed, provenance-tagged edges between subjects (serves/results_in/supersedes).
CREATE TABLE bsk.subject_relation (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    from_subject uuid NOT NULL,
    relation text NOT NULL,
    to_subject uuid NOT NULL,
    provenance text NOT NULL,
    created_at timestamp with time zone NOT NULL DEFAULT now(),
    CONSTRAINT relation_from_subject_fkey FOREIGN KEY (from_subject) REFERENCES bsk.subject(id),
    CONSTRAINT relation_pkey PRIMARY KEY (id),
    CONSTRAINT relation_provenance_check CHECK ((provenance = ANY (ARRAY['declared'::text, 'observed'::text, 'derived'::text]))),
    CONSTRAINT relation_to_subject_fkey FOREIGN KEY (to_subject) REFERENCES bsk.subject(id),
    CONSTRAINT subject_relation_relation_check CHECK ((relation = ANY (ARRAY['serves'::text, 'results_in'::text, 'supersedes'::text])))
);

-- Derived: current status per subject, folded from state_change events. Rebuilt wholesale; never edited directly.
CREATE TABLE bsk_derived.subject_current (
    subject_id uuid NOT NULL,
    urn text NOT NULL,
    type text NOT NULL,
    title text NOT NULL,
    status text,
    status_event_id uuid,
    status_occurred_at timestamp with time zone,
    CONSTRAINT subject_current_pkey PRIMARY KEY (subject_id)
);

-- One row per MCP tool call, or per refused call from a client not on the allow list. Kept 90 days.
CREATE TABLE bsk_ops.agent_call (
    id bigint NOT NULL,
    called_at timestamp with time zone NOT NULL DEFAULT now(),
    client_id text NOT NULL,
    tool text,
    arguments text,
    outcome text NOT NULL,
    items integer,
    duration_ms integer NOT NULL,
    CONSTRAINT agent_call_arguments_check CHECK (((arguments IS NULL) OR (length(arguments) <= 1024))),
    CONSTRAINT agent_call_client_id_check CHECK (((length(client_id) >= 1) AND (length(client_id) <= 200))),
    CONSTRAINT agent_call_duration_ms_check CHECK ((duration_ms >= 0)),
    CONSTRAINT agent_call_items_check CHECK (((items IS NULL) OR (items >= 0))),
    CONSTRAINT agent_call_outcome_check CHECK ((outcome = ANY (ARRAY['ok'::text, 'error'::text, 'refused'::text]))),
    CONSTRAINT agent_call_pkey PRIMARY KEY (id),
    CONSTRAINT agent_call_tool_check CHECK (((tool IS NULL) OR ((length(tool) >= 1) AND (length(tool) <= 100))))
);

-- Agents Chris approved in LifeOS (the /mcp allow list): allowed while revoked_at is null.
CREATE TABLE bsk_ops.agent_client (
    client_id text NOT NULL,
    name text NOT NULL,
    client_uri text,
    allowed_at timestamp with time zone NOT NULL DEFAULT now(),
    revoked_at timestamp with time zone,
    person_id uuid,
    updated_at timestamp with time zone NOT NULL DEFAULT now(),
    redirect_uri text,
    replaced_by text,
    CONSTRAINT agent_client_client_id_check CHECK ((client_id ~ '^[A-Za-z0-9._:-]{1,200}$'::text)),
    CONSTRAINT agent_client_client_uri_check CHECK (((client_uri IS NULL) OR (length(client_uri) <= 500))),
    CONSTRAINT agent_client_name_check CHECK (((length(name) >= 1) AND (length(name) <= 200))),
    CONSTRAINT agent_client_pkey PRIMARY KEY (client_id),
    CONSTRAINT agent_client_redirect_uri_check CHECK (((redirect_uri IS NULL) OR ((length(redirect_uri) >= 1) AND (length(redirect_uri) <= 500)))),
    CONSTRAINT agent_client_replaced_by_check CHECK (((replaced_by IS NULL) OR (replaced_by ~ '^[A-Za-z0-9._:-]{1,200}$'::text)))
);

-- One row per connected outside service: its encrypted OAuth tokens, a connect in progress, and the last sync's outcome.
CREATE TABLE bsk_ops.integration (
    service text NOT NULL,
    access_token_enc bytea,
    refresh_token_enc bytea,
    expires_at timestamp with time zone,
    scopes text[] NOT NULL DEFAULT '{}'::text[],
    connected_at timestamp with time zone,
    needs_reconnect boolean NOT NULL DEFAULT false,
    reconnect_reason text,
    pending_state text,
    pending_verifier text,
    pending_redirect_uri text,
    pending_until timestamp with time zone,
    last_synced_at timestamp with time zone,
    last_sync_error text,
    history_from date,
    updated_at timestamp with time zone NOT NULL DEFAULT now(),
    history_done boolean NOT NULL DEFAULT false,
    CONSTRAINT integration_pending_together CHECK ((((pending_state IS NULL) = (pending_verifier IS NULL)) AND ((pending_state IS NULL) = (pending_redirect_uri IS NULL)) AND ((pending_state IS NULL) = (pending_until IS NULL)))),
    CONSTRAINT integration_pkey PRIMARY KEY (service),
    CONSTRAINT integration_service_check CHECK ((service ~ '^[a-z][a-z0-9_-]*$'::text)),
    CONSTRAINT integration_tokens_together CHECK ((((access_token_enc IS NULL) = (refresh_token_enc IS NULL)) AND ((access_token_enc IS NULL) = (expires_at IS NULL))))
);

CREATE TABLE public.schema_migrations (
    version bigint NOT NULL,
    name text NOT NULL,
    checksum text NOT NULL,
    applied_at timestamp with time zone NOT NULL DEFAULT now(),
    CONSTRAINT schema_migrations_pkey PRIMARY KEY (version)
);

-- ---------------------------------------------------------------- indexes

CREATE INDEX artifact_blob_sha256 ON bsk.artifact_blob USING btree (sha256);
CREATE INDEX artifact_object_sha256 ON bsk.artifact_object USING btree (sha256);
CREATE INDEX event_activity_occurrence_idx ON bsk.event USING btree (((payload ->> 'subject_id'::text)), ((payload ->> 'occurrence'::text)), occurred_at DESC, recorded_at DESC, id DESC) WHERE (kind = 'activity'::text);
CREATE INDEX event_archive_change_subject ON bsk.event USING btree (((payload ->> 'subject_id'::text)), occurred_at DESC) WHERE (kind = 'archive_change'::text);
CREATE INDEX event_artifact_idx ON bsk.event USING btree (artifact_id, recorded_at, id) WHERE (artifact_id IS NOT NULL);
CREATE INDEX event_exception_day_idx ON bsk.event USING btree ((((payload -> 'exception_day'::text) ->> 'day'::text)), recorded_at) WHERE ((kind = 'note'::text) AND jsonb_exists(payload, 'exception_day'::text));
CREATE INDEX event_fast_idx ON bsk.event USING btree ((((payload -> 'fast'::text) ->> 'id'::text)), recorded_at) WHERE ((kind = 'activity'::text) AND jsonb_exists(payload, 'fast'::text));
CREATE INDEX event_food_day_idx ON bsk.event USING btree ((((payload -> 'food_day'::text) ->> 'on'::text))) WHERE ((kind = 'activity'::text) AND jsonb_exists(payload, 'food_day'::text));
CREATE INDEX event_kind_occurred_idx ON bsk.event USING btree (kind, occurred_at);
CREATE INDEX event_lab_visit_idx ON bsk.event USING btree ((((payload -> 'lab_visit'::text) ->> 'id'::text)), recorded_at) WHERE ((kind = 'measurement'::text) AND jsonb_exists(payload, 'lab_visit'::text));
CREATE INDEX event_ledger_award_key_idx ON bsk.event USING btree (((payload ->> 'award_key'::text))) WHERE (kind = 'ledger'::text);
CREATE INDEX event_ledger_occurred_idx ON bsk.event USING btree (occurred_at) WHERE (kind = 'ledger'::text);
CREATE INDEX event_ledger_subject_idx ON bsk.event USING btree (((payload ->> 'subject_id'::text))) WHERE (kind = 'ledger'::text);
CREATE INDEX event_meal_idx ON bsk.event USING btree ((((payload -> 'meal'::text) ->> 'id'::text)), recorded_at) WHERE ((kind = 'measurement'::text) AND jsonb_exists(payload, 'meal'::text));
CREATE INDEX event_metric_reading_idx ON bsk.event USING btree ((((payload -> 'metric_reading'::text) ->> 'id'::text)), recorded_at) WHERE ((kind = 'measurement'::text) AND jsonb_exists(payload, 'metric_reading'::text));
CREATE INDEX event_oura_activity_idx ON bsk.event USING btree ((((payload -> 'oura_activity'::text) ->> 'day'::text)), recorded_at) WHERE ((kind = 'measurement'::text) AND jsonb_exists(payload, 'oura_activity'::text));
CREATE INDEX event_oura_readiness_idx ON bsk.event USING btree ((((payload -> 'oura_readiness'::text) ->> 'day'::text)), recorded_at) WHERE ((kind = 'measurement'::text) AND jsonb_exists(payload, 'oura_readiness'::text));
CREATE INDEX event_oura_sleep_day_idx ON bsk.event USING btree ((((payload -> 'oura_sleep_day'::text) ->> 'day'::text)), recorded_at) WHERE ((kind = 'measurement'::text) AND jsonb_exists(payload, 'oura_sleep_day'::text));
CREATE INDEX event_oura_sleep_idx ON bsk.event USING btree ((((payload -> 'oura_sleep'::text) ->> 'id'::text)), recorded_at) WHERE ((kind = 'measurement'::text) AND jsonb_exists(payload, 'oura_sleep'::text));
CREATE INDEX event_oura_tag_idx ON bsk.event USING btree ((((payload -> 'oura_tag'::text) ->> 'id'::text)), recorded_at) WHERE ((kind = 'observation'::text) AND jsonb_exists(payload, 'oura_tag'::text));
CREATE INDEX event_oura_workout_idx ON bsk.event USING btree ((((payload -> 'oura_workout'::text) ->> 'id'::text)), recorded_at) WHERE ((kind = 'activity'::text) AND jsonb_exists(payload, 'oura_workout'::text));
CREATE INDEX event_payload_gin ON bsk.event USING gin (payload);
CREATE INDEX event_recorded_at_idx ON bsk.event USING btree (recorded_at);
CREATE UNIQUE INDEX event_source_external_key ON bsk.event USING btree (source_id, external_id) WHERE (external_id IS NOT NULL);
CREATE INDEX event_space_photo_idx ON bsk.event USING btree ((((payload -> 'space_photo'::text) ->> 'id'::text)), recorded_at) WHERE ((kind = 'observation'::text) AND jsonb_exists(payload, 'space_photo'::text));
CREATE INDEX event_space_rating_idx ON bsk.event USING btree ((((payload -> 'space_rating'::text) ->> 'id'::text)), recorded_at) WHERE ((kind = 'measurement'::text) AND jsonb_exists(payload, 'space_rating'::text));
CREATE INDEX event_state_change_subject ON bsk.event USING btree (((payload ->> 'subject_id'::text)), occurred_at DESC) WHERE (kind = 'state_change'::text);
CREATE INDEX event_timer_subject_idx ON bsk.event USING btree (((payload ->> 'subject_id'::text)), occurred_at) WHERE ((kind = 'activity'::text) AND jsonb_exists(payload, 'timer'::text));
CREATE INDEX event_triage_item ON bsk.event USING btree (((payload ->> 'item_id'::text)), occurred_at DESC) WHERE (kind = 'triage'::text);
CREATE INDEX event_workout_note_idx ON bsk.event USING btree ((((payload -> 'workout_note'::text) ->> 'workout'::text)), recorded_at) WHERE ((kind = 'note'::text) AND jsonb_exists(payload, 'workout_note'::text));
CREATE UNIQUE INDEX item_tag_event_key ON bsk.item_tag USING btree (event_id, tag) WHERE (event_id IS NOT NULL);
CREATE UNIQUE INDEX item_tag_subject_key ON bsk.item_tag USING btree (subject_id, tag) WHERE (subject_id IS NOT NULL);
CREATE INDEX item_tag_tag ON bsk.item_tag USING btree (tag);
CREATE INDEX subject_attributes_gin ON bsk.subject USING gin (attributes);
CREATE INDEX subject_next_review_at ON bsk.subject USING btree (((attributes ->> 'next_review_at'::text))) WHERE jsonb_exists(attributes, 'next_review_at'::text);
CREATE UNIQUE INDEX subject_reuse_title_key ON bsk.subject USING btree (type, title) WHERE (type = 'Problem'::text);
CREATE UNIQUE INDEX subject_review_period_key ON bsk.subject USING btree (((attributes ->> 'kind'::text)), ((attributes ->> 'period'::text))) WHERE (type = 'Review'::text);
CREATE UNIQUE INDEX subject_task_repetition_key ON bsk.subject USING btree (((attributes ->> 'series'::text)), ((attributes ->> 'occurrence'::text))) WHERE ((type = 'Task'::text) AND ((attributes ->> 'series'::text) IS NOT NULL));
CREATE UNIQUE INDEX subject_event_edge_key ON bsk.subject_event USING btree (event_id, subject_id, relation);
CREATE INDEX subject_event_subject_idx ON bsk.subject_event USING btree (subject_id, relation);
CREATE INDEX subject_relation_from ON bsk.subject_relation USING btree (from_subject, relation);
CREATE INDEX subject_relation_to ON bsk.subject_relation USING btree (to_subject, relation);
CREATE INDEX agent_call_called_at ON bsk_ops.agent_call USING btree (called_at);
CREATE INDEX agent_call_client_called_at ON bsk_ops.agent_call USING btree (client_id, called_at DESC);

-- ---------------------------------------------------------------- views

-- ACTIVITY-2 (D14): one row per Oura activity day (starting 4 a.m.) — the newest version by recorded_at. Steps, calories, walking distance, activity time by intensity, non-wear time, in seconds where a time.
CREATE VIEW bsk.v_activity_day AS
 WITH version AS (
         SELECT e.id AS event_id,
            e.payload -> 'oura_activity'::text AS a,
            e.recorded_at,
            row_number() OVER w_desc AS rn,
            count(*) OVER w_all AS versions
           FROM bsk.event e
          WHERE e.kind = 'measurement'::text AND e.source_id = 'oura'::text AND jsonb_exists(e.payload, 'oura_activity'::text) AND jsonb_typeof(e.payload -> 'oura_activity'::text) = 'object'::text AND bsk.try_to_date((e.payload -> 'oura_activity'::text) ->> 'day'::text) IS NOT NULL
          WINDOW w_all AS (PARTITION BY ((e.payload -> 'oura_activity'::text) ->> 'day'::text)), w_desc AS (PARTITION BY ((e.payload -> 'oura_activity'::text) ->> 'day'::text) ORDER BY e.recorded_at DESC, e.id DESC)
        )
 SELECT bsk.try_to_date(a ->> 'day'::text) AS day,
    bsk.json_int(a -> 'steps'::text) AS steps,
    bsk.json_int(a -> 'active_calories'::text) AS active_calories,
    bsk.json_int(a -> 'total_calories'::text) AS total_calories,
    bsk.json_int(a -> 'walking_m'::text) AS walking_m,
    bsk.json_int(a -> 'high_s'::text) AS high_s,
    bsk.json_int(a -> 'medium_s'::text) AS medium_s,
    bsk.json_int(a -> 'low_s'::text) AS low_s,
    bsk.json_int(a -> 'sedentary_s'::text) AS sedentary_s,
    bsk.json_int(a -> 'resting_s'::text) AS resting_s,
    bsk.json_int(a -> 'non_wear_s'::text) AS non_wear_s,
    bsk.json_int(a -> 'score'::text) AS score,
    recorded_at AS synced_at,
    versions::integer AS versions,
    event_id
   FROM version v
  WHERE rn = 1;

-- CAL-1/D10: Appointments — one-time, series templates, and materialized occurrences (attributes.series). Occurrences inherit template fields; status folded (default Scheduled).
CREATE VIEW bsk.v_appointment AS
 SELECT a.id,
    a.urn,
    a.title,
    a.attributes ->> 'date'::text AS date,
    COALESCE(a.attributes ->> 'start'::text, ser.attributes ->> 'start'::text) AS start_time,
    COALESCE(a.attributes ->> 'end'::text, ser.attributes ->> 'end'::text) AS end_time,
    COALESCE(a.attributes ->> 'all_day'::text, ser.attributes ->> 'all_day'::text) AS all_day,
    COALESCE(a.attributes ->> 'location'::text, ser.attributes ->> 'location'::text) AS location,
    COALESCE(a.attributes ->> 'meeting_link'::text, ser.attributes ->> 'meeting_link'::text) AS meeting_link,
    COALESCE(a.attributes ->> 'area'::text, ser.attributes ->> 'area'::text) AS area,
    a.attributes ->> 'series'::text AS series_urn,
    a.attributes -> 'recurrence'::text AS recurrence,
    COALESCE(scs.status, 'Scheduled'::text) AS status,
    bsk.is_archived(a.id) AS archived,
    a.created_at
   FROM bsk.subject a
     LEFT JOIN bsk.subject ser ON ser.urn = (a.attributes ->> 'series'::text) AND ser.type = 'Appointment'::text
     LEFT JOIN bsk_derived.subject_current_source scs ON scs.subject_id = a.id
  WHERE a.type = 'Appointment'::text;

CREATE VIEW bsk.v_area AS
 SELECT id,
    urn,
    title AS name,
    attributes ->> 'description'::text AS description,
    attributes ->> 'notes'::text AS notes,
    created_at
   FROM bsk.subject s
  WHERE type = 'Area'::text;

CREATE VIEW bsk.v_artifact AS
 SELECT a.id,
    a.recorded_at,
    b.artifact_id IS NOT NULL OR o.artifact_id IS NOT NULL AS has_bytes,
        CASE
            WHEN b.artifact_id IS NOT NULL THEN b.content_type
            ELSE o.content_type
        END AS content_type,
        CASE
            WHEN b.artifact_id IS NOT NULL THEN b.byte_size
            ELSE o.byte_size
        END AS byte_size,
        CASE
            WHEN b.artifact_id IS NOT NULL THEN b.sha256
            ELSE o.sha256
        END AS sha256,
        CASE
            WHEN b.artifact_id IS NOT NULL THEN b.filename
            ELSE o.filename
        END AS filename,
    e.id AS event_id,
    e.kind,
        CASE
            WHEN b.artifact_id IS NOT NULL THEN 'database'::text
            WHEN o.artifact_id IS NOT NULL THEN o.store
            ELSE NULL::text
        END AS stored_in
   FROM bsk.artifact a
     LEFT JOIN bsk.artifact_blob b ON b.artifact_id = a.id
     LEFT JOIN bsk.artifact_object o ON o.artifact_id = a.id
     LEFT JOIN LATERAL ( SELECT ev.id,
            ev.kind
           FROM bsk.event ev
          WHERE ev.artifact_id = a.id
          ORDER BY ev.recorded_at, ev.id
         LIMIT 1) e ON true;

-- REWARD-6/7 (D12): one row per currently paid award key x currency — base (before any life-domain multiplier), paid (the key's net), the latest earn's credited_on (NULL before 0030), its occurred_at and event. A reversed award has no row; re-earning files it on the new earn's day. Life-domain days total this per currency per credited day.
CREATE VIEW bsk.v_award_credit AS
 WITH net AS (
         SELECT l.award_key,
            l.currency,
            sum(l.amount)::bigint AS paid
           FROM bsk.v_ledger l
          WHERE l.award_key IS NOT NULL
          GROUP BY l.award_key, l.currency
         HAVING sum(l.amount) > 0::numeric
        ), latest_earn AS (
         SELECT DISTINCT ON ((e.payload ->> 'award_key'::text)) e.payload ->> 'award_key'::text AS award_key,
            e.id AS event_id,
            e.occurred_at,
            bsk.try_to_date(e.payload ->> 'credited_on'::text) AS credited_on,
                CASE
                    WHEN jsonb_typeof(e.payload -> 'amounts'::text) = 'object'::text THEN e.payload -> 'amounts'::text
                    ELSE NULL::jsonb
                END AS amounts,
                CASE
                    WHEN jsonb_typeof(e.payload -> 'base'::text) = 'object'::text THEN e.payload -> 'base'::text
                    ELSE NULL::jsonb
                END AS base
           FROM bsk.event e
          WHERE e.kind = 'ledger'::text AND (e.payload ->> 'entry'::text) = 'earn'::text AND (e.payload ->> 'award_key'::text) IS NOT NULL
          ORDER BY (e.payload ->> 'award_key'::text), e.occurred_at DESC, e.recorded_at DESC, e.id DESC
        )
 SELECT n.award_key,
    n.currency,
    COALESCE(
        CASE
            WHEN jsonb_typeof(le.base -> n.currency) = 'number'::text THEN (le.base ->> n.currency)::numeric
            ELSE NULL::numeric
        END,
        CASE
            WHEN jsonb_typeof(le.amounts -> n.currency) = 'number'::text THEN (le.amounts ->> n.currency)::numeric
            ELSE NULL::numeric
        END, n.paid::numeric)::bigint AS base,
    n.paid,
    le.credited_on,
    le.occurred_at,
    le.event_id
   FROM net n
     JOIN latest_earn le ON le.award_key = n.award_key;

CREATE VIEW bsk.v_event AS
 SELECT id,
    kind,
    provenance,
    occurred_at,
    recorded_at,
    source_id,
    external_id,
    payload ->> 'subject_id'::text AS subject_id,
    payload ->> 'status'::text AS status,
    artifact_id,
    derived_from,
    payload
   FROM bsk.event e;

-- REWARD-8 (D17): one row per day Chris has marked as an exception — the newest version of each day by recorded_at, unless voided. marked_at is when that version was written. From X10 the life-domain fold skips these days.
CREATE VIEW bsk.v_exception_day AS
 WITH version AS (
         SELECT e.id AS event_id,
            e.payload -> 'exception_day'::text AS x,
            bsk.try_to_date((e.payload -> 'exception_day'::text) ->> 'day'::text) AS day,
            e.recorded_at,
            row_number() OVER (PARTITION BY (bsk.try_to_date((e.payload -> 'exception_day'::text) ->> 'day'::text)) ORDER BY e.recorded_at DESC, e.id DESC) AS rn
           FROM bsk.event e
          WHERE e.kind = 'note'::text AND jsonb_exists(e.payload, 'exception_day'::text) AND jsonb_typeof(e.payload -> 'exception_day'::text) = 'object'::text AND bsk.try_to_date((e.payload -> 'exception_day'::text) ->> 'day'::text) IS NOT NULL
        )
 SELECT day,
    btrim(x ->> 'comment'::text) AS comment,
    recorded_at AS marked_at,
    event_id
   FROM version v
  WHERE rn = 1 AND COALESCE(x ->> 'voided'::text, 'false'::text) <> 'true'::text AND jsonb_typeof(x -> 'comment'::text) = 'string'::text AND btrim(x ->> 'comment'::text) <> ''::text;

-- FOOD-2 (D13): one row per live fast — the newest version of each fast id by recorded_at, unless voided. running while ended_at is null; seconds once ended; ended_on (the day it counts on) in the home zone; meals_during counts food with calories eaten inside it.
CREATE VIEW bsk.v_fast AS
 WITH version AS (
         SELECT e.id AS event_id,
            ((e.payload -> 'fast'::text) ->> 'id'::text)::uuid AS fast_id,
            e.payload -> 'fast'::text AS f,
            e.recorded_at,
            row_number() OVER (PARTITION BY ((e.payload -> 'fast'::text) ->> 'id'::text) ORDER BY e.recorded_at DESC, e.id DESC) AS rn
           FROM bsk.event e
          WHERE e.kind = 'activity'::text AND jsonb_exists(e.payload, 'fast'::text) AND jsonb_typeof(e.payload -> 'fast'::text) = 'object'::text AND ((e.payload -> 'fast'::text) ->> 'id'::text) ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'::text
        ), live AS (
         SELECT v.fast_id,
            (v.f ->> 'started_at'::text)::timestamp with time zone AS started_at,
                CASE
                    WHEN jsonb_typeof(v.f -> 'ended_at'::text) = 'string'::text THEN (v.f ->> 'ended_at'::text)::timestamp with time zone
                    ELSE NULL::timestamp with time zone
                END AS ended_at,
            v.recorded_at,
            v.event_id
           FROM version v
          WHERE v.rn = 1 AND COALESCE(v.f ->> 'voided'::text, 'false'::text) <> 'true'::text AND jsonb_typeof(v.f -> 'started_at'::text) = 'string'::text
        ), meal_time AS MATERIALIZED (
         SELECT m.eaten_at
           FROM bsk.v_meal m
          WHERE m.calories > 0
        )
 SELECT fast_id,
    started_at,
    ended_at,
        CASE
            WHEN ended_at IS NULL THEN 'running'::text
            ELSE 'ended'::text
        END AS state,
        CASE
            WHEN ended_at IS NOT NULL THEN floor(EXTRACT(epoch FROM ended_at - started_at))::bigint
            ELSE NULL::bigint
        END AS seconds,
    (started_at AT TIME ZONE bsk.home_zone())::date AS started_on,
    (ended_at AT TIME ZONE bsk.home_zone())::date AS ended_on,
    (started_at AT TIME ZONE bsk.home_zone()) AS started_local,
    (ended_at AT TIME ZONE bsk.home_zone()) AS ended_local,
    (( SELECT count(*) AS count
           FROM meal_time m
          WHERE m.eaten_at > l.started_at AND m.eaten_at < COALESCE(l.ended_at, now())))::integer AS meals_during,
    recorded_at,
    event_id
   FROM live l;

-- FOOD-3 / FOOD-4 / FOOD-6 (D13): one row per home-zone day with a live food entry, an ended fast or a close — calories, score x calories (numeric, since a score can be a half), the longest fast ending that day, its drinks, and whether it is closed (by hand, or because the day is over). DietRules (C#) scores it.
CREATE VIEW bsk.v_food_day AS
 WITH meal_day AS (
         SELECT m_1.eaten_on AS day,
            count(*)::integer AS meals,
            COALESCE(sum(m_1.calories), 0::bigint) AS calories,
            COALESCE(sum(m_1.score * m_1.calories::numeric), 0::numeric) AS score_calories,
            max(m_1.logged_at) AS last_recorded_at,
            array_agg(m_1.event_id ORDER BY m_1.eaten_at, m_1.event_id) AS food_event_ids,
            count(*) FILTER (WHERE m_1.alcoholic)::integer AS drinks,
            COALESCE(array_agg(m_1.event_id ORDER BY m_1.eaten_at, m_1.event_id) FILTER (WHERE m_1.alcoholic), '{}'::uuid[]) AS drink_event_ids
           FROM bsk.v_meal m_1
          GROUP BY m_1.eaten_on
        ), fast_day AS (
         SELECT DISTINCT ON (f_1.ended_on) f_1.ended_on AS day,
            f_1.seconds AS longest_fast_seconds,
            f_1.fast_id AS longest_fast_id,
            f_1.event_id AS fast_event_id,
            f_1.recorded_at
           FROM bsk.v_fast f_1
          WHERE f_1.state = 'ended'::text
          ORDER BY f_1.ended_on, f_1.seconds DESC, f_1.event_id
        ), close_day AS (
         SELECT bsk.try_to_date((e.payload -> 'food_day'::text) ->> 'on'::text) AS day,
            min(e.occurred_at) AS closed_at,
            (array_agg(e.id ORDER BY e.occurred_at, e.id))[1] AS close_event_id
           FROM bsk.event e
          WHERE e.kind = 'activity'::text AND jsonb_exists(e.payload, 'food_day'::text) AND jsonb_typeof(e.payload -> 'food_day'::text) = 'object'::text AND ((e.payload -> 'food_day'::text) ->> 'closed'::text) = 'true'::text
          GROUP BY (bsk.try_to_date((e.payload -> 'food_day'::text) ->> 'on'::text))
        ), days AS (
         SELECT meal_day.day
           FROM meal_day
        UNION
         SELECT fast_day.day
           FROM fast_day
        UNION
         SELECT close_day.day
           FROM close_day
          WHERE close_day.day IS NOT NULL
        )
 SELECT d.day,
    COALESCE(m.meals, 0) AS meals,
    COALESCE(m.calories, 0::bigint) AS calories,
    COALESCE(m.score_calories, 0::numeric) AS score_calories,
    f.longest_fast_seconds,
    f.longest_fast_id,
    f.fast_event_id,
    c.day IS NOT NULL AS closed_by_hand,
    c.closed_at,
    c.close_event_id,
    c.day IS NOT NULL OR d.day < bsk.home_today() AS closed,
    GREATEST(m.last_recorded_at, f.recorded_at, c.closed_at) AS last_recorded_at,
    COALESCE(m.food_event_ids, '{}'::uuid[]) AS food_event_ids,
    COALESCE(m.drinks, 0) AS drinks,
    COALESCE(m.drink_event_ids, '{}'::uuid[]) AS drink_event_ids
   FROM days d
     LEFT JOIN meal_day m ON m.day = d.day
     LEFT JOIN fast_day f ON f.day = d.day
     LEFT JOIN close_day c ON c.day = d.day;

CREATE VIEW bsk.v_habit AS
 SELECT id,
    urn,
    title AS name,
    attributes ->> 'cue'::text AS cue,
    attributes ->> 'routine'::text AS routine,
    attributes ->> 'reward'::text AS reward,
    attributes ->> 'start'::text AS start_date,
    attributes ->> 'end'::text AS end_date,
    COALESCE((attributes ->> 'allows_partial'::text)::boolean, false) AS allows_partial,
    attributes -> 'recurrence'::text AS recurrence,
    bsk.is_archived(id) AS archived,
    created_at
   FROM bsk.subject s
  WHERE type = 'Habit'::text;

-- REWARD-2: per Building habit (not Planned, 0049), adherence over its establish_target window (partial = half) and whether the target is met — the "ready to establish" suggestion. Its age is the days since its start less the days it spent Planned (0049). The window ends today in the home zone (0033). Read-only.
CREATE VIEW bsk.v_habit_establishment AS
 WITH habits AS (
         SELECT h.id,
            h.urn,
            h.title,
            st.start_date,
            pd.planned_days,
                CASE
                    WHEN jsonb_typeof((h.attributes -> 'establish_target'::text) -> 'min_adherence'::text) = 'number'::text THEN ((h.attributes -> 'establish_target'::text) ->> 'min_adherence'::text)::numeric
                    ELSE NULL::numeric
                END AS min_raw,
                CASE
                    WHEN jsonb_typeof((h.attributes -> 'establish_target'::text) -> 'window_days'::text) = 'number'::text THEN ((h.attributes -> 'establish_target'::text) ->> 'window_days'::text)::numeric
                    ELSE NULL::numeric
                END AS window_raw
           FROM bsk.subject h
             LEFT JOIN bsk_derived.subject_current_source c_1 ON c_1.subject_id = h.id
             CROSS JOIN LATERAL ( SELECT COALESCE(bsk.try_to_date(h.attributes ->> 'start'::text), (h.created_at AT TIME ZONE bsk.home_zone())::date) AS start_date) st
             CROSS JOIN LATERAL ( SELECT COALESCE(sum(GREATEST(0, LEAST(p.planned_until, bsk.home_today()) - GREATEST(COALESCE(p.planned_from, st.start_date), st.start_date))), 0::bigint)::integer AS planned_days
                   FROM bsk_derived.habit_planned_source p
                  WHERE p.habit_id = h.id AND p.planned_until IS NOT NULL) pd
          WHERE h.type = 'Habit'::text AND NOT bsk.is_terminal_status(c_1.status) AND lower(btrim(COALESCE(c_1.status, ''::text))) <> 'planned'::text AND NOT bsk.is_archived(h.id)
        ), targets AS (
         SELECT habits.id,
            habits.urn,
            habits.title,
            habits.start_date,
            habits.planned_days,
                CASE
                    WHEN habits.min_raw > 0::numeric AND habits.min_raw <= 1::numeric THEN habits.min_raw
                    WHEN habits.min_raw > 1::numeric AND habits.min_raw <= 100::numeric THEN habits.min_raw / 100::numeric
                    ELSE 0.8
                END AS min_adherence,
                CASE
                    WHEN habits.window_raw >= 1::numeric THEN floor(habits.window_raw)::integer
                    ELSE 60
                END AS window_days
           FROM habits
        ), counts AS (
         SELECT o.habit_id,
            count(*) FILTER (WHERE o.state = 'followed'::text) AS followed,
            count(*) FILTER (WHERE o.state = 'partial'::text) AS partial,
            count(*) FILTER (WHERE o.state = 'missed'::text) AS missed
           FROM bsk.v_habit_occurrence o
             JOIN targets t_1 ON t_1.id = o.habit_id
          WHERE o.occurrence_date > (bsk.home_today() - t_1.window_days) AND o.occurrence_date <= bsk.home_today()
          GROUP BY o.habit_id
        )
 SELECT t.id AS habit_id,
    t.urn AS habit_urn,
    t.title AS habit_name,
    t.min_adherence,
    t.window_days,
    bsk.home_today() - t.window_days + 1 AS window_start,
    COALESCE(c.followed, 0::bigint) AS followed,
    COALESCE(c.partial, 0::bigint) AS partial,
    COALESCE(c.missed, 0::bigint) AS missed,
    COALESCE(c.followed + c.partial + c.missed, 0::bigint) AS concluded,
        CASE
            WHEN COALESCE(c.followed + c.partial + c.missed, 0::bigint) > 0 THEN round((c.followed::numeric + 0.5 * c.partial::numeric) / (c.followed + c.partial + c.missed)::numeric, 4)
            ELSE NULL::numeric
        END AS adherence,
    bsk.home_today() - t.start_date - t.planned_days AS age_days,
    COALESCE(c.followed + c.partial + c.missed, 0::bigint) > 0 AND ((c.followed::numeric + 0.5 * c.partial::numeric) / (c.followed + c.partial + c.missed)::numeric) >= t.min_adherence AND (bsk.home_today() - t.start_date - t.planned_days) >= t.window_days AS target_met
   FROM targets t
     LEFT JOIN counts c ON c.habit_id = t.id;

-- GEN-3: derived habit occurrences (recurrence UNION recorded dates) with resolved state; unrecorded past dates read as missed; a Planned habit has no scheduled dates (0049) and a graduated (Established / Abandoned) one stops scheduling. Pure read — corrections recalculate automatically.
CREATE VIEW bsk.v_habit_occurrence AS
 SELECT habit_id,
    habit_urn,
    occurrence_date,
    state,
    adherence_event_id
   FROM bsk_derived.habit_occurrence_source o
  WHERE NOT archived;

-- GEN-3/D2: current per-habit streak of consecutive fully-followed concluded occurrences; partial or missed breaks it.
CREATE VIEW bsk.v_habit_streak AS
 WITH concluded AS (
         SELECT v_habit_occurrence.habit_id,
            v_habit_occurrence.habit_urn,
            v_habit_occurrence.occurrence_date,
            v_habit_occurrence.state,
            row_number() OVER (PARTITION BY v_habit_occurrence.habit_id ORDER BY v_habit_occurrence.occurrence_date DESC) AS rn
           FROM bsk.v_habit_occurrence
          WHERE v_habit_occurrence.state <> 'unrecorded'::text
        )
 SELECT habit_id,
    habit_urn,
    COALESCE(min(rn) FILTER (WHERE state <> 'followed'::text), max(rn) + 1) - 1 AS current_streak,
    max(occurrence_date) AS last_occurrence,
    (array_agg(state ORDER BY occurrence_date DESC))[1] AS last_state
   FROM concluded c
  GROUP BY habit_id, habit_urn;

-- INBOX-1: items whose newest triage marker is `flagged`, minus archived (D9) and terminal-status (D7) subjects. Attention only; resolution lives in the Status field. See docs/pilot/inbox-attention-vs-status.md.
CREATE VIEW bsk.v_inbox AS
 WITH latest_triage AS (
         SELECT DISTINCT ON ((e.payload ->> 'item_id'::text)) e.payload ->> 'item_id'::text AS item_id,
            e.payload ->> 'item_kind'::text AS item_kind,
            e.payload ->> 'state'::text AS state,
            e.id AS triage_event_id,
            e.occurred_at AS triaged_at
           FROM bsk.event e
          WHERE e.kind = 'triage'::text
          ORDER BY (e.payload ->> 'item_id'::text), e.occurred_at DESC, e.recorded_at DESC, e.id DESC
        )
 SELECT lt.item_id::uuid AS item_id,
    lt.item_kind,
    lt.triage_event_id,
    lt.triaged_at,
    s.urn AS subject_urn,
    s.type AS subject_type,
    s.title AS subject_title,
    ev.kind AS event_kind,
    a.content AS event_content
   FROM latest_triage lt
     LEFT JOIN bsk.subject s ON lt.item_kind = 'subject'::text AND s.id = lt.item_id::uuid
     LEFT JOIN bsk.event ev ON lt.item_kind = 'event'::text AND ev.id = lt.item_id::uuid
     LEFT JOIN bsk.artifact a ON a.id = ev.artifact_id
     LEFT JOIN bsk_derived.subject_current_source sc ON lt.item_kind = 'subject'::text AND sc.subject_id = lt.item_id::uuid
  WHERE lt.state = 'flagged'::text AND (s.id IS NULL OR NOT bsk.is_archived(s.id)) AND NOT bsk.is_terminal_status(sc.status);

-- Items that were triaged and have left the inbox (dismissed, promoted, or a terminal Status), minus archived subjects (D9). The complement of v_inbox; drives the web Inbox's Resolved view and Back to Inbox.
CREATE VIEW bsk.v_inbox_resolved AS
 WITH latest_triage AS (
         SELECT DISTINCT ON ((e.payload ->> 'item_id'::text)) e.payload ->> 'item_id'::text AS item_id,
            e.payload ->> 'item_kind'::text AS item_kind,
            e.payload ->> 'state'::text AS state,
            e.id AS triage_event_id,
            e.occurred_at AS triaged_at
           FROM bsk.event e
          WHERE e.kind = 'triage'::text
          ORDER BY (e.payload ->> 'item_id'::text), e.occurred_at DESC, e.recorded_at DESC, e.id DESC
        )
 SELECT lt.item_id::uuid AS item_id,
    lt.item_kind,
    lt.triage_event_id,
    lt.triaged_at,
        CASE
            WHEN lt.state = 'flagged'::text THEN 'status'::text
            ELSE lt.state
        END AS resolution,
        CASE
            WHEN lt.state = 'flagged'::text THEN GREATEST(lt.triaged_at, sc.status_occurred_at)
            ELSE lt.triaged_at
        END AS resolved_at,
    sc.status AS subject_status,
    bsk.is_terminal_status(sc.status) AS subject_status_terminal,
    s.urn AS subject_urn,
    s.type AS subject_type,
    s.title AS subject_title,
    ev.kind AS event_kind,
    a.content AS event_content
   FROM latest_triage lt
     LEFT JOIN bsk.subject s ON lt.item_kind = 'subject'::text AND s.id = lt.item_id::uuid
     LEFT JOIN bsk.event ev ON lt.item_kind = 'event'::text AND ev.id = lt.item_id::uuid
     LEFT JOIN bsk.artifact a ON a.id = ev.artifact_id
     LEFT JOIN bsk_derived.subject_current_source sc ON lt.item_kind = 'subject'::text AND sc.subject_id = lt.item_id::uuid
  WHERE (s.id IS NULL OR NOT bsk.is_archived(s.id)) AND (lt.state <> 'flagged'::text OR bsk.is_terminal_status(sc.status));

CREATE VIEW bsk.v_item_tag AS
 SELECT it.id,
        CASE
            WHEN it.subject_id IS NOT NULL THEN 'subject'::text
            ELSE 'event'::text
        END AS item_kind,
    COALESCE(it.subject_id, it.event_id) AS item_id,
    s.urn AS subject_urn,
    s.type AS subject_type,
    it.tag,
    it.created_at
   FROM bsk.item_tag it
     LEFT JOIN bsk.subject s ON s.id = it.subject_id;

-- MEASURE-4 (D20): one row per live lab visit — the newest version of each visit id by recorded_at, unless voided. collected_at is when the sample was taken; collected_on / collected_local read it in the home zone. report_id is the report (a PDF or photo), served by /api/artifacts/{id}/content, null without one. Its results are the readings in bsk.v_metric_reading with this visit_id.
CREATE VIEW bsk.v_lab_visit AS
 WITH version AS (
         SELECT e.id AS event_id,
            e.payload -> 'lab_visit'::text AS v,
            e.artifact_id,
            e.occurred_at,
            e.recorded_at,
            row_number() OVER w_desc AS rn,
            count(*) OVER w_all AS versions,
            min(e.recorded_at) OVER w_all AS first_logged_at
           FROM bsk.event e
          WHERE e.kind = 'measurement'::text AND jsonb_exists(e.payload, 'lab_visit'::text) AND jsonb_typeof(e.payload -> 'lab_visit'::text) = 'object'::text AND ((e.payload -> 'lab_visit'::text) ->> 'id'::text) ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'::text
          WINDOW w_all AS (PARTITION BY ((e.payload -> 'lab_visit'::text) ->> 'id'::text)), w_desc AS (PARTITION BY ((e.payload -> 'lab_visit'::text) ->> 'id'::text) ORDER BY e.recorded_at DESC, e.id DESC)
        ), live AS (
         SELECT v.event_id,
            v.v,
            v.artifact_id,
            v.occurred_at,
            v.recorded_at,
            v.rn,
            v.versions,
            v.first_logged_at,
                CASE
                    WHEN (v.v ->> 'report'::text) ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'::text THEN (v.v ->> 'report'::text)::uuid
                    ELSE v.artifact_id
                END AS report
           FROM version v
          WHERE v.rn = 1 AND COALESCE(v.v ->> 'voided'::text, 'false'::text) <> 'true'::text
        )
 SELECT (l.v ->> 'id'::text)::uuid AS id,
    NULLIF(btrim(
        CASE
            WHEN jsonb_typeof(l.v -> 'lab'::text) = 'string'::text THEN l.v ->> 'lab'::text
            ELSE NULL::text
        END), ''::text) AS lab,
    NULLIF(btrim(
        CASE
            WHEN jsonb_typeof(l.v -> 'note'::text) = 'string'::text THEN l.v ->> 'note'::text
            ELSE NULL::text
        END), ''::text) AS note,
    l.occurred_at AS collected_at,
    (l.occurred_at AT TIME ZONE bsk.home_zone())::date AS collected_on,
    (l.occurred_at AT TIME ZONE bsk.home_zone()) AS collected_local,
    a.id AS report_id,
    a.filename AS report_filename,
    a.content_type AS report_content_type,
    a.byte_size AS report_byte_size,
    l.first_logged_at,
    l.recorded_at AS logged_at,
    l.versions::integer AS versions,
    l.event_id
   FROM live l
     LEFT JOIN bsk.v_artifact a ON a.id = l.report AND a.has_bytes;

-- REWARD-1/3/5 (D11): every ledger entry, one row per currency, with the item that earned it or the Reward it bought. Signed amounts: earn > 0, reversal and redeem < 0.
CREATE VIEW bsk.v_ledger AS
 SELECT e.id AS event_id,
    e.occurred_at,
    e.recorded_at,
    e.provenance,
    e.source_id,
    e.payload ->> 'entry'::text AS entry,
    a.key AS currency,
    ((a.value #>> '{}'::text[])::numeric)::bigint AS amount,
    e.payload ->> 'award_key'::text AS award_key,
    e.payload ->> 'trigger'::text AS trigger,
    s.id AS subject_id,
    s.urn AS subject_urn,
    s.type AS subject_type,
    s.title AS subject_title,
    e.payload ->> 'note'::text AS note,
    (e.payload ->> 'dollars'::text)::numeric AS dollars,
    (e.payload ->> 'coin_rate'::text)::numeric AS coin_rate,
    e.derived_from
   FROM bsk.event e
     CROSS JOIN LATERAL jsonb_each(
        CASE
            WHEN jsonb_typeof(e.payload -> 'amounts'::text) = 'object'::text THEN e.payload -> 'amounts'::text
            ELSE '{}'::jsonb
        END) a(key, value)
     LEFT JOIN bsk.subject s ON s.id = NULLIF(e.payload ->> 'subject_id'::text, ''::text)::uuid
  WHERE e.kind = 'ledger'::text AND jsonb_typeof(a.value) = 'number'::text;

-- FOOD-1 / FOOD-6 (D13): one row per live food entry — the newest version of each meal id by recorded_at, unless it is voided. score is 1 to 5 in half points (numeric). eaten_on / eaten_local read eaten_at in the home zone (bsk.home_zone()); alcoholic marks a drink (one entry, one drink).
CREATE VIEW bsk.v_meal AS
 WITH version AS (
         SELECT e.id AS event_id,
            ((e.payload -> 'meal'::text) ->> 'id'::text)::uuid AS meal_id,
            e.payload -> 'meal'::text AS m,
            e.occurred_at,
            e.recorded_at,
            row_number() OVER w_desc AS rn,
            count(*) OVER w_all AS versions,
            min(e.recorded_at) OVER w_all AS first_logged_at
           FROM bsk.event e
          WHERE e.kind = 'measurement'::text AND jsonb_exists(e.payload, 'meal'::text) AND jsonb_typeof(e.payload -> 'meal'::text) = 'object'::text AND ((e.payload -> 'meal'::text) ->> 'id'::text) ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'::text
          WINDOW w_all AS (PARTITION BY ((e.payload -> 'meal'::text) ->> 'id'::text)), w_desc AS (PARTITION BY ((e.payload -> 'meal'::text) ->> 'id'::text) ORDER BY e.recorded_at DESC, e.id DESC)
        )
 SELECT meal_id,
    occurred_at AS eaten_at,
    (occurred_at AT TIME ZONE bsk.home_zone())::date AS eaten_on,
    (occurred_at AT TIME ZONE bsk.home_zone()) AS eaten_local,
    m ->> 'name'::text AS name,
        CASE
            WHEN jsonb_typeof(m -> 'score'::text) = 'number'::text THEN (m ->> 'score'::text)::numeric
            ELSE NULL::numeric
        END AS score,
        CASE
            WHEN jsonb_typeof(m -> 'calories'::text) = 'number'::text THEN ((m ->> 'calories'::text)::numeric)::integer
            ELSE NULL::integer
        END AS calories,
        CASE
            WHEN jsonb_typeof(m -> 'protein_g'::text) = 'number'::text THEN ((m ->> 'protein_g'::text)::numeric)::integer
            ELSE NULL::integer
        END AS protein_g,
        CASE
            WHEN jsonb_typeof(m -> 'carbs_g'::text) = 'number'::text THEN ((m ->> 'carbs_g'::text)::numeric)::integer
            ELSE NULL::integer
        END AS carbs_g,
        CASE
            WHEN jsonb_typeof(m -> 'fat_g'::text) = 'number'::text THEN ((m ->> 'fat_g'::text)::numeric)::integer
            ELSE NULL::integer
        END AS fat_g,
    m ->> 'meal_type'::text AS meal_type,
        CASE
            WHEN (m ->> 'photo'::text) ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'::text THEN (m ->> 'photo'::text)::uuid
            ELSE NULL::uuid
        END AS photo_artifact_id,
    first_logged_at,
    recorded_at AS logged_at,
    versions::integer AS versions,
    event_id,
    COALESCE((m -> 'alcoholic'::text) = 'true'::jsonb, false) AS alcoholic
   FROM version v
  WHERE rn = 1 AND COALESCE(m ->> 'voided'::text, 'false'::text) <> 'true'::text;

-- MEASURE-1 / MEASURE-3 / MEASURE-4 (D20): one row per Metric subject — its name, unit, decimals (0-3, 1 when missing or malformed), optional target, which way is better, the starter-list key it was made from, when it was archived (null while it is not), its normal range (low and high, either or both; null unless a number), and the lab panel it is on (cmp, lipids, a1c or other; null for a metric that is not a lab test). Day values, the trend, the goal date and a reading's high or low flag are computed in C# from this and bsk.v_metric_reading.
CREATE VIEW bsk.v_metric AS
 SELECT m.id,
    m.urn,
    m.title AS name,
        CASE
            WHEN jsonb_typeof(m.attributes -> 'unit'::text) = 'string'::text THEN btrim(m.attributes ->> 'unit'::text)
            ELSE ''::text
        END AS unit,
        CASE
            WHEN jsonb_typeof(m.attributes -> 'decimals'::text) = 'number'::text AND ((m.attributes ->> 'decimals'::text)::numeric) >= 0::numeric AND ((m.attributes ->> 'decimals'::text)::numeric) <= 3::numeric AND ((m.attributes ->> 'decimals'::text)::numeric) = trunc((m.attributes ->> 'decimals'::text)::numeric) THEN ((m.attributes ->> 'decimals'::text)::numeric)::integer
            ELSE 1
        END AS decimals,
    bsk.json_num(m.attributes -> 'target'::text) AS target,
        CASE
            WHEN (m.attributes ->> 'better'::text) = ANY (ARRAY['lower'::text, 'higher'::text]) THEN m.attributes ->> 'better'::text
            ELSE NULL::text
        END AS better,
        CASE
            WHEN jsonb_typeof(m.attributes -> 'catalog'::text) = 'string'::text THEN NULLIF(btrim(m.attributes ->> 'catalog'::text), ''::text)
            ELSE NULL::text
        END AS catalog,
        CASE
            WHEN a.archived THEN a.occurred_at
            ELSE NULL::timestamp with time zone
        END AS archived_at,
        CASE
            WHEN a.archived THEN (a.occurred_at AT TIME ZONE bsk.home_zone())::date
            ELSE NULL::date
        END AS archived_on,
    m.created_at,
    bsk.json_num(m.attributes -> 'low'::text) AS low,
    bsk.json_num(m.attributes -> 'high'::text) AS high,
        CASE
            WHEN (m.attributes ->> 'panel'::text) = ANY (ARRAY['cmp'::text, 'lipids'::text, 'a1c'::text, 'other'::text]) THEN m.attributes ->> 'panel'::text
            ELSE NULL::text
        END AS panel
   FROM bsk.subject m
     LEFT JOIN LATERAL ( SELECT COALESCE((e.payload ->> 'archived'::text)::boolean, false) AS archived,
            e.occurred_at
           FROM bsk.event e
          WHERE e.kind = 'archive_change'::text AND (e.payload ->> 'subject_id'::text) = m.id::text
          ORDER BY e.occurred_at DESC, e.recorded_at DESC, e.id DESC
         LIMIT 1) a ON true
  WHERE m.type = 'Metric'::text;

-- MEASURE-1 / MEASURE-4 (D20): one row per live reading of a metric — the newest version of each reading id by recorded_at, unless voided. taken_at is when it was taken; taken_on / taken_local read it in the home zone (bsk.home_zone()). unit is the metric's unit when the reading was written. visit_id is the lab visit a result belongs to, and low / high the range printed beside it on the report (null when none).
CREATE VIEW bsk.v_metric_reading AS
 WITH version AS (
         SELECT e.id AS event_id,
            e.payload -> 'metric_reading'::text AS r,
            e.occurred_at,
            e.recorded_at,
            row_number() OVER w_desc AS rn,
            count(*) OVER w_all AS versions,
            min(e.recorded_at) OVER w_all AS first_logged_at
           FROM bsk.event e
          WHERE e.kind = 'measurement'::text AND jsonb_exists(e.payload, 'metric_reading'::text) AND jsonb_typeof(e.payload -> 'metric_reading'::text) = 'object'::text AND ((e.payload -> 'metric_reading'::text) ->> 'id'::text) ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'::text
          WINDOW w_all AS (PARTITION BY ((e.payload -> 'metric_reading'::text) ->> 'id'::text)), w_desc AS (PARTITION BY ((e.payload -> 'metric_reading'::text) ->> 'id'::text) ORDER BY e.recorded_at DESC, e.id DESC)
        ), live AS (
         SELECT v.event_id,
            v.r,
            v.occurred_at,
            v.recorded_at,
            v.rn,
            v.versions,
            v.first_logged_at
           FROM version v
          WHERE v.rn = 1 AND COALESCE(v.r ->> 'voided'::text, 'false'::text) <> 'true'::text AND (v.r ->> 'metric'::text) ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'::text AND jsonb_typeof(v.r -> 'value'::text) = 'number'::text
        )
 SELECT (l.r ->> 'id'::text)::uuid AS id,
    m.id AS metric_id,
    (l.r ->> 'value'::text)::numeric AS value,
        CASE
            WHEN jsonb_typeof(l.r -> 'unit'::text) = 'string'::text THEN btrim(l.r ->> 'unit'::text)
            ELSE ''::text
        END AS unit,
    NULLIF(btrim(
        CASE
            WHEN jsonb_typeof(l.r -> 'note'::text) = 'string'::text THEN l.r ->> 'note'::text
            ELSE NULL::text
        END), ''::text) AS note,
    l.occurred_at AS taken_at,
    (l.occurred_at AT TIME ZONE bsk.home_zone())::date AS taken_on,
    (l.occurred_at AT TIME ZONE bsk.home_zone()) AS taken_local,
    l.first_logged_at,
    l.recorded_at AS logged_at,
    l.versions::integer AS versions,
    l.event_id,
        CASE
            WHEN (l.r ->> 'visit'::text) ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'::text THEN (l.r ->> 'visit'::text)::uuid
            ELSE NULL::uuid
        END AS visit_id,
    bsk.json_num(l.r -> 'low'::text) AS low,
    bsk.json_num(l.r -> 'high'::text) AS high
   FROM live l
     JOIN bsk.subject m ON m.id = ((l.r ->> 'metric'::text)::uuid) AND m.type = 'Metric'::text;

-- ACTIVITY-6 (D14): one row per live tag from the Oura app — the newest version of each Oura id by recorded_at, unless voided. day is the day it starts; ended_at is null for a moment. Its name is C#'s OuraTags.Name.
CREATE VIEW bsk.v_oura_tag AS
 WITH version AS (
         SELECT e.id AS event_id,
            e.payload -> 'oura_tag'::text AS t,
            e.recorded_at,
            row_number() OVER (PARTITION BY ((e.payload -> 'oura_tag'::text) ->> 'id'::text) ORDER BY e.recorded_at DESC, e.id DESC) AS rn
           FROM bsk.event e
          WHERE e.kind = 'observation'::text AND e.source_id = 'oura'::text AND jsonb_exists(e.payload, 'oura_tag'::text) AND jsonb_typeof(e.payload -> 'oura_tag'::text) = 'object'::text AND COALESCE((e.payload -> 'oura_tag'::text) ->> 'id'::text, ''::text) <> ''::text
        )
 SELECT t ->> 'id'::text AS tag_id,
    bsk.try_to_date(t ->> 'day'::text) AS day,
    bsk.try_to_date(t ->> 'end_day'::text) AS end_day,
    NULLIF(btrim(t ->> 'type_code'::text), ''::text) AS type_code,
    NULLIF(btrim(t ->> 'custom_name'::text), ''::text) AS custom_name,
    (t ->> 'start'::text)::timestamp with time zone AS started_at,
        CASE
            WHEN jsonb_typeof(t -> 'end'::text) = 'string'::text THEN (t ->> 'end'::text)::timestamp with time zone
            ELSE NULL::timestamp with time zone
        END AS ended_at,
    NULLIF(btrim(t ->> 'comment'::text), ''::text) AS comment,
    recorded_at AS synced_at,
    event_id
   FROM version v
  WHERE rn = 1 AND COALESCE(t ->> 'voided'::text, 'false'::text) <> 'true'::text AND bsk.try_to_date(t ->> 'day'::text) IS NOT NULL AND jsonb_typeof(t -> 'start'::text) = 'string'::text;

-- Flattened People-association links (attributes.people): which Person is involved in which subject, and in what role. A property, not an alignment edge.
CREATE VIEW bsk.v_person_association AS
 SELECT s.id AS subject_id,
    s.urn AS subject_urn,
    s.type AS subject_type,
    s.title AS subject_title,
    assoc.value ->> 'role'::text AS role,
    p.id AS person_id,
    p.urn AS person_urn,
    p.title AS person_name
   FROM bsk.subject s
     CROSS JOIN LATERAL jsonb_array_elements(s.attributes -> 'people'::text) assoc(value)
     JOIN bsk.subject p ON p.urn = (assoc.value ->> 'person'::text) AND p.type = 'Person'::text
  WHERE jsonb_typeof(s.attributes -> 'people'::text) = 'array'::text AND NOT bsk.is_archived(s.id);

-- ACTIVITY-4 (D14): one row per day with a readiness score — the newest version by recorded_at: the score, its contributors and the temperature deviation in degrees C.
CREATE VIEW bsk.v_readiness_day AS
 SELECT DISTINCT ON (((payload -> 'oura_readiness'::text) ->> 'day'::text)) bsk.try_to_date((payload -> 'oura_readiness'::text) ->> 'day'::text) AS day,
    bsk.json_int((payload -> 'oura_readiness'::text) -> 'score'::text) AS score,
        CASE
            WHEN jsonb_typeof((payload -> 'oura_readiness'::text) -> 'contributors'::text) = 'object'::text THEN (payload -> 'oura_readiness'::text) -> 'contributors'::text
            ELSE '{}'::jsonb
        END AS contributors,
    bsk.json_num((payload -> 'oura_readiness'::text) -> 'temperature_deviation'::text) AS temperature_deviation,
    bsk.json_num((payload -> 'oura_readiness'::text) -> 'temperature_trend_deviation'::text) AS temperature_trend_deviation,
    recorded_at AS synced_at,
    id AS event_id
   FROM bsk.event e
  WHERE kind = 'measurement'::text AND source_id = 'oura'::text AND jsonb_exists(payload, 'oura_readiness'::text) AND jsonb_typeof(payload -> 'oura_readiness'::text) = 'object'::text AND bsk.try_to_date((payload -> 'oura_readiness'::text) ->> 'day'::text) IS NOT NULL
  ORDER BY ((payload -> 'oura_readiness'::text) ->> 'day'::text), recorded_at DESC, id DESC;

-- REVIEW-1/REVIEW-2 (D3, D8): one row per Review — its kind (daily, weekly), its period (the day, or the Sunday naming the Monday-to-Sunday week), the folded status, keep_focus, when the current Completed was set and when it was first completed (first_completed_on in the home zone), and the newest reflection link (reviews plan §4.1: its vault path, vault, summary and when it was recorded). Missed is computed from these on read.
CREATE VIEW bsk.v_review AS
 SELECT r.id,
    r.urn,
    r.title,
    r.attributes ->> 'kind'::text AS kind,
    bsk.try_to_date(r.attributes ->> 'period'::text) AS period,
    scs.status,
    (r.attributes ->> 'keep_focus'::text) IS DISTINCT FROM 'false'::text AS keep_focus,
        CASE
            WHEN lower(scs.status) = 'completed'::text THEN scs.status_occurred_at
            ELSE NULL::timestamp with time zone
        END AS completed_at,
    fc.first_completed_at,
    (fc.first_completed_at AT TIME ZONE bsk.home_zone())::date AS first_completed_on,
    bsk.is_archived(r.id) AS archived,
    r.created_at,
    rf.reflection ->> 'path'::text AS reflection_path,
    rf.reflection ->> 'vault'::text AS reflection_vault,
    rf.reflection ->> 'summary'::text AS reflection_summary,
    rf.recorded_at AS reflection_at
   FROM bsk.subject r
     LEFT JOIN bsk_derived.subject_current_source scs ON scs.subject_id = r.id
     LEFT JOIN LATERAL ( SELECT min(e.occurred_at) AS first_completed_at
           FROM bsk.event e
          WHERE e.kind = 'state_change'::text AND (e.payload ->> 'subject_id'::text) = r.id::text AND lower(e.payload ->> 'status'::text) = 'completed'::text) fc ON true
     LEFT JOIN LATERAL ( SELECT e.payload -> 'reflection'::text AS reflection,
            e.recorded_at
           FROM bsk.subject_event se
             JOIN bsk.event e ON e.id = se.event_id
          WHERE se.subject_id = r.id AND se.relation = 'concerns'::text AND e.kind = 'observation'::text AND jsonb_typeof(e.payload -> 'reflection'::text) = 'object'::text
          ORDER BY e.recorded_at DESC, e.id DESC
         LIMIT 1) rf ON true
  WHERE r.type = 'Review'::text AND ((r.attributes ->> 'kind'::text) = ANY (ARRAY['daily'::text, 'weekly'::text])) AND bsk.try_to_date(r.attributes ->> 'period'::text) IS NOT NULL;

-- REWARD-3: the reward catalog — instant rewards with a price, savings goals with a target — and what has been redeemed against each. Savings progress sums the dollars stamped on each deposit, so a coin-rate change never rewrites it.
CREATE VIEW bsk.v_reward AS
 WITH catalog AS (
         SELECT r.id,
            r.urn,
            r.title,
                CASE
                    WHEN lower(TRIM(BOTH FROM r.attributes ->> 'kind'::text)) = 'savings'::text THEN 'savings'::text
                    ELSE 'instant'::text
                END AS kind,
                CASE
                    WHEN jsonb_typeof(r.attributes -> 'price'::text) = 'object'::text THEN r.attributes -> 'price'::text
                    ELSE NULL::jsonb
                END AS price,
                CASE
                    WHEN jsonb_typeof(r.attributes -> 'target_usd'::text) = 'number'::text THEN (r.attributes ->> 'target_usd'::text)::numeric
                    WHEN regexp_replace(r.attributes ->> 'target_usd'::text, '[\s$,]'::text, ''::text, 'g'::text) ~ '^[0-9]+(\.[0-9]+)?$'::text THEN regexp_replace(r.attributes ->> 'target_usd'::text, '[\s$,]'::text, ''::text, 'g'::text)::numeric
                    ELSE NULL::numeric
                END AS target_usd,
            r.attributes ->> 'description'::text AS description,
            r.attributes ->> 'notes'::text AS notes,
            r.created_at
           FROM bsk.subject r
          WHERE r.type = 'Reward'::text
        )
 SELECT c.id,
    c.urn,
    c.title AS name,
    c.kind,
    c.price,
        CASE
            WHEN c.target_usd > 0::numeric THEN c.target_usd
            ELSE NULL::numeric
        END AS target_usd,
    d.redemptions,
    COALESCE(d.coins_redeemed, 0::bigint) AS coins_redeemed,
    COALESCE(d.dollars_redeemed, 0::numeric) AS dollars_redeemed,
    d.last_redeemed_at,
        CASE
            WHEN c.kind = 'savings'::text AND c.target_usd > 0::numeric THEN round(100::numeric * COALESCE(d.dollars_redeemed, 0::numeric) / c.target_usd, 1)
            ELSE NULL::numeric
        END AS percent_of_target,
        CASE
            WHEN c.kind = 'savings'::text AND c.target_usd > 0::numeric THEN GREATEST(c.target_usd - COALESCE(d.dollars_redeemed, 0::numeric), 0::numeric)
            ELSE NULL::numeric
        END AS remaining_usd,
    c.description,
    c.notes,
    bsk.is_archived(c.id) AS archived,
    c.created_at
   FROM catalog c
     LEFT JOIN LATERAL ( SELECT count(*) AS redemptions,
            COALESCE(- sum(
                CASE
                    WHEN jsonb_typeof((e.payload -> 'amounts'::text) -> 'coins'::text) = 'number'::text THEN ((e.payload -> 'amounts'::text) ->> 'coins'::text)::numeric
                    ELSE NULL::numeric
                END), 0::numeric)::bigint AS coins_redeemed,
            COALESCE(sum(
                CASE
                    WHEN jsonb_typeof(e.payload -> 'dollars'::text) = 'number'::text THEN (e.payload ->> 'dollars'::text)::numeric
                    ELSE NULL::numeric
                END), 0::numeric) AS dollars_redeemed,
            max(e.occurred_at) AS last_redeemed_at
           FROM bsk.event e
          WHERE e.kind = 'ledger'::text AND (e.payload ->> 'subject_id'::text) = c.id::text AND (e.payload ->> 'entry'::text) = 'redeem'::text) d ON true;

-- REWARD-1/2 (D11): the awards due right now — v_reward_trigger rows on or after bsk.ledger_opened_at(). Reconcile earns each due key the ledger has not paid and reverses each paid key that is no longer due.
CREATE VIEW bsk.v_reward_due AS
 SELECT subject_id,
    subject_type,
    award_key,
    trigger,
    source_event_ids,
    triggered_at,
    timed_seconds,
    occurred_on
   FROM bsk.v_reward_trigger t
  WHERE triggered_at >= bsk.ledger_opened_at();

-- REWARD-1/2/6 (D11): every award the record supports, one row per award key, with the events that make it due — Task/Project/Goal completed; Task timed (timer time before the completion, in timed_seconds); Habit established, days followed / partial (up to graduation, and after it while the habit is Established), and 7 / 30 / 100 streaks (up to graduation). occurred_on is the award's own local date (a habit day's date, a streak's last day), NULL when it has none. Not yet filtered by the wallet's start line; see v_reward_due.
CREATE VIEW bsk.v_reward_trigger AS
 SELECT c.subject_id,
    c.type AS subject_type,
    'completed:'::text || c.subject_id::text AS award_key,
    'completed'::text AS trigger,
    ARRAY[c.status_event_id] AS source_event_ids,
    c.status_occurred_at AS triggered_at,
    NULL::bigint AS timed_seconds,
    NULL::date AS occurred_on
   FROM bsk_derived.subject_current_source c
  WHERE (c.type = ANY (ARRAY['Task'::text, 'Project'::text, 'Goal'::text])) AND lower(btrim(c.status)) = 'completed'::text
UNION ALL
 SELECT c.subject_id,
    c.type AS subject_type,
    'timed:'::text || c.subject_id::text AS award_key,
    'timed'::text AS trigger,
    ARRAY[c.status_event_id] || t.event_ids AS source_event_ids,
    c.status_occurred_at AS triggered_at,
    t.seconds AS timed_seconds,
    NULL::date AS occurred_on
   FROM bsk_derived.subject_current_source c
     CROSS JOIN LATERAL ( SELECT floor(sum(EXTRACT(epoch FROM COALESCE(s.next_at, c.status_occurred_at) - s.occurred_at)) FILTER (WHERE s.action = ANY (ARRAY['start'::text, 'resume'::text])))::bigint AS seconds,
            array_agg(s.id ORDER BY s.occurred_at, s.recorded_at, s.id) AS event_ids
           FROM ( SELECT e.id,
                    e.payload ->> 'timer'::text AS action,
                    e.occurred_at,
                    e.recorded_at,
                    lead(e.occurred_at) OVER (ORDER BY e.occurred_at, e.recorded_at, e.id) AS next_at
                   FROM bsk.event e
                  WHERE e.kind = 'activity'::text AND jsonb_exists(e.payload, 'timer'::text) AND (e.payload ->> 'subject_id'::text) = c.subject_id::text AND e.occurred_at <= c.status_occurred_at) s) t
  WHERE c.type = 'Task'::text AND lower(btrim(c.status)) = 'completed'::text AND t.seconds > 0
UNION ALL
 SELECT c.subject_id,
    c.type AS subject_type,
    'established:'::text || c.subject_id::text AS award_key,
    'established'::text AS trigger,
    ARRAY[c.status_event_id] AS source_event_ids,
    c.status_occurred_at AS triggered_at,
    NULL::bigint AS timed_seconds,
    NULL::date AS occurred_on
   FROM bsk_derived.subject_current_source c
  WHERE c.type = 'Habit'::text AND lower(btrim(c.status)) = 'established'::text
UNION ALL
 SELECT o.habit_id AS subject_id,
    'Habit'::text AS subject_type,
    (((('day:'::text || o.habit_id::text) || ':'::text) || to_char(o.occurrence_date::timestamp with time zone, 'YYYY-MM-DD'::text)) || ':'::text) || o.state AS award_key,
    o.state AS trigger,
    ARRAY[o.adherence_event_id] AS source_event_ids,
    o.adherence_at AS triggered_at,
    NULL::bigint AS timed_seconds,
    o.occurrence_date AS occurred_on
   FROM bsk_derived.habit_occurrence_source o
     LEFT JOIN bsk_derived.subject_current_source c ON c.subject_id = o.habit_id
  WHERE (o.state = ANY (ARRAY['followed'::text, 'partial'::text])) AND (o.graduated_on IS NULL OR o.occurrence_date <= o.graduated_on OR lower(btrim(c.status)) = 'established'::text)
UNION ALL
 SELECT m.habit_id AS subject_id,
    'Habit'::text AS subject_type,
    (((('streak_'::text || m.n) || ':'::text) || m.habit_id::text) || ':'::text) || to_char(m.run_start::timestamp with time zone, 'YYYY-MM-DD'::text) AS award_key,
    'streak_'::text || m.n AS trigger,
    m.source_event_ids,
    m.triggered_at,
    NULL::bigint AS timed_seconds,
    m.last_date AS occurred_on
   FROM ( SELECT r.habit_id,
            n.n,
            r.run_start,
            array_agg(r.adherence_event_id ORDER BY r.occurrence_date) AS source_event_ids,
            max(r.adherence_at) AS triggered_at,
            max(r.occurrence_date) AS last_date
           FROM ( SELECT f.habit_id,
                    f.occurrence_date,
                    f.state,
                    f.adherence_event_id,
                    f.adherence_at,
                    f.seq,
                    f.island,
                    min(f.occurrence_date) OVER (PARTITION BY f.habit_id, f.island) AS run_start,
                    row_number() OVER (PARTITION BY f.habit_id, f.island ORDER BY f.occurrence_date) AS run_day
                   FROM ( SELECT s.habit_id,
                            s.occurrence_date,
                            s.state,
                            s.adherence_event_id,
                            s.adherence_at,
                            s.seq,
                            s.seq - row_number() OVER (PARTITION BY s.habit_id ORDER BY s.occurrence_date) AS island
                           FROM ( SELECT o.habit_id,
                                    o.occurrence_date,
                                    o.state,
                                    o.adherence_event_id,
                                    o.adherence_at,
                                    row_number() OVER (PARTITION BY o.habit_id ORDER BY o.occurrence_date) AS seq
                                   FROM bsk_derived.habit_occurrence_source o
                                  WHERE o.graduated_on IS NULL OR o.occurrence_date <= o.graduated_on) s
                          WHERE s.state = 'followed'::text) f) r
             CROSS JOIN ( VALUES (7), (30), (100)) n(n)
          WHERE r.run_day <= n.n
          GROUP BY r.habit_id, n.n, r.run_start
         HAVING count(*) = n.n) m;

-- ACTIVITY-4 (D14): one row per day with a sleep score or a sleep period — the score and its contributors; the night (the longest long_sleep): bed and wake times and its stages; and the other periods as naps.
CREATE VIEW bsk.v_sleep_day AS
 WITH score AS (
         SELECT DISTINCT ON (((e.payload -> 'oura_sleep_day'::text) ->> 'day'::text)) bsk.try_to_date((e.payload -> 'oura_sleep_day'::text) ->> 'day'::text) AS day,
            bsk.json_int((e.payload -> 'oura_sleep_day'::text) -> 'score'::text) AS score,
                CASE
                    WHEN jsonb_typeof((e.payload -> 'oura_sleep_day'::text) -> 'contributors'::text) = 'object'::text THEN (e.payload -> 'oura_sleep_day'::text) -> 'contributors'::text
                    ELSE '{}'::jsonb
                END AS contributors,
            e.recorded_at,
            e.id AS event_id
           FROM bsk.event e
          WHERE e.kind = 'measurement'::text AND e.source_id = 'oura'::text AND jsonb_exists(e.payload, 'oura_sleep_day'::text) AND jsonb_typeof(e.payload -> 'oura_sleep_day'::text) = 'object'::text AND bsk.try_to_date((e.payload -> 'oura_sleep_day'::text) ->> 'day'::text) IS NOT NULL
          ORDER BY ((e.payload -> 'oura_sleep_day'::text) ->> 'day'::text), e.recorded_at DESC, e.id DESC
        ), night AS (
         SELECT DISTINCT ON (p_1.day) p_1.period_id,
            p_1.day,
            p_1.type,
            p_1.started_at,
            p_1.ended_at,
            p_1.in_bed_s,
            p_1.asleep_s,
            p_1.deep_s,
            p_1.light_s,
            p_1.rem_s,
            p_1.awake_s,
            p_1.latency_s,
            p_1.efficiency,
            p_1.average_hrv,
            p_1.average_hr,
            p_1.lowest_hr,
            p_1.synced_at,
            p_1.event_id
           FROM bsk.v_sleep_period p_1
          WHERE p_1.type = 'long_sleep'::text
          ORDER BY p_1.day, p_1.asleep_s DESC NULLS LAST, p_1.in_bed_s DESC NULLS LAST, p_1.started_at, p_1.period_id
        ), nap AS (
         SELECT p_1.day,
            count(*)::integer AS naps,
            COALESCE(sum(p_1.asleep_s), 0::bigint)::integer AS nap_s,
            max(p_1.synced_at) AS synced_at,
            array_agg(p_1.event_id ORDER BY p_1.started_at, p_1.event_id) AS event_ids
           FROM bsk.v_sleep_period p_1
             LEFT JOIN night n_1 ON n_1.period_id = p_1.period_id
          WHERE n_1.period_id IS NULL
          GROUP BY p_1.day
        ), days AS (
         SELECT score.day
           FROM score
        UNION
         SELECT night.day
           FROM night
        UNION
         SELECT nap.day
           FROM nap
        )
 SELECT d.day,
    s.score,
    COALESCE(s.contributors, '{}'::jsonb) AS contributors,
    n.started_at AS bed_at,
    n.ended_at AS woke_at,
    n.in_bed_s,
    n.asleep_s,
    n.deep_s,
    n.light_s,
    n.rem_s,
    n.awake_s,
    n.efficiency,
    n.average_hrv,
    n.lowest_hr,
    COALESCE(p.naps, 0) AS naps,
    COALESCE(p.nap_s, 0) AS nap_s,
    GREATEST(s.recorded_at, n.synced_at, p.synced_at) AS synced_at,
    array_remove(ARRAY[s.event_id, n.event_id], NULL::uuid) || COALESCE(p.event_ids, '{}'::uuid[]) AS event_ids
   FROM days d
     LEFT JOIN score s ON s.day = d.day
     LEFT JOIN night n ON n.day = d.day
     LEFT JOIN nap p ON p.day = d.day;

-- ACTIVITY-4 (D14): one row per live Oura sleep period — the newest version of each Oura id by recorded_at — leaving out voided, rejected (rest) and deleted ones. day is the morning it counts on; durations in seconds.
CREATE VIEW bsk.v_sleep_period AS
 WITH version AS (
         SELECT e.id AS event_id,
            e.payload -> 'oura_sleep'::text AS s,
            e.recorded_at,
            row_number() OVER (PARTITION BY ((e.payload -> 'oura_sleep'::text) ->> 'id'::text) ORDER BY e.recorded_at DESC, e.id DESC) AS rn
           FROM bsk.event e
          WHERE e.kind = 'measurement'::text AND e.source_id = 'oura'::text AND jsonb_exists(e.payload, 'oura_sleep'::text) AND jsonb_typeof(e.payload -> 'oura_sleep'::text) = 'object'::text AND COALESCE((e.payload -> 'oura_sleep'::text) ->> 'id'::text, ''::text) <> ''::text
        )
 SELECT s ->> 'id'::text AS period_id,
    bsk.try_to_date(s ->> 'day'::text) AS day,
    COALESCE(s ->> 'type'::text, 'unknown'::text) AS type,
    (s ->> 'start'::text)::timestamp with time zone AS started_at,
    (s ->> 'end'::text)::timestamp with time zone AS ended_at,
    bsk.json_int(s -> 'in_bed_s'::text) AS in_bed_s,
    bsk.json_int(s -> 'asleep_s'::text) AS asleep_s,
    bsk.json_int(s -> 'deep_s'::text) AS deep_s,
    bsk.json_int(s -> 'light_s'::text) AS light_s,
    bsk.json_int(s -> 'rem_s'::text) AS rem_s,
    bsk.json_int(s -> 'awake_s'::text) AS awake_s,
    bsk.json_int(s -> 'latency_s'::text) AS latency_s,
    bsk.json_int(s -> 'efficiency'::text) AS efficiency,
    bsk.json_int(s -> 'average_hrv'::text) AS average_hrv,
    bsk.json_num(s -> 'average_hr'::text) AS average_hr,
    bsk.json_int(s -> 'lowest_hr'::text) AS lowest_hr,
    recorded_at AS synced_at,
    event_id
   FROM version v
  WHERE rn = 1 AND COALESCE(s ->> 'voided'::text, 'false'::text) <> 'true'::text AND (COALESCE(s ->> 'type'::text, ''::text) <> ALL (ARRAY['rest'::text, 'deleted'::text])) AND bsk.try_to_date(s ->> 'day'::text) IS NOT NULL AND jsonb_typeof(s -> 'start'::text) = 'string'::text AND jsonb_typeof(s -> 'end'::text) = 'string'::text;

-- HOME-1, HOME-4 (D18): one row per Space subject — its name, weight (1-10) and pace (1-5), each 3 when missing or malformed, when it was archived (null while it is not), and its shots (attributes.shots, [] when missing). The Home Score is computed in C# from this and bsk.v_space_rating.
CREATE VIEW bsk.v_space AS
 SELECT s.id,
    s.urn,
    s.title AS name,
        CASE
            WHEN jsonb_typeof(s.attributes -> 'weight'::text) = 'number'::text AND ((s.attributes ->> 'weight'::text)::numeric) >= 1::numeric AND ((s.attributes ->> 'weight'::text)::numeric) <= 10::numeric AND ((s.attributes ->> 'weight'::text)::numeric) = trunc((s.attributes ->> 'weight'::text)::numeric) THEN ((s.attributes ->> 'weight'::text)::numeric)::integer
            ELSE 3
        END AS weight,
        CASE
            WHEN jsonb_typeof(s.attributes -> 'pace'::text) = 'number'::text AND ((s.attributes ->> 'pace'::text)::numeric) >= 1::numeric AND ((s.attributes ->> 'pace'::text)::numeric) <= 5::numeric AND ((s.attributes ->> 'pace'::text)::numeric) = trunc((s.attributes ->> 'pace'::text)::numeric) THEN ((s.attributes ->> 'pace'::text)::numeric)::integer
            ELSE 3
        END AS pace,
        CASE
            WHEN a.archived THEN a.occurred_at
            ELSE NULL::timestamp with time zone
        END AS archived_at,
        CASE
            WHEN a.archived THEN (a.occurred_at AT TIME ZONE bsk.home_zone())::date
            ELSE NULL::date
        END AS archived_on,
    s.created_at,
        CASE
            WHEN jsonb_typeof(s.attributes -> 'shots'::text) = 'array'::text THEN s.attributes -> 'shots'::text
            ELSE '[]'::jsonb
        END AS shots
   FROM bsk.subject s
     LEFT JOIN LATERAL ( SELECT COALESCE((e.payload ->> 'archived'::text)::boolean, false) AS archived,
            e.occurred_at
           FROM bsk.event e
          WHERE e.kind = 'archive_change'::text AND (e.payload ->> 'subject_id'::text) = s.id::text
          ORDER BY e.occurred_at DESC, e.recorded_at DESC, e.id DESC
         LIMIT 1) a ON true
  WHERE s.type = 'Space'::text;

-- HOME-4 (D18): one row per live photo of a space — the newest version of each photo id by recorded_at, unless voided. shot_id is the spot it was taken from (main when left out); day is a home-zone date; artifact_id is the photo, served by /api/artifacts/{id}/content. Photos never feed the Home Score.
CREATE VIEW bsk.v_space_photo AS
 WITH version AS (
         SELECT e.id AS event_id,
            e.payload -> 'space_photo'::text AS p,
            e.artifact_id,
            e.recorded_at,
            row_number() OVER (PARTITION BY ((e.payload -> 'space_photo'::text) ->> 'id'::text) ORDER BY e.recorded_at DESC, e.id DESC) AS rn,
            min(e.recorded_at) OVER (PARTITION BY ((e.payload -> 'space_photo'::text) ->> 'id'::text)) AS added_at
           FROM bsk.event e
          WHERE e.kind = 'observation'::text AND jsonb_exists(e.payload, 'space_photo'::text) AND jsonb_typeof(e.payload -> 'space_photo'::text) = 'object'::text AND ((e.payload -> 'space_photo'::text) ->> 'id'::text) ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'::text
        ), live AS (
         SELECT v.event_id,
            v.p,
            v.artifact_id,
            v.recorded_at,
            v.rn,
            v.added_at,
                CASE
                    WHEN (v.p ->> 'photo'::text) ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'::text THEN (v.p ->> 'photo'::text)::uuid
                    ELSE v.artifact_id
                END AS photo
           FROM version v
          WHERE v.rn = 1 AND COALESCE(v.p ->> 'voided'::text, 'false'::text) <> 'true'::text AND (v.p ->> 'space'::text) ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'::text AND bsk.try_to_date(v.p ->> 'day'::text) IS NOT NULL
        )
 SELECT (l.p ->> 'id'::text)::uuid AS id,
    s.id AS space_id,
    COALESCE(NULLIF(btrim(
        CASE
            WHEN jsonb_typeof(l.p -> 'shot'::text) = 'string'::text THEN l.p ->> 'shot'::text
            ELSE NULL::text
        END), ''::text), 'main'::text) AS shot_id,
    bsk.try_to_date(l.p ->> 'day'::text) AS day,
    a.id AS artifact_id,
    l.added_at,
    l.recorded_at AS changed_at,
    l.event_id
   FROM live l
     JOIN bsk.subject s ON s.id = ((l.p ->> 'space'::text)::uuid) AND s.type = 'Space'::text
     JOIN bsk.artifact a ON a.id = l.photo;

-- HOME-2 (D18): one row per live space rating — the newest version of each rating id by recorded_at, unless voided. day is a home-zone date; each part is 1-5 in halves or null when left out. Fades and the Home Score are computed in C#.
CREATE VIEW bsk.v_space_rating AS
 WITH version AS (
         SELECT e.id AS event_id,
            e.payload -> 'space_rating'::text AS r,
            e.recorded_at,
            row_number() OVER (PARTITION BY ((e.payload -> 'space_rating'::text) ->> 'id'::text) ORDER BY e.recorded_at DESC, e.id DESC) AS rn
           FROM bsk.event e
          WHERE e.kind = 'measurement'::text AND jsonb_exists(e.payload, 'space_rating'::text) AND jsonb_typeof(e.payload -> 'space_rating'::text) = 'object'::text AND ((e.payload -> 'space_rating'::text) ->> 'id'::text) ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'::text
        ), live AS (
         SELECT v.event_id,
            v.r,
            v.recorded_at,
            v.rn
           FROM version v
          WHERE v.rn = 1 AND COALESCE(v.r ->> 'voided'::text, 'false'::text) <> 'true'::text AND (v.r ->> 'space'::text) ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'::text AND bsk.try_to_date(v.r ->> 'day'::text) IS NOT NULL
        )
 SELECT (l.r ->> 'id'::text)::uuid AS id,
    s.id AS space_id,
    bsk.try_to_date(l.r ->> 'day'::text) AS day,
        CASE
            WHEN jsonb_typeof(l.r -> 'tidiness'::text) = 'number'::text AND ((l.r ->> 'tidiness'::text)::numeric) >= 1::numeric AND ((l.r ->> 'tidiness'::text)::numeric) <= 5::numeric AND (((l.r ->> 'tidiness'::text)::numeric) * 2::numeric) = trunc(((l.r ->> 'tidiness'::text)::numeric) * 2::numeric) THEN (l.r ->> 'tidiness'::text)::numeric
            ELSE NULL::numeric
        END AS tidiness,
        CASE
            WHEN jsonb_typeof(l.r -> 'cleanliness'::text) = 'number'::text AND ((l.r ->> 'cleanliness'::text)::numeric) >= 1::numeric AND ((l.r ->> 'cleanliness'::text)::numeric) <= 5::numeric AND (((l.r ->> 'cleanliness'::text)::numeric) * 2::numeric) = trunc(((l.r ->> 'cleanliness'::text)::numeric) * 2::numeric) THEN (l.r ->> 'cleanliness'::text)::numeric
            ELSE NULL::numeric
        END AS cleanliness,
        CASE
            WHEN jsonb_typeof(l.r -> 'aesthetics'::text) = 'number'::text AND ((l.r ->> 'aesthetics'::text)::numeric) >= 1::numeric AND ((l.r ->> 'aesthetics'::text)::numeric) <= 5::numeric AND (((l.r ->> 'aesthetics'::text)::numeric) * 2::numeric) = trunc(((l.r ->> 'aesthetics'::text)::numeric) * 2::numeric) THEN (l.r ->> 'aesthetics'::text)::numeric
            ELSE NULL::numeric
        END AS aesthetics,
    NULLIF(btrim(
        CASE
            WHEN jsonb_typeof(l.r -> 'note'::text) = 'string'::text THEN l.r ->> 'note'::text
            ELSE NULL::text
        END), ''::text) AS note,
    l.recorded_at AS rated_at,
    l.event_id
   FROM live l
     JOIN bsk.subject s ON s.id = ((l.r ->> 'space'::text)::uuid) AND s.type = 'Space'::text;

CREATE VIEW bsk.v_subject AS
 SELECT id,
    urn,
    type,
    title,
    attributes ->> 'expected_cadence'::text AS expected_cadence,
    bsk.try_to_timestamptz(attributes ->> 'next_review_at'::text) AS next_review_at,
    attributes ->> 'scope'::text AS scope,
    origin_event_id,
    created_at,
    attributes,
    attributes ->> 'statement'::text AS statement,
    bsk.is_archived(id) AS archived,
    attributes ->> 'area'::text AS area
   FROM bsk.subject s;

CREATE VIEW bsk.v_subject_current AS
 SELECT subject_id,
    urn,
    type,
    title,
    status,
    status_event_id,
    status_occurred_at
   FROM bsk_derived.subject_current;

CREATE VIEW bsk.v_subject_event AS
 SELECT se.id,
    se.event_id,
    e.kind AS event_kind,
    se.relation,
    se.subject_id,
    s.urn AS subject_urn,
    s.type AS subject_type,
    se.provenance,
    se.created_at
   FROM bsk.subject_event se
     JOIN bsk.event e ON e.id = se.event_id
     JOIN bsk.subject s ON s.id = se.subject_id;

CREATE VIEW bsk.v_subject_relation AS
 SELECT r.id,
    r.from_subject,
    f.urn AS from_urn,
    f.type AS from_type,
    r.relation,
    r.to_subject,
    t.urn AS to_urn,
    t.type AS to_type,
    r.provenance,
    r.created_at
   FROM bsk.subject_relation r
     JOIN bsk.subject f ON f.id = r.from_subject
     JOIN bsk.subject t ON t.id = r.to_subject;

CREATE VIEW bsk.v_tag_universe AS
 SELECT tag,
    count(*) AS item_count
   FROM bsk.item_tag
  GROUP BY tag;

-- D16: each repetition of a repeating Task, with the date it stands for, its dates, its status folded from source, and the home-zone day it closed (Completed, Missed or Cancelled).
CREATE VIEW bsk.v_task_repetition AS
 SELECT r.id,
    r.urn,
    r.title,
    sr.id AS series_id,
    r.attributes ->> 'series'::text AS series_urn,
    bsk.try_to_date(r.attributes ->> 'occurrence'::text) AS occurrence,
    bsk.try_to_date(r.attributes ->> 'due'::text) AS due,
    bsk.try_to_date(r.attributes ->> 'scheduled'::text) AS scheduled,
    c.status,
    bsk.is_terminal_status(c.status) AS closed,
        CASE
            WHEN bsk.is_terminal_status(c.status) THEN (c.status_occurred_at AT TIME ZONE bsk.home_zone())::date
            ELSE NULL::date
        END AS closed_on,
    bsk.is_archived(r.id) AS archived,
    r.created_at
   FROM bsk.subject r
     LEFT JOIN bsk.subject sr ON sr.urn = (r.attributes ->> 'series'::text)
     LEFT JOIN bsk_derived.subject_current_source c ON c.subject_id = r.id
  WHERE r.type = 'Task'::text AND (r.attributes ->> 'series'::text) IS NOT NULL;

-- D16: each repeating Task's series: its pattern and start, whether it is stopped (Cancelled or archived), and its repetitions counted: open, completed, missed, cancelled, the last completed day, the next open due date and the latest date made.
CREATE VIEW bsk.v_task_series AS
 SELECT s.id,
    s.urn,
    s.title,
    s.attributes -> 'recurrence'::text AS recurrence,
    bsk.try_to_date(s.attributes ->> 'start'::text) AS start_date,
    c.status,
    bsk.is_archived(s.id) AS archived,
    bsk.is_terminal_status(c.status) OR bsk.is_archived(s.id) AS stopped,
    COALESCE(r.open_count, 0::bigint) AS open_count,
    COALESCE(r.completed_count, 0::bigint) AS completed_count,
    COALESCE(r.missed_count, 0::bigint) AS missed_count,
    COALESCE(r.cancelled_count, 0::bigint) AS cancelled_count,
    r.last_completed_on,
    r.next_due,
    r.latest_occurrence
   FROM bsk.subject s
     LEFT JOIN bsk_derived.subject_current_source c ON c.subject_id = s.id
     LEFT JOIN LATERAL ( SELECT count(*) FILTER (WHERE NOT x.closed AND NOT x.archived) AS open_count,
            count(*) FILTER (WHERE lower(x.status) = 'completed'::text) AS completed_count,
            count(*) FILTER (WHERE lower(x.status) = 'missed'::text) AS missed_count,
            count(*) FILTER (WHERE lower(x.status) = 'cancelled'::text) AS cancelled_count,
            max(x.closed_on) FILTER (WHERE lower(x.status) = 'completed'::text) AS last_completed_on,
            min(x.due) FILTER (WHERE NOT x.closed AND NOT x.archived) AS next_due,
            max(x.occurrence) AS latest_occurrence
           FROM bsk.v_task_repetition x
          WHERE x.series_urn = s.urn) r ON true
  WHERE bsk.is_task_series(s.type, s.attributes);

-- TASKS-2: per-task timer state (running / paused / stopped) and recorded time, folded from append-only timer activity events. total_seconds excludes a still-running segment; add now() - running_since.
CREATE VIEW bsk.v_task_timer AS
 WITH timer_event AS (
         SELECT (e.payload ->> 'subject_id'::text)::uuid AS subject_id,
            e.payload ->> 'timer'::text AS action,
            e.occurred_at,
            e.recorded_at,
            e.id
           FROM bsk.event e
          WHERE e.kind = 'activity'::text AND jsonb_exists(e.payload, 'timer'::text)
        ), sequenced AS (
         SELECT t.subject_id,
            t.action,
            t.occurred_at,
            t.recorded_at,
            t.id,
            lead(t.occurred_at) OVER w AS next_at,
            count(*) FILTER (WHERE t.action = 'start'::text) OVER w AS session_no,
            row_number() OVER (PARTITION BY t.subject_id ORDER BY t.occurred_at DESC, t.recorded_at DESC, t.id DESC) AS rn_desc
           FROM timer_event t
          WINDOW w AS (PARTITION BY t.subject_id ORDER BY t.occurred_at, t.recorded_at, t.id)
        ), segment AS (
         SELECT sequenced.subject_id,
            sequenced.session_no,
            EXTRACT(epoch FROM sequenced.next_at - sequenced.occurred_at) AS seconds
           FROM sequenced
          WHERE (sequenced.action = ANY (ARRAY['start'::text, 'resume'::text])) AND sequenced.next_at IS NOT NULL
        ), totals AS (
         SELECT segment.subject_id,
            sum(segment.seconds) AS seconds
           FROM segment
          GROUP BY segment.subject_id
        ), latest AS (
         SELECT sequenced.subject_id,
            sequenced.action,
            sequenced.occurred_at,
            sequenced.session_no
           FROM sequenced
          WHERE sequenced.rn_desc = 1
        ), session_start AS (
         SELECT DISTINCT ON (sequenced.subject_id) sequenced.subject_id,
            sequenced.occurred_at
           FROM sequenced
          WHERE sequenced.action = 'start'::text
          ORDER BY sequenced.subject_id, sequenced.occurred_at DESC, sequenced.recorded_at DESC, sequenced.id DESC
        )
 SELECT l.subject_id,
    s.urn AS subject_urn,
    s.type AS subject_type,
    s.title AS subject_title,
    bsk.is_archived(s.id) AS archived,
        CASE l.action
            WHEN 'start'::text THEN 'running'::text
            WHEN 'resume'::text THEN 'running'::text
            WHEN 'pause'::text THEN 'paused'::text
            ELSE 'stopped'::text
        END AS state,
        CASE
            WHEN l.action = ANY (ARRAY['start'::text, 'resume'::text]) THEN l.occurred_at
            ELSE NULL::timestamp with time zone
        END AS running_since,
    floor(COALESCE(tot.seconds, 0::numeric))::bigint AS total_seconds,
    floor(COALESCE(( SELECT sum(g.seconds) AS sum
           FROM segment g
          WHERE g.subject_id = l.subject_id AND g.session_no = l.session_no), 0::numeric))::bigint AS session_seconds,
    ss.occurred_at AS session_started_at,
    l.session_no AS sessions,
    l.occurred_at AS last_changed_at
   FROM latest l
     JOIN bsk.subject s ON s.id = l.subject_id
     LEFT JOIN totals tot ON tot.subject_id = l.subject_id
     LEFT JOIN session_start ss ON ss.subject_id = l.subject_id;

-- REWARD-1/3 (D11): the balance of each currency touched by the ledger, with earned, reversed and spent totals.
CREATE VIEW bsk.v_wallet AS
 SELECT currency,
    COALESCE(sum(amount) FILTER (WHERE entry = 'earn'::text), 0::numeric)::bigint AS earned,
    COALESCE(- sum(amount) FILTER (WHERE entry = 'reversal'::text), 0::numeric)::bigint AS reversed,
    COALESCE(- sum(amount) FILTER (WHERE entry = 'redeem'::text), 0::numeric)::bigint AS spent,
    sum(amount)::bigint AS balance,
    max(occurred_at) AS last_changed_at
   FROM bsk.v_ledger l
  GROUP BY currency;

-- ACTIVITY-3 (D14): one row per live Oura workout — the newest version of each Oura id by recorded_at, unless voided — with Chris's newest note on it (null when there is none or it was cleared). Whether it counts as exercise is C#'s ExerciseRules.
CREATE VIEW bsk.v_workout AS
 WITH version AS (
         SELECT e.id AS event_id,
            e.payload -> 'oura_workout'::text AS w,
            e.recorded_at,
            row_number() OVER (PARTITION BY ((e.payload -> 'oura_workout'::text) ->> 'id'::text) ORDER BY e.recorded_at DESC, e.id DESC) AS rn
           FROM bsk.event e
          WHERE e.kind = 'activity'::text AND e.source_id = 'oura'::text AND jsonb_exists(e.payload, 'oura_workout'::text) AND jsonb_typeof(e.payload -> 'oura_workout'::text) = 'object'::text AND COALESCE((e.payload -> 'oura_workout'::text) ->> 'id'::text, ''::text) <> ''::text
        ), live AS (
         SELECT v.w ->> 'id'::text AS workout_id,
            bsk.try_to_date(v.w ->> 'day'::text) AS day,
            v.w,
            (v.w ->> 'start'::text)::timestamp with time zone AS started_at,
            (v.w ->> 'end'::text)::timestamp with time zone AS ended_at,
            v.recorded_at,
            v.event_id
           FROM version v
          WHERE v.rn = 1 AND COALESCE(v.w ->> 'voided'::text, 'false'::text) <> 'true'::text AND jsonb_typeof(v.w -> 'start'::text) = 'string'::text AND jsonb_typeof(v.w -> 'end'::text) = 'string'::text
        ), note AS (
         SELECT DISTINCT ON (((e.payload -> 'workout_note'::text) ->> 'workout'::text)) (e.payload -> 'workout_note'::text) ->> 'workout'::text AS workout_id,
            NULLIF(btrim((e.payload -> 'workout_note'::text) ->> 'text'::text), ''::text) AS text,
            e.recorded_at AS noted_at
           FROM bsk.event e
          WHERE e.kind = 'note'::text AND jsonb_exists(e.payload, 'workout_note'::text) AND jsonb_typeof(e.payload -> 'workout_note'::text) = 'object'::text
          ORDER BY ((e.payload -> 'workout_note'::text) ->> 'workout'::text), e.recorded_at DESC, e.id DESC
        )
 SELECT l.workout_id,
    l.day,
    COALESCE(l.w ->> 'activity'::text, 'unknown'::text) AS activity,
    l.w ->> 'label'::text AS label,
    l.started_at,
    l.ended_at,
    GREATEST(0::numeric, floor(EXTRACT(epoch FROM l.ended_at - l.started_at)))::integer AS seconds,
    bsk.json_int(l.w -> 'calories'::text) AS calories,
    bsk.json_int(l.w -> 'distance_m'::text) AS distance_m,
    l.w ->> 'intensity'::text AS intensity,
    COALESCE(l.w ->> 'source'::text, 'unknown'::text) AS source,
    n.text AS note,
        CASE
            WHEN n.text IS NOT NULL THEN n.noted_at
            ELSE NULL::timestamp with time zone
        END AS note_at,
    l.recorded_at AS synced_at,
    l.event_id
   FROM live l
     LEFT JOIN note n ON n.workout_id = l.workout_id
  WHERE l.day IS NOT NULL;

-- GEN-3 / REWARD-2: every habit's occurrences (archived included) with resolved state, when the deciding adherence was recorded, and the graduation date. Scheduled dates stop at graduation and skip the days the habit was Planned (0049). Today, graduation and a default start are dates in the home zone (bsk.home_today(), 0033).
CREATE VIEW bsk_derived.habit_occurrence_source AS
 WITH habits AS NOT MATERIALIZED (
         SELECT h.id,
            h.urn,
            h.attributes -> 'recurrence'::text AS recurrence,
            COALESCE(bsk.try_to_date(h.attributes ->> 'start'::text), (h.created_at AT TIME ZONE bsk.home_zone())::date) AS start_date,
            g.graduated_on,
            LEAST(bsk.home_today(), COALESCE(bsk.try_to_date(h.attributes ->> 'end'::text), bsk.home_today()), COALESCE(g.graduated_on - 1, bsk.home_today())) AS end_cap,
            bsk.is_archived(h.id) AS archived
           FROM bsk.subject h
             LEFT JOIN bsk_derived.subject_current_source c ON c.subject_id = h.id
             CROSS JOIN LATERAL ( SELECT
                        CASE
                            WHEN bsk.is_terminal_status(c.status) THEN (c.status_occurred_at AT TIME ZONE bsk.home_zone())::date
                            ELSE NULL::date
                        END AS graduated_on) g
          WHERE h.type = 'Habit'::text
        ), scheduled AS NOT MATERIALIZED (
         SELECT hb_1.id AS habit_id,
            occ.d AS occurrence_date
           FROM habits hb_1
             CROSS JOIN LATERAL bsk.recurrence_occurrences(hb_1.recurrence, hb_1.start_date, hb_1.start_date, hb_1.end_cap) occ(d)
          WHERE NOT (EXISTS ( SELECT 1
                   FROM bsk_derived.habit_planned_source p
                  WHERE p.habit_id = hb_1.id AND (p.planned_from IS NULL OR occ.d >= p.planned_from) AND (p.planned_until IS NULL OR occ.d < p.planned_until)))
        ), recorded AS NOT MATERIALIZED (
         SELECT DISTINCT hb_1.id AS habit_id,
            (e.payload ->> 'occurrence'::text)::date AS occurrence_date
           FROM habits hb_1
             JOIN bsk.event e ON e.kind = 'activity'::text AND (e.payload ->> 'subject_id'::text) = hb_1.id::text AND jsonb_exists(e.payload, 'occurrence'::text)
        ), dates AS NOT MATERIALIZED (
         SELECT scheduled.habit_id,
            scheduled.occurrence_date
           FROM scheduled
        UNION
         SELECT recorded.habit_id,
            recorded.occurrence_date
           FROM recorded
        )
 SELECT d.habit_id,
    hb.urn AS habit_urn,
    d.occurrence_date,
        CASE
            WHEN latest.result IS NOT NULL THEN latest.result
            WHEN d.occurrence_date < bsk.home_today() THEN 'missed'::text
            ELSE 'unrecorded'::text
        END AS state,
    latest.event_id AS adherence_event_id,
    latest.occurred_at AS adherence_at,
    hb.graduated_on,
    hb.archived
   FROM dates d
     JOIN habits hb ON hb.id = d.habit_id
     LEFT JOIN LATERAL ( SELECT e.payload ->> 'result'::text AS result,
            e.id AS event_id,
            e.occurred_at
           FROM bsk.event e
          WHERE e.kind = 'activity'::text AND (e.payload ->> 'subject_id'::text) = d.habit_id::text AND (e.payload ->> 'occurrence'::text) = to_char(d.occurrence_date::timestamp with time zone, 'YYYY-MM-DD'::text)
          ORDER BY e.occurred_at DESC, e.recorded_at DESC, e.id DESC
         LIMIT 1) latest ON true;

-- Each spell a habit was Planned (not tracked yet, 0049): planned_from (NULL when it was Planned since it was made: every day before) up to, not including, planned_until (NULL while still Planned), as dates in the home zone.
CREATE VIEW bsk_derived.habit_planned_source AS
 SELECT habit_id,
    planned_from,
    planned_until
   FROM ( SELECT h.id AS habit_id,
            lower(btrim(e.payload ->> 'status'::text)) AS status,
                CASE
                    WHEN e.occurred_at <> h.created_at THEN (e.occurred_at AT TIME ZONE bsk.home_zone())::date
                    ELSE NULL::date
                END AS planned_from,
            (lead(e.occurred_at) OVER (PARTITION BY h.id ORDER BY e.occurred_at, e.recorded_at, e.id) AT TIME ZONE bsk.home_zone())::date AS planned_until
           FROM bsk.subject h
             JOIN bsk.event e ON e.kind = 'state_change'::text AND (e.payload ->> 'subject_id'::text) = h.id::text
          WHERE h.type = 'Habit'::text) s
  WHERE status = 'planned'::text;

CREATE VIEW bsk_derived.subject_current_source AS
 SELECT s.id AS subject_id,
    s.urn,
    s.type,
    s.title,
    latest.status,
    latest.status_event_id,
    latest.status_occurred_at
   FROM bsk.subject s
     LEFT JOIN LATERAL ( SELECT e.payload ->> 'status'::text AS status,
            e.id AS status_event_id,
            e.occurred_at AS status_occurred_at
           FROM bsk.event e
          WHERE e.kind = 'state_change'::text AND (e.payload ->> 'subject_id'::text) = s.id::text
          ORDER BY e.occurred_at DESC, e.recorded_at DESC, e.id DESC
         LIMIT 1) latest ON true;

-- ---------------------------------------------------------------- functions

CREATE OR REPLACE FUNCTION bsk.artifact_bytes_in_one_place()
 RETURNS trigger
 LANGUAGE plpgsql
AS $function$
BEGIN
    IF (TG_TABLE_NAME = 'artifact_object'
            AND EXISTS (SELECT 1 FROM bsk.artifact_blob WHERE artifact_id = NEW.artifact_id))
        OR (TG_TABLE_NAME = 'artifact_blob'
            AND EXISTS (SELECT 1 FROM bsk.artifact_object WHERE artifact_id = NEW.artifact_id)) THEN
        RAISE EXCEPTION 'artifact % already has its bytes stored elsewhere', NEW.artifact_id
            USING ERRCODE = 'unique_violation';
    END IF;
    RETURN NEW;
END;
$function$;

CREATE OR REPLACE FUNCTION bsk.deny_mutation()
 RETURNS trigger
 LANGUAGE plpgsql
AS $function$
BEGIN
    RAISE EXCEPTION 'Table %.% is append-only; % is not permitted',
        TG_TABLE_SCHEMA, TG_TABLE_NAME, TG_OP
        USING ERRCODE = 'restrict_violation';
END;
$function$;

-- Today in the home zone (bsk.home_zone()). The habit views use it in place of current_date, which is the database's (UTC) date.
CREATE OR REPLACE FUNCTION bsk.home_today()
 RETURNS date
 LANGUAGE sql
 STABLE PARALLEL SAFE
AS $function$
    SELECT (now() AT TIME ZONE bsk.home_zone())::date;
$function$;

-- The home zone (an IANA name): the database setting bsk.home_zone, or America/Los_Angeles when unset. Must match the app's Rewards:HomeTimeZone.
CREATE OR REPLACE FUNCTION bsk.home_zone()
 RETURNS text
 LANGUAGE sql
 STABLE PARALLEL SAFE
AS $function$
    SELECT coalesce(nullif(btrim(current_setting('bsk.home_zone', true)), ''), 'America/Los_Angeles');
$function$;

-- True when a subject's newest archive_change event set archived=true (D9). No archive_change events means active. Reversible: a later restore appends archived=false.
CREATE OR REPLACE FUNCTION bsk.is_archived(subject uuid)
 RETURNS boolean
 LANGUAGE sql
 STABLE PARALLEL SAFE
AS $function$
    SELECT coalesce((
        SELECT (e.payload->>'archived')::boolean
        FROM bsk.event e
        WHERE e.kind = 'archive_change'
          AND e.payload->>'subject_id' = subject::text
        ORDER BY e.occurred_at DESC, e.recorded_at DESC, e.id DESC
        LIMIT 1
    ), false);
$function$;

-- D16: true for a repeating Task's series (a Task carrying a recurrence object and no series of its own). Series hold the pattern and price and stay out of the Task lists.
CREATE OR REPLACE FUNCTION bsk.is_task_series(type text, attributes jsonb)
 RETURNS boolean
 LANGUAGE sql
 IMMUTABLE PARALLEL SAFE
AS $function$
    -- coalesce: false, never null, for a Task with no pattern (NOT null would drop it).
    SELECT coalesce(
        type = 'Task'
        AND jsonb_typeof(attributes->'recurrence') = 'object'
        AND (attributes->>'series') IS NULL,
        false);
$function$;

-- True when a status string means the subject is finished/dead (the union of every type's terminal statuses — D7). NULL status is active, not terminal.
CREATE OR REPLACE FUNCTION bsk.is_terminal_status(status text)
 RETURNS boolean
 LANGUAGE sql
 IMMUTABLE PARALLEL SAFE
AS $function$
    SELECT lower(trim(coalesce(status, ''))) IN (
        -- Original set (0008).
        'done', 'completed', 'resolved', 'closed', 'cancelled',
        'abandoned', 'dropped', 'archived', 'superseded',
        -- Added for the per-type vocabularies (D7, 0011): Commitment Fulfilled/Missed,
        -- Appointment Missed, Idea Promoted/Rejected.
        'fulfilled', 'missed', 'promoted', 'rejected',
        -- Habit Established (0027). Habit Abandoned is already above.
        'established'
    );
$function$;

-- A jsonb number as an int (rounded); null for anything else.
CREATE OR REPLACE FUNCTION bsk.json_int(value jsonb)
 RETURNS integer
 LANGUAGE sql
 IMMUTABLE
AS $function$
    SELECT CASE WHEN jsonb_typeof(value) = 'number' THEN round((value #>> '{}')::numeric)::int END;
$function$;

-- A jsonb number as numeric; null for anything else.
CREATE OR REPLACE FUNCTION bsk.json_num(value jsonb)
 RETURNS numeric
 LANGUAGE sql
 IMMUTABLE
AS $function$
    SELECT CASE WHEN jsonb_typeof(value) = 'number' THEN (value #>> '{}')::numeric END;
$function$;

CREATE OR REPLACE FUNCTION bsk.ledger_opened_at()
 RETURNS timestamp with time zone
 LANGUAGE sql
 STABLE PARALLEL SAFE
AS $function$
    SELECT min(e.occurred_at)
    FROM bsk.event e
    WHERE e.kind = 'ledger'
      AND e.payload->>'entry' = 'open';
$function$;

-- D8: the recurrence object for a recurrence that reached attributes.recurrence as text (the kernel shape as text, the web's {kind} shape, or the mock's words), or NULL. Used once by migration 0041 to repair them.
CREATE OR REPLACE FUNCTION bsk.recurrence_from_text(t text)
 RETURNS jsonb
 LANGUAGE plpgsql
 IMMUTABLE PARALLEL SAFE
AS $function$
DECLARE
    j     jsonb;
    names text[] := ARRAY['sun', 'mon', 'tue', 'wed', 'thu', 'fri', 'sat'];
    days  jsonb;
    every int;
BEGIN
    IF t IS NULL THEN
        RETURN NULL;
    END IF;

    t := btrim(t);
    IF t = 'daily' THEN
        RETURN '{"freq":"daily"}'::jsonb;
    ELSIF t = 'weekdays' THEN
        RETURN '{"freq":"weekly","on":["mon","tue","wed","thu","fri"]}'::jsonb;
    ELSIF t = 'weekly' THEN
        RETURN '{"freq":"weekly","on":["sun"]}'::jsonb;
    END IF;

    BEGIN
        j := t::jsonb;
    EXCEPTION WHEN others THEN
        RETURN NULL;
    END;

    IF jsonb_typeof(j) IS DISTINCT FROM 'object' THEN
        RETURN NULL;
    END IF;

    IF j ? 'freq' THEN
        RETURN j;
    END IF;

    IF j->>'kind' = 'daily' THEN
        RETURN '{"freq":"daily"}'::jsonb;
    ELSIF j->>'kind' = 'weekly' THEN
        IF jsonb_typeof(j->'weekdays') = 'array' THEN
            SELECT jsonb_agg(names[d + 1] ORDER BY d)
            INTO days
            FROM (
                SELECT DISTINCT x::int AS d
                FROM jsonb_array_elements_text(j->'weekdays') x
                WHERE x ~ '^[0-6]$'
            ) w;
        END IF;
        -- The web read an empty or missing list as every day.
        IF days IS NULL THEN
            RETURN '{"freq":"daily"}'::jsonb;
        END IF;
        RETURN jsonb_build_object('freq', 'weekly', 'on', days);
    ELSIF j->>'kind' = 'interval' THEN
        IF (j->>'intervalDays') ~ '^[0-9]+$' THEN
            every := greatest(1, (j->>'intervalDays')::int);
        ELSE
            every := 1;
        END IF;
        RETURN jsonb_build_object('freq', 'interval', 'every', every, 'unit', 'days');
    END IF;

    RETURN NULL;
END;
$function$;

-- D8: expands a structured recurrence (attributes.recurrence) into occurrence dates within [win_start, win_end], anchored at the item start. Once each week (freq each, period week) gives each week's Monday for the weeks that overlap the window (D16). An every-N counted from the last completion, a trigger, and anything unknown yield nothing.
CREATE OR REPLACE FUNCTION bsk.recurrence_occurrences(recur jsonb, anchor date, win_start date, win_end date)
 RETURNS SETOF date
 LANGUAGE plpgsql
 STABLE PARALLEL SAFE
AS $function$
DECLARE
    freq  text := recur->>'freq';
    every int;
    step  interval;
    day_n int;
BEGIN
    IF recur IS NULL OR freq IS NULL OR win_start > win_end THEN
        RETURN;
    END IF;

    IF freq = 'daily' THEN
        RETURN QUERY
        SELECT d::date
        FROM generate_series(greatest(anchor, win_start), win_end, interval '1 day') d;

    ELSIF freq = 'weekly' THEN
        -- Match by day-of-week number (locale-independent); Postgres dow: 0=Sun..6=Sat.
        RETURN QUERY
        SELECT d::date
        FROM generate_series(greatest(anchor, win_start), win_end, interval '1 day') d
        WHERE extract(dow FROM d)::int = ANY (
            SELECT CASE lower(x)
                       WHEN 'sun' THEN 0 WHEN 'mon' THEN 1 WHEN 'tue' THEN 2
                       WHEN 'wed' THEN 3 WHEN 'thu' THEN 4 WHEN 'fri' THEN 5
                       WHEN 'sat' THEN 6 END
            FROM jsonb_array_elements_text(recur->'on') x
        );

    ELSIF freq = 'interval' THEN
        -- Counted from the last completion: the dates aren't known from the pattern.
        IF recur->>'from' = 'completion' THEN
            RETURN;
        END IF;
        every := (recur->>'every')::int;
        IF every IS NULL OR every < 1 THEN
            RETURN;
        END IF;
        step := CASE recur->>'unit'
                    WHEN 'days'   THEN make_interval(days   => every)
                    WHEN 'weeks'  THEN make_interval(weeks  => every)
                    WHEN 'months' THEN make_interval(months => every)
                    ELSE NULL
                END;
        IF step IS NULL THEN
            RETURN;
        END IF;
        -- Phased from the anchor; keep only those inside the window.
        RETURN QUERY
        SELECT d::date
        FROM generate_series(anchor::timestamp, win_end::timestamp, step) d
        WHERE d::date >= win_start;

    ELSIF freq = 'monthly' THEN
        IF recur->>'on' = 'last' THEN
            RETURN QUERY
            SELECT last_day::date
            FROM generate_series(
                     date_trunc('month', greatest(anchor, win_start))::timestamp,
                     win_end::timestamp, interval '1 month') m,
                 LATERAL (SELECT (date_trunc('month', m) + interval '1 month' - interval '1 day')::date) AS x(last_day)
            WHERE last_day BETWEEN win_start AND win_end;
        ELSE
            day_n := (recur->>'day')::int;
            IF day_n IS NULL OR day_n < 1 OR day_n > 31 THEN
                RETURN;
            END IF;
            RETURN QUERY
            SELECT occ::date
            FROM generate_series(
                     date_trunc('month', greatest(anchor, win_start))::timestamp,
                     win_end::timestamp, interval '1 month') m,
                 LATERAL (SELECT (date_trunc('month', m) + make_interval(days => day_n - 1))::date) AS x(occ)
            WHERE occ BETWEEN win_start AND win_end
              -- Skip months that have no such day (e.g. day 31 in February).
              AND extract(month FROM occ) = extract(month FROM m);
        END IF;

    ELSIF freq = 'each' THEN
        -- Once each week, Monday to Sunday (date_trunc('week') is the Monday): each week
        -- from the anchor's, named by its Monday, for the weeks that overlap the window.
        IF recur->>'period' IS DISTINCT FROM 'week' THEN
            RETURN;
        END IF;
        RETURN QUERY
        SELECT w::date
        FROM generate_series(
                 date_trunc('week', greatest(anchor, win_start))::timestamp,
                 win_end::timestamp, interval '1 week') w;

    ELSE
        -- 'trigger' and anything unknown: no scheduled dates.
        RETURN;
    END IF;
END;
$function$;

-- Parses text to a date, returning NULL instead of raising when the value is malformed or out of range (e.g. 2026-02-30). NULL input returns NULL.
CREATE OR REPLACE FUNCTION bsk.try_to_date(value text)
 RETURNS date
 LANGUAGE plpgsql
 STABLE PARALLEL SAFE STRICT
AS $function$
BEGIN
    RETURN value::date;
EXCEPTION
    WHEN invalid_datetime_format OR datetime_field_overflow THEN
        RETURN NULL;
END;
$function$;

-- Parses text to a timestamptz, returning NULL instead of raising when the value is malformed or out of range. NULL input returns NULL.
CREATE OR REPLACE FUNCTION bsk.try_to_timestamptz(value text)
 RETURNS timestamp with time zone
 LANGUAGE plpgsql
 STABLE PARALLEL SAFE STRICT
AS $function$
BEGIN
    RETURN value::timestamptz;
EXCEPTION
    WHEN invalid_datetime_format OR datetime_field_overflow THEN
        RETURN NULL;
END;
$function$;

-- ---------------------------------------------------------------- triggers

CREATE TRIGGER artifact_deny_delete BEFORE DELETE ON bsk.artifact FOR EACH ROW EXECUTE FUNCTION bsk.deny_mutation();
CREATE TRIGGER artifact_deny_truncate BEFORE TRUNCATE ON bsk.artifact FOR EACH STATEMENT EXECUTE FUNCTION bsk.deny_mutation();
CREATE TRIGGER artifact_deny_update BEFORE UPDATE ON bsk.artifact FOR EACH ROW EXECUTE FUNCTION bsk.deny_mutation();
CREATE TRIGGER artifact_blob_deny_delete BEFORE DELETE ON bsk.artifact_blob FOR EACH ROW EXECUTE FUNCTION bsk.deny_mutation();
CREATE TRIGGER artifact_blob_deny_truncate BEFORE TRUNCATE ON bsk.artifact_blob FOR EACH STATEMENT EXECUTE FUNCTION bsk.deny_mutation();
CREATE TRIGGER artifact_blob_deny_update BEFORE UPDATE ON bsk.artifact_blob FOR EACH ROW EXECUTE FUNCTION bsk.deny_mutation();
CREATE TRIGGER artifact_blob_one_place BEFORE INSERT ON bsk.artifact_blob FOR EACH ROW EXECUTE FUNCTION bsk.artifact_bytes_in_one_place();
CREATE TRIGGER artifact_object_deny_delete BEFORE DELETE ON bsk.artifact_object FOR EACH ROW EXECUTE FUNCTION bsk.deny_mutation();
CREATE TRIGGER artifact_object_deny_truncate BEFORE TRUNCATE ON bsk.artifact_object FOR EACH STATEMENT EXECUTE FUNCTION bsk.deny_mutation();
CREATE TRIGGER artifact_object_deny_update BEFORE UPDATE ON bsk.artifact_object FOR EACH ROW EXECUTE FUNCTION bsk.deny_mutation();
CREATE TRIGGER artifact_object_one_place BEFORE INSERT ON bsk.artifact_object FOR EACH ROW EXECUTE FUNCTION bsk.artifact_bytes_in_one_place();
CREATE TRIGGER event_deny_delete BEFORE DELETE ON bsk.event FOR EACH ROW EXECUTE FUNCTION bsk.deny_mutation();
CREATE TRIGGER event_deny_truncate BEFORE TRUNCATE ON bsk.event FOR EACH STATEMENT EXECUTE FUNCTION bsk.deny_mutation();
CREATE TRIGGER event_deny_update BEFORE UPDATE ON bsk.event FOR EACH ROW EXECUTE FUNCTION bsk.deny_mutation();
