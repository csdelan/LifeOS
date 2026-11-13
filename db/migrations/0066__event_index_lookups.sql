-- Code review CR77: a lookup of one entry by its id reads that entry's events through its index,
-- not every version of every entry.
--
-- The six newest-version views (meals, fasts, lab visits, metric readings, space photos and
-- ratings) rank each entry's versions in a window partitioned by the id as text, but expose the id
-- cast to uuid. Postgres pushes a filter below a window only when it is on the partition key
-- itself, so `WHERE meal_id = @id` ranked every version of every meal before keeping one, and
-- scanned the per-id index whole. Each view now ends with its key as text (meal_key, fast_key,
-- visit_key, reading_key, photo_key, rating_key), and the repositories look up by it: the filter
-- goes below the window and becomes the index's condition. A column added at the end changes no
-- view built on these.
--
-- v_task_timer used each of its steps more than once, so Postgres worked each out in full, every
-- timer event of every task sorted, before the read for one task filtered it; and it keyed them by
-- the subject id cast to uuid, which event_timer_subject_idx (on the text) can't look up. Its steps
-- are now NOT MATERIALIZED and keyed by the text, with subject_key at the end, so a read for one
-- task by subject_key reads that task's timer events by the index, under the timer lock too.
--
-- EventIndexTests now needs a read for every index on bsk.event and checks a lookup's plan uses
-- its index's condition, which a full scan of the index (what these views did) doesn't.

CREATE OR REPLACE VIEW bsk.v_meal AS
 WITH version AS (
         SELECT e.id AS event_id,
            ((e.payload -> 'meal'::text) ->> 'id'::text) AS meal_key,
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
    COALESCE((m -> 'alcoholic'::text) = 'true'::jsonb, false) AS alcoholic,
    meal_key
   FROM version v
  WHERE rn = 1 AND COALESCE(m ->> 'voided'::text, 'false'::text) <> 'true'::text;

CREATE OR REPLACE VIEW bsk.v_fast AS
 WITH version AS (
         SELECT e.id AS event_id,
            ((e.payload -> 'fast'::text) ->> 'id'::text) AS fast_key,
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
            v.event_id,
            v.fast_key
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
    event_id,
    fast_key
   FROM live l;

CREATE OR REPLACE VIEW bsk.v_lab_visit AS
 WITH version AS (
         SELECT e.id AS event_id,
            ((e.payload -> 'lab_visit'::text) ->> 'id'::text) AS visit_key,
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
                END AS report,
            v.visit_key
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
    l.event_id,
    l.visit_key
   FROM live l
     LEFT JOIN bsk.v_artifact a ON a.id = l.report AND a.has_bytes;

CREATE OR REPLACE VIEW bsk.v_metric_reading AS
 WITH version AS (
         SELECT e.id AS event_id,
            ((e.payload -> 'metric_reading'::text) ->> 'id'::text) AS reading_key,
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
            v.first_logged_at,
            v.reading_key
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
    bsk.json_num(l.r -> 'high'::text) AS high,
    l.reading_key
   FROM live l
     JOIN bsk.subject m ON m.id = ((l.r ->> 'metric'::text)::uuid) AND m.type = 'Metric'::text;

CREATE OR REPLACE VIEW bsk.v_space_photo AS
 WITH version AS (
         SELECT e.id AS event_id,
            ((e.payload -> 'space_photo'::text) ->> 'id'::text) AS photo_key,
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
                END AS photo,
            v.photo_key
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
    l.event_id,
    l.photo_key
   FROM live l
     JOIN bsk.subject s ON s.id = ((l.p ->> 'space'::text)::uuid) AND s.type = 'Space'::text
     JOIN bsk.artifact a ON a.id = l.photo;

CREATE OR REPLACE VIEW bsk.v_space_rating AS
 WITH version AS (
         SELECT e.id AS event_id,
            ((e.payload -> 'space_rating'::text) ->> 'id'::text) AS rating_key,
            e.payload -> 'space_rating'::text AS r,
            e.recorded_at,
            row_number() OVER (PARTITION BY ((e.payload -> 'space_rating'::text) ->> 'id'::text) ORDER BY e.recorded_at DESC, e.id DESC) AS rn
           FROM bsk.event e
          WHERE e.kind = 'measurement'::text AND jsonb_exists(e.payload, 'space_rating'::text) AND jsonb_typeof(e.payload -> 'space_rating'::text) = 'object'::text AND ((e.payload -> 'space_rating'::text) ->> 'id'::text) ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'::text
        ), live AS (
         SELECT v.event_id,
            v.r,
            v.recorded_at,
            v.rn,
            v.rating_key
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
    l.event_id,
    l.rating_key
   FROM live l
     JOIN bsk.subject s ON s.id = ((l.r ->> 'space'::text)::uuid) AND s.type = 'Space'::text;

CREATE OR REPLACE VIEW bsk.v_task_timer AS
 WITH timer_event AS NOT MATERIALIZED (
         SELECT e.payload ->> 'subject_id'::text AS subject_key,
            e.payload ->> 'timer'::text AS action,
            e.occurred_at,
            e.recorded_at,
            e.id
           FROM bsk.event e
          WHERE e.kind = 'activity'::text AND jsonb_exists(e.payload, 'timer'::text)
        ), sequenced AS NOT MATERIALIZED (
         SELECT t.subject_key,
            t.action,
            t.occurred_at,
            t.recorded_at,
            t.id,
            lead(t.occurred_at) OVER w AS next_at,
            count(*) FILTER (WHERE t.action = 'start'::text) OVER w AS session_no,
            row_number() OVER (PARTITION BY t.subject_key ORDER BY t.occurred_at DESC, t.recorded_at DESC, t.id DESC) AS rn_desc
           FROM timer_event t
          WINDOW w AS (PARTITION BY t.subject_key ORDER BY t.occurred_at, t.recorded_at, t.id)
        ), segment AS NOT MATERIALIZED (
         SELECT sequenced.subject_key,
            sequenced.session_no,
            EXTRACT(epoch FROM sequenced.next_at - sequenced.occurred_at) AS seconds
           FROM sequenced
          WHERE (sequenced.action = ANY (ARRAY['start'::text, 'resume'::text])) AND sequenced.next_at IS NOT NULL
        ), totals AS NOT MATERIALIZED (
         SELECT segment.subject_key,
            sum(segment.seconds) AS seconds
           FROM segment
          GROUP BY segment.subject_key
        ), latest AS NOT MATERIALIZED (
         SELECT sequenced.subject_key,
            sequenced.action,
            sequenced.occurred_at,
            sequenced.session_no
           FROM sequenced
          WHERE sequenced.rn_desc = 1
        ), session_start AS NOT MATERIALIZED (
         SELECT DISTINCT ON (sequenced.subject_key) sequenced.subject_key,
            sequenced.occurred_at
           FROM sequenced
          WHERE sequenced.action = 'start'::text
          ORDER BY sequenced.subject_key, sequenced.occurred_at DESC, sequenced.recorded_at DESC, sequenced.id DESC
        )
 SELECT l.subject_key::uuid AS subject_id,
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
          WHERE g.subject_key = l.subject_key AND g.session_no = l.session_no), 0::numeric))::bigint AS session_seconds,
    ss.occurred_at AS session_started_at,
    l.session_no AS sessions,
    l.occurred_at AS last_changed_at,
    l.subject_key
   FROM latest l
     JOIN bsk.subject s ON s.id = l.subject_key::uuid
     LEFT JOIN totals tot ON tot.subject_key = l.subject_key
     LEFT JOIN session_start ss ON ss.subject_key = l.subject_key;
