-- Three years of made-up use, shaped like Chris's but none of it his (code review R13). It runs
-- once, on a copy of the migrated template, and that copy becomes the seeded template the scale
-- tests copy (SeededTemplate). Every row is the shape the kernel writes, copied from what its
-- services write for one of each; ids and choices come from hashes of a name, so two runs make
-- the same data on the same day, whatever order Postgres evaluates rows in.
--
-- Days run back from yesterday, 1,095 of them, in UTC, the seeded template's home zone.
-- Rows are staged in temp tables, then written to the log in recorded_at order, as the kernel
-- appends them. What the items earned (the ledger) is worked out from the views afterwards, so
-- what the ledger pays is what the views say is due, and a reconcile finds nothing to change.
-- The food and activity days, whose prices are the kernel's rules, are paid by the kernel's own
-- settle once this has run (SeededTemplate), and the log is clustered after that.

SET TIME ZONE 'UTC';

-- A number in [0, 1) from a name, a uuid from a name, and short hex from a name.
CREATE FUNCTION pg_temp.r(k text) RETURNS float8 LANGUAGE sql IMMUTABLE
AS $$ SELECT ('x' || substr(md5(k), 1, 8))::bit(32)::bigint / 4294967296.0 $$;
CREATE FUNCTION pg_temp.u(k text) RETURNS uuid LANGUAGE sql IMMUTABLE
AS $$ SELECT md5(k)::uuid $$;
CREATE FUNCTION pg_temp.hex(k text, n int) RETURNS text LANGUAGE sql IMMUTABLE
AS $$ SELECT substr(md5('hex:' || k), 1, n) $$;
-- An instant as the kernel writes one inside a payload.
CREATE FUNCTION pg_temp.iso(t timestamptz) RETURNS text LANGUAGE sql IMMUTABLE
AS $$ SELECT to_char(t AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"+00:00"') $$;
-- A subject's urn: its type, its title as a slug, and six hex digits.
CREATE FUNCTION pg_temp.urn(type text, title text, k text) RETURNS text LANGUAGE sql IMMUTABLE
AS $$ SELECT 'urn:bsk:' || lower(type) || ':' || trim(BOTH '-' FROM lower(regexp_replace(title, '[^a-zA-Z0-9]+', '-', 'g'))) || '-' || pg_temp.hex(k, 6) $$;
-- A score from 1 to 5 in halves.
CREATE FUNCTION pg_temp.score(x float8, low float8, high float8) RETURNS numeric LANGUAGE sql IMMUTABLE
AS $$ SELECT round((low + x * (high - low)) * 2)::numeric / 2 $$;

CREATE TEMP TABLE days AS
SELECT ago, current_date - ago AS day, (current_date - ago)::timestamptz AS t
FROM generate_series(1, 1095) ago;

CREATE TEMP TABLE ev (
    id uuid PRIMARY KEY,
    kind text NOT NULL,
    provenance text NOT NULL DEFAULT 'declared',
    occurred_at timestamptz NOT NULL,
    recorded_at timestamptz NOT NULL,
    source_id text NOT NULL DEFAULT 'api',
    external_id text,
    payload jsonb NOT NULL,
    derived_from uuid[] NOT NULL DEFAULT '{}',
    artifact_id uuid);

CREATE TEMP TABLE edge (event_id uuid NOT NULL, subject_id uuid NOT NULL, relation text NOT NULL, provenance text NOT NULL DEFAULT 'declared');

CREATE TEMP TABLE art (id uuid PRIMARY KEY, content text NOT NULL, recorded_at timestamptz NOT NULL);

-- ---------------------------------------------------------------- the wallet

INSERT INTO ev (id, kind, occurred_at, recorded_at, source_id, payload)
VALUES (pg_temp.u('wallet'), 'ledger', (current_date - 1096)::timestamptz, (current_date - 1096)::timestamptz, 'cli', '{"entry": "open"}');

-- ---------------------------------------------------------------- areas, goals, projects

INSERT INTO bsk.subject (id, urn, type, title, attributes, created_at)
SELECT pg_temp.u('area:' || n), pg_temp.urn('Area', title, 'area:' || n), 'Area', title, '{}', (current_date - 1100)::timestamptz
FROM (VALUES (1, 'Health'), (2, 'Home'), (3, 'Work'), (4, 'Money'), (5, 'People'), (6, 'Learning')) a(n, title);

INSERT INTO bsk.subject (id, urn, type, title, attributes, created_at)
SELECT pg_temp.u('goal:' || n), pg_temp.urn('Goal', 'Goal ' || n, 'goal:' || n), 'Goal', 'Goal ' || n,
       jsonb_build_object('target_date', to_char(current_date - 1095 + n * 70 + 120, 'YYYY-MM-DD')),
       (current_date - 1095 + n * 70)::timestamptz + interval '10 hours'
FROM generate_series(1, 15) n;

INSERT INTO bsk.subject (id, urn, type, title, attributes, created_at)
SELECT pg_temp.u('project:' || n), pg_temp.urn('Project', 'Project ' || n, 'project:' || n), 'Project', 'Project ' || n, '{}',
       (current_date - 1095 + n * 26)::timestamptz + interval '11 hours'
FROM generate_series(1, 40) n;

INSERT INTO bsk.subject_relation (from_subject, relation, to_subject, provenance, created_at)
SELECT pg_temp.u('project:' || n), 'serves', pg_temp.u('goal:' || (1 + (n - 1) * 15 / 40)), 'declared',
       (current_date - 1095 + n * 26)::timestamptz + interval '11 hours'
FROM generate_series(1, 40) n;

-- A Goal and a Project are Active from when they're made, and most finish.
INSERT INTO ev (id, kind, occurred_at, recorded_at, payload)
SELECT pg_temp.u('status:active:' || s.id), 'state_change', s.created_at, s.created_at,
       jsonb_build_object('status', 'Active', 'subject_id', s.id::text)
FROM bsk.subject s WHERE s.type IN ('Goal', 'Project');

INSERT INTO ev (id, kind, occurred_at, recorded_at, payload)
SELECT pg_temp.u('status:done:' || s.id), 'state_change', done, done, jsonb_build_object('status', 'Completed', 'subject_id', s.id::text)
FROM bsk.subject s
CROSS JOIN LATERAL (SELECT s.created_at + (60 + pg_temp.r('done:' || s.id) * 120) * interval '1 day' AS done) d
WHERE s.type IN ('Goal', 'Project') AND pg_temp.r('finish:' || s.id) < 0.6 AND done < now() - interval '2 days';

-- ---------------------------------------------------------------- habits and their check-offs

CREATE TEMP TABLE habit AS
SELECT * FROM (VALUES
    (1, 'Morning walk', 1095, '{"freq": "daily"}'::jsonb, 0.80, 0.05, '{"followed": {"xp": 5, "freedom": 5, "health_xp": 20}}'::jsonb),
    (2, 'Meditate', 1095, '{"freq": "daily"}', 0.70, 0.10, '{"followed": {"xp": 5, "freedom": 5, "health_xp": 10}}'),
    (3, 'Read 20 pages', 900, '{"freq": "daily"}', 0.65, 0.10, NULL),
    (4, 'Stretch', 1095, '{"on": ["mon", "wed", "fri"], "freq": "weekly"}', 0.75, 0.05, '{"followed": {"xp": 5, "freedom": 5, "health_xp": 15}}'),
    (5, 'No phone after 10pm', 600, '{"freq": "daily"}', 0.60, 0.0, NULL),
    (6, 'Floss', 300, '{"freq": "daily"}', 0.85, 0.0, '{"followed": {"xp": 5, "freedom": 5, "health_xp": 5}}')
) v(n, title, start_ago, recurrence, p_followed, p_partial, bounty);

INSERT INTO bsk.subject (id, urn, type, title, attributes, created_at)
SELECT pg_temp.u('habit:' || n), pg_temp.urn('Habit', title, 'habit:' || n), 'Habit', title,
       jsonb_build_object('start', to_char(current_date - start_ago, 'YYYY-MM-DD'), 'recurrence', recurrence)
           || CASE WHEN bounty IS NULL THEN '{}' ELSE jsonb_build_object('bounty', bounty) END
           || CASE WHEN p_partial > 0 THEN '{"allows_partial": true}'::jsonb ELSE '{}' END,
       (current_date - start_ago)::timestamptz + interval '9 hours'
FROM habit;

INSERT INTO ev (id, kind, occurred_at, recorded_at, payload)
SELECT pg_temp.u('status:building:' || n), 'state_change', created, created,
       jsonb_build_object('status', 'Building', 'subject_id', pg_temp.u('habit:' || n)::text)
FROM habit CROSS JOIN LATERAL (SELECT (current_date - start_ago)::timestamptz + interval '9 hours' AS created) c;

-- Most scheduled days are checked off that evening, some the next day, and a few are marked missed.
INSERT INTO ev (id, kind, occurred_at, recorded_at, payload)
SELECT pg_temp.u('adh:' || h.n || ':' || d.day), 'activity', at, at,
       jsonb_build_object('note', NULL, 'result', result, 'occurrence', to_char(d.day, 'YYYY-MM-DD'),
                          'subject_id', pg_temp.u('habit:' || h.n)::text)
FROM habit h
JOIN days d ON d.ago <= h.start_ago AND (h.n <> 4 OR extract(isodow FROM d.day) IN (1, 3, 5))
CROSS JOIN LATERAL (SELECT pg_temp.r('adh:' || h.n || ':' || d.day) AS x) rr
CROSS JOIN LATERAL (
    SELECT CASE WHEN x < h.p_followed THEN 'followed'
                WHEN x < h.p_followed + h.p_partial THEN 'partial'
                WHEN x < h.p_followed + h.p_partial + 0.04 THEN 'missed' END AS result,
           d.t + interval '19 hours' + pg_temp.r('adh-at:' || h.n || ':' || d.day) * interval '4 hours'
               + CASE WHEN pg_temp.r('late:' || h.n || ':' || d.day) < 0.1 THEN interval '13 hours' ELSE interval '0' END AS at) z
WHERE result IS NOT NULL;

-- A few days are recorded again, later, as followed.
INSERT INTO ev (id, kind, occurred_at, recorded_at, payload)
SELECT pg_temp.u('adh-again:' || e.id), 'activity', e.occurred_at + interval '2 hours', e.recorded_at + interval '2 hours',
       e.payload || '{"result": "followed"}'
FROM ev e
WHERE e.kind = 'activity' AND e.payload ? 'result' AND e.payload->>'result' <> 'followed' AND pg_temp.r('again:' || e.id) < 0.15;

-- ---------------------------------------------------------------- repeating Tasks

CREATE TEMP TABLE series AS
SELECT * FROM (VALUES
    (1, 'Make the bed', '{"freq": "daily"}'::jsonb, '{"completed": {"xp": 2, "coins": 1, "freedom": 2}}'::jsonb),
    (2, 'Water the plants', '{"on": ["mon"], "freq": "weekly"}', '{"completed": {"xp": 10, "coins": 5, "freedom": 10, "environment_xp": 10}}'),
    (3, 'Take out the trash', '{"on": ["thu"], "freq": "weekly"}', '{"completed": {"xp": 5, "coins": 2, "freedom": 5, "environment_xp": 5}}'),
    (4, 'Pay the bills', '{"day": 1, "freq": "monthly"}', '{"completed": {"xp": 20, "coins": 10, "freedom": 20, "finance_xp": 15}}')
) v(n, title, recurrence, bounty);

INSERT INTO bsk.subject (id, urn, type, title, attributes, created_at)
SELECT pg_temp.u('series:' || n), pg_temp.urn('Task', title, 'series:' || n), 'Task', title,
       jsonb_build_object('start', to_char(current_date - 1095, 'YYYY-MM-DD'), 'bounty', bounty, 'recurrence', recurrence),
       (current_date - 1095)::timestamptz + interval '8 hours'
FROM series;

CREATE TEMP TABLE rep AS
SELECT s.n, s.title, o.d AS occurrence, pg_temp.u('rep:' || s.n || ':' || o.d) AS id
FROM series s
CROSS JOIN LATERAL bsk.recurrence_occurrences(s.recurrence, current_date - 1095, current_date - 1095, current_date + 1) o(d);

INSERT INTO bsk.subject (id, urn, type, title, attributes, created_at)
SELECT r.id, pg_temp.urn('Task', r.title, 'rep:' || r.n || ':' || r.occurrence), 'Task', r.title,
       jsonb_build_object('due', to_char(r.occurrence, 'YYYY-MM-DD'), 'series', s.urn, 'occurrence', to_char(r.occurrence, 'YYYY-MM-DD')),
       greatest(r.occurrence::timestamptz - interval '2 days', (current_date - 1095)::timestamptz + interval '8 hours')
FROM rep r JOIN bsk.subject s ON s.id = pg_temp.u('series:' || r.n);

-- Most are done on the day; some are missed (closed by the settle the next morning) or cancelled.
INSERT INTO ev (id, kind, occurred_at, recorded_at, source_id, payload)
SELECT pg_temp.u('status:rep:' || r.id), 'state_change', at, at, 'api',
       jsonb_build_object('status', status, 'subject_id', r.id::text)
FROM rep r
CROSS JOIN LATERAL (SELECT pg_temp.r('rep-close:' || r.id) AS x) rr
CROSS JOIN LATERAL (
    SELECT CASE WHEN x < 0.85 THEN 'Completed' WHEN x < 0.95 THEN 'Missed' ELSE 'Cancelled' END AS status,
           CASE WHEN x >= 0.85 AND x < 0.95 THEN r.occurrence::timestamptz + interval '1 day 4 hours'
                ELSE r.occurrence::timestamptz + interval '7 hours' + pg_temp.r('rep-at:' || r.id) * interval '14 hours' END AS at) z
WHERE r.occurrence < current_date - 1;

-- ---------------------------------------------------------------- one-off Tasks, their timers and Inbox flags

CREATE TEMP TABLE task1 AS
SELECT i, pg_temp.u('task:' || i) AS id, 'Task ' || i AS title,
       (current_date - 1095 + (i * 1094 / 1000))::timestamptz + interval '9 hours' + pg_temp.r('task-at:' || i) * interval '10 hours' AS created_at,
       pg_temp.r('task-end:' || i) AS fate,
       floor(pg_temp.r('task-days:' || i) * 12)::int AS took
FROM generate_series(1, 1000) i;

INSERT INTO bsk.subject (id, urn, type, title, attributes, created_at)
SELECT id, pg_temp.urn('Task', title, 'task:' || i), 'Task', title,
       CASE WHEN pg_temp.r('task-due:' || i) < 0.4 THEN jsonb_build_object('due', to_char((created_at + took * interval '1 day')::date, 'YYYY-MM-DD')) ELSE '{}' END
           || jsonb_build_object('area', pg_temp.urn('Area', (ARRAY['Health', 'Home', 'Work', 'Money', 'People', 'Learning'])[1 + i % 6], 'area:' || (1 + i % 6))),
       created_at
FROM task1;

INSERT INTO bsk.subject_relation (from_subject, relation, to_subject, provenance, created_at)
SELECT id, 'serves', pg_temp.u('project:' || (1 + i % 40)), 'declared', created_at
FROM task1 WHERE pg_temp.r('task-serves:' || i) < 0.5;

INSERT INTO ev (id, kind, occurred_at, recorded_at, payload)
SELECT pg_temp.u('status:started:' || id), 'state_change', at, at, jsonb_build_object('status', 'In progress', 'subject_id', id::text)
FROM task1 CROSS JOIN LATERAL (SELECT created_at + took * interval '12 hours' AS at) z
WHERE pg_temp.r('task-started:' || i) < 0.5 AND at < now() - interval '1 day';

INSERT INTO ev (id, kind, occurred_at, recorded_at, payload)
SELECT pg_temp.u('status:closed:' || id), 'state_change', at, at,
       jsonb_build_object('status', CASE WHEN fate < 0.8 THEN 'Completed' ELSE 'Cancelled' END, 'subject_id', id::text)
FROM task1 CROSS JOIN LATERAL (SELECT created_at + took * interval '1 day' + interval '3 hours' AS at) z
WHERE fate < 0.9 AND at < now() - interval '1 day';

-- A timed session on some of the completed ones, stopped as each was completed.
INSERT INTO ev (id, kind, occurred_at, recorded_at, payload)
SELECT pg_temp.u('timer:' || action || ':' || t.id), 'activity', done - back, done - back,
       jsonb_build_object('timer', action, 'subject_id', t.id::text)
FROM task1 t
CROSS JOIN LATERAL (SELECT t.created_at + t.took * interval '1 day' + interval '3 hours' AS done) z
CROSS JOIN (VALUES ('start', interval '2 hours'), ('pause', interval '90 minutes'), ('resume', interval '1 hour'), ('stop', interval '0')) a(action, back)
WHERE t.fate < 0.8 AND done < now() - interval '1 day' AND pg_temp.r('timed:' || t.i) < 0.3;

INSERT INTO edge (event_id, subject_id, relation)
SELECT id, (payload->>'subject_id')::uuid, 'concerns' FROM ev WHERE kind = 'activity' AND payload ? 'timer';

-- Some Tasks are flagged to the Inbox, and leave it when they close.
INSERT INTO ev (id, kind, occurred_at, recorded_at, payload)
SELECT pg_temp.u('flag:' || id), 'triage', created_at + interval '1 minute', created_at + interval '1 minute',
       jsonb_build_object('state', 'flagged', 'item_id', id::text, 'item_kind', 'subject')
FROM task1 WHERE pg_temp.r('task-flag:' || i) < 0.12;

INSERT INTO ev (id, kind, occurred_at, recorded_at, payload)
SELECT pg_temp.u('archive:' || id), 'archive_change', at, at, jsonb_build_object('archived', true, 'subject_id', id::text)
FROM task1 CROSS JOIN LATERAL (SELECT created_at + interval '200 days' AS at) z
WHERE fate >= 0.9 AND pg_temp.r('task-archive:' || i) < 0.4 AND at < now();

-- ---------------------------------------------------------------- the Food Log

CREATE TEMP TABLE slot AS
SELECT * FROM (VALUES
    (1, 'breakfast', 0.92, ARRAY['Oatmeal', 'Eggs and toast', 'Yogurt and berries', 'Smoothie'], 300, 600, 3.0, 5.0, false),
    (2, 'lunch', 0.85, ARRAY['Chicken salad', 'Turkey sandwich', 'Burrito bowl', 'Leftovers', 'Soup'], 450, 900, 2.5, 4.5, false),
    (3, 'dinner', 0.95, ARRAY['Salmon and rice', 'Pasta', 'Stir fry', 'Pizza', 'Tacos', 'Steak and potatoes'], 600, 1200, 2.0, 5.0, false),
    (4, 'snack', 0.50, ARRAY['Apple', 'Almonds', 'Protein bar', 'Chips', 'Cookie'], 100, 350, 1.5, 4.0, false),
    (5, 'drink', 0.25, ARRAY['Beer', 'Glass of wine'], 120, 200, 1.0, 1.0, true),
    (6, 'drink', 0.08, ARRAY['Beer', 'Glass of wine'], 120, 200, 1.0, 1.0, true)
) v(n, meal_type, p, names, cal_low, cal_high, score_low, score_high, alcoholic);

-- When each day's overnight fast ended, and so when breakfast was.
CREATE TEMP TABLE morning AS
SELECT d.ago, d.day, d.t, d.t + interval '7 hours' + pg_temp.r('morning:' || d.day) * interval '3 hours' AS fast_end
FROM days d;

CREATE TEMP TABLE meal AS
SELECT pg_temp.u('meal:' || m.day || ':' || s.n) AS id, m.day, s.n, s.meal_type, s.alcoholic,
       s.names[1 + floor(pg_temp.r('meal-name:' || m.day || ':' || s.n) * array_length(s.names, 1))::int] AS name,
       pg_temp.score(pg_temp.r('meal-score:' || m.day || ':' || s.n), s.score_low, s.score_high) AS score,
       (s.cal_low + pg_temp.r('meal-cal:' || m.day || ':' || s.n) * (s.cal_high - s.cal_low))::int AS calories,
       CASE s.n WHEN 1 THEN m.fast_end + interval '10 minutes'
                WHEN 2 THEN m.t + interval '12 hours' + pg_temp.r('lunch:' || m.day) * interval '90 minutes'
                WHEN 3 THEN m.t + interval '18 hours 30 minutes' + pg_temp.r('dinner:' || m.day) * interval '90 minutes'
                WHEN 4 THEN m.t + interval '15 hours' + pg_temp.r('snack:' || m.day) * interval '2 hours'
                WHEN 5 THEN m.t + interval '20 hours 30 minutes'
                ELSE m.t + interval '21 hours 30 minutes' END AS eaten_at
FROM morning m CROSS JOIN slot s
WHERE pg_temp.r('meal:' || m.day || ':' || s.n) < s.p;

INSERT INTO ev (id, kind, occurred_at, recorded_at, payload)
SELECT pg_temp.u('meal-v1:' || id), 'measurement', eaten_at, eaten_at + interval '5 minutes' + pg_temp.r('meal-logged:' || id) * interval '2 hours',
       jsonb_build_object('meal', jsonb_build_object('id', id::text, 'name', name, 'score', score, 'calories', calories, 'meal_type', meal_type)
           || CASE WHEN alcoholic THEN '{"alcoholic": true}'::jsonb ELSE '{}' END)
FROM meal;

-- Some are edited later (a new version), and a few deleted (a voided one).
INSERT INTO ev (id, kind, occurred_at, recorded_at, payload)
SELECT pg_temp.u('meal-v2:' || id), 'measurement', eaten_at, eaten_at + interval '3 hours' + pg_temp.r('meal-edit:' || id) * interval '30 hours',
       jsonb_build_object('meal', jsonb_build_object('id', id::text, 'name', name, 'score', score, 'calories', calories + 50, 'meal_type', meal_type)
           || CASE WHEN alcoholic THEN '{"alcoholic": true}'::jsonb ELSE '{}' END)
FROM meal WHERE pg_temp.r('meal-edited:' || id) < 0.12;

INSERT INTO ev (id, kind, occurred_at, recorded_at, payload)
SELECT pg_temp.u('meal-void:' || id), 'measurement', eaten_at, eaten_at + interval '2 days',
       jsonb_build_object('meal', jsonb_build_object('id', id::text, 'voided', true))
FROM meal WHERE pg_temp.r('meal-voided:' || id) < 0.02;

-- The overnight fast, from the evening before to the morning: half recorded afterwards in one
-- go, half started at night and ended in the morning (two versions).
CREATE TEMP TABLE fast AS
SELECT pg_temp.u('fast:' || m.day) AS id, m.day, m.fast_end AS ended_at,
       m.t - interval '3 hours 30 minutes' + pg_temp.r('fast-start:' || m.day) * interval '90 minutes' AS started_at,
       pg_temp.r('fast-mode:' || m.day) < 0.5 AS recorded
FROM morning m WHERE pg_temp.r('fast:' || m.day) < 0.8;

INSERT INTO ev (id, kind, occurred_at, recorded_at, payload)
SELECT pg_temp.u('fast-start:' || id), 'activity', started_at, started_at + interval '1 minute',
       jsonb_build_object('fast', jsonb_build_object('id', id::text, 'started_at', pg_temp.iso(started_at)))
FROM fast WHERE NOT recorded;

INSERT INTO ev (id, kind, occurred_at, recorded_at, payload)
SELECT pg_temp.u('fast-end:' || id), 'activity', ended_at,
       ended_at + CASE WHEN recorded THEN interval '30 minutes' + pg_temp.r('fast-logged:' || id) * interval '4 hours' ELSE interval '1 minute' END,
       jsonb_build_object('fast', jsonb_build_object('id', id::text, 'ended_at', pg_temp.iso(ended_at), 'started_at', pg_temp.iso(started_at)))
FROM fast;

INSERT INTO ev (id, kind, occurred_at, recorded_at, payload)
SELECT pg_temp.u('food-close:' || d.day), 'activity', d.t + interval '21 hours 45 minutes', d.t + interval '21 hours 45 minutes',
       jsonb_build_object('food_day', jsonb_build_object('on', to_char(d.day, 'YYYY-MM-DD'), 'closed', true))
FROM days d WHERE pg_temp.r('close:' || d.day) < 0.3;

-- ---------------------------------------------------------------- Oura

-- An activity day is sent again as it goes on: three to eight versions, steps rising, each
-- naming the version before it, as the sync's external ids do.
CREATE TEMP TABLE activity_version AS
SELECT d.day, d.t, v.j, k.versions,
       (2000 + pg_temp.r('steps:' || d.day) * 12000)::int AS steps_final
FROM days d
CROSS JOIN LATERAL (SELECT 3 + floor(pg_temp.r('act-versions:' || d.day) * 6)::int AS versions) k
CROSS JOIN LATERAL generate_series(1, k.versions) v(j);

INSERT INTO ev (id, kind, provenance, occurred_at, recorded_at, source_id, external_id, payload)
SELECT pg_temp.u('oura-act:' || a.day || ':' || a.j), 'measurement', 'observed', a.t + interval '4 hours',
       CASE WHEN a.j = a.versions THEN a.t + interval '1 day 9 hours' ELSE a.t + interval '8 hours' + (a.j - 1) * interval '150 minutes' END,
       'oura',
       'daily_activity:' || to_char(a.day, 'YYYY-MM-DD') || ':' || pg_temp.hex('act:' || a.day || ':' || a.j, 16) || ':'
           || CASE WHEN a.j = 1 THEN 'first' ELSE pg_temp.u('oura-act:' || a.day || ':' || (a.j - 1))::text END,
       jsonb_build_object('oura_activity', jsonb_build_object(
           'day', to_char(a.day, 'YYYY-MM-DD'), 'oura_id', pg_temp.hex('oura-id:act:' || a.day, 24),
           'steps', steps, 'active_calories', steps / 25, 'total_calories', 2100 + steps / 25, 'walking_m', (steps * 0.75)::int,
           'high_s', steps / 10, 'medium_s', steps / 4, 'low_s', steps, 'sedentary_s', 30000, 'resting_s', 28000, 'non_wear_s', 600,
           'score', 50 + (pg_temp.r('act-score:' || a.day) * 45)::int))
FROM activity_version a CROSS JOIN LATERAL (SELECT a.steps_final * a.j / a.versions AS steps) s;

-- The day's sleep score and readiness, each sent twice; the night's sleep period twice, and a nap now and then.
INSERT INTO ev (id, kind, provenance, occurred_at, recorded_at, source_id, external_id, payload)
SELECT pg_temp.u('oura-' || c.name || ':' || d.day || ':' || v.j), 'measurement', 'observed', d.t,
       d.t + CASE WHEN v.j = 1 THEN interval '7 hours 30 minutes' ELSE interval '10 hours' END, 'oura',
       c.name || ':' || to_char(d.day, 'YYYY-MM-DD') || ':' || pg_temp.hex(c.name || ':' || d.day || ':' || v.j, 16) || ':'
           || CASE WHEN v.j = 1 THEN 'first' ELSE pg_temp.u('oura-' || c.name || ':' || d.day || ':1')::text END,
       CASE c.name
           WHEN 'daily_sleep' THEN jsonb_build_object('oura_sleep_day', jsonb_build_object(
               'day', to_char(d.day, 'YYYY-MM-DD'), 'oura_id', pg_temp.hex('oura-id:sleep:' || d.day, 24),
               'score', 55 + (pg_temp.r('sleep-score:' || d.day) * 40)::int + v.j,
               'contributors', jsonb_build_object('deep_sleep', 80, 'efficiency', 90, 'latency', 75, 'rem_sleep', 70, 'restfulness', 65, 'timing', 85, 'total_sleep', 80)))
           ELSE jsonb_build_object('oura_readiness', jsonb_build_object(
               'day', to_char(d.day, 'YYYY-MM-DD'), 'oura_id', pg_temp.hex('oura-id:ready:' || d.day, 24),
               'score', 55 + (pg_temp.r('ready-score:' || d.day) * 40)::int + v.j,
               'temperature_deviation', round((pg_temp.r('temp:' || d.day) - 0.5)::numeric, 2),
               'temperature_trend_deviation', round((pg_temp.r('trend:' || d.day) - 0.5)::numeric / 2, 2),
               'contributors', jsonb_build_object('activity_balance', 80, 'body_temperature', 95, 'hrv_balance', 70, 'previous_day_activity', 75, 'previous_night', 80, 'recovery_index', 85, 'resting_heart_rate', 90, 'sleep_balance', 80)))
       END
FROM days d
CROSS JOIN (VALUES ('daily_sleep'), ('daily_readiness')) c(name)
CROSS JOIN generate_series(1, 2) v(j);

CREATE TEMP TABLE sleep_period AS
SELECT d.day, d.t, 'long_sleep' AS type, md5('oura-sleep:' || d.day)::uuid::text AS oura_id,
       d.t - interval '1 hour' + pg_temp.r('bed:' || d.day) * interval '90 minutes' AS started_at,
       d.t + interval '6 hours 30 minutes' + pg_temp.r('wake:' || d.day) * interval '90 minutes' AS ended_at, 2 AS versions
FROM days d
UNION ALL
SELECT d.day, d.t, 'late_nap', md5('oura-nap:' || d.day)::uuid::text, d.t + interval '14 hours', d.t + interval '14 hours 40 minutes', 1
FROM days d WHERE pg_temp.r('nap:' || d.day) < 0.1;

INSERT INTO ev (id, kind, provenance, occurred_at, recorded_at, source_id, external_id, payload)
SELECT pg_temp.u('oura-sleep:' || p.oura_id || ':' || v.j), 'measurement', 'observed', p.started_at,
       p.ended_at + CASE WHEN v.j = 1 THEN interval '1 hour' ELSE interval '3 hours' END, 'oura',
       'sleep:' || p.oura_id || ':' || pg_temp.hex('sleep:' || p.oura_id || ':' || v.j, 16) || ':'
           || CASE WHEN v.j = 1 THEN 'first' ELSE pg_temp.u('oura-sleep:' || p.oura_id || ':1')::text END,
       jsonb_build_object('oura_sleep', jsonb_build_object(
           'id', p.oura_id, 'day', to_char(p.day, 'YYYY-MM-DD'), 'type', p.type,
           'start', pg_temp.iso(p.started_at), 'end', pg_temp.iso(p.ended_at),
           'in_bed_s', in_bed, 'asleep_s', in_bed * 9 / 10, 'deep_s', in_bed / 6, 'light_s', in_bed / 2, 'rem_s', in_bed / 5,
           'awake_s', in_bed / 10, 'latency_s', 600, 'efficiency', 88 + v.j, 'average_hrv', 40 + (pg_temp.r('hrv:' || p.day) * 30)::int,
           'average_hr', 55.5, 'lowest_hr', 48))
FROM sleep_period p
CROSS JOIN LATERAL (SELECT extract(epoch FROM p.ended_at - p.started_at)::int AS in_bed) b
CROSS JOIN LATERAL generate_series(1, p.versions) v(j);

-- Workouts on most days, some sent twice; a tag now and then. A walk is one Oura found; the
-- rest are recorded, live or entered afterwards, and count as exercise (ExerciseRules).
CREATE TEMP TABLE workout AS
SELECT d.day, d.t, md5('oura-workout:' || d.day)::uuid::text AS oura_id,
       (ARRAY['walking', 'running', 'cycling', 'strengthTraining'])[1 + floor(pg_temp.r('workout-kind:' || d.day) * 4)::int] AS activity,
       d.t + interval '17 hours' + pg_temp.r('workout-at:' || d.day) * interval '2 hours' AS started_at,
       CASE WHEN pg_temp.r('workout-twice:' || d.day) < 0.4 THEN 2 ELSE 1 END AS versions
FROM days d WHERE pg_temp.r('workout:' || d.day) < 0.6;

INSERT INTO ev (id, kind, provenance, occurred_at, recorded_at, source_id, external_id, payload)
SELECT pg_temp.u('oura-workout:' || w.oura_id || ':' || v.j), 'activity', 'observed', w.started_at,
       w.started_at + interval '2 hours' + (v.j - 1) * interval '12 hours', 'oura',
       'workout:' || w.oura_id || ':' || pg_temp.hex('workout:' || w.oura_id || ':' || v.j, 16) || ':'
           || CASE WHEN v.j = 1 THEN 'first' ELSE pg_temp.u('oura-workout:' || w.oura_id || ':1')::text END,
       jsonb_build_object('oura_workout', jsonb_build_object(
           'id', w.oura_id, 'day', to_char(w.day, 'YYYY-MM-DD'),
           'activity', w.activity,
           'label', NULL, 'start', pg_temp.iso(w.started_at), 'end', pg_temp.iso(w.started_at + interval '45 minutes'),
           'calories', 250 + v.j * 10, 'distance_m', 4000, 'intensity', 'moderate',
           'source', CASE WHEN w.activity = 'walking' THEN 'autodetected'
                          WHEN pg_temp.r('workout-live:' || w.day) < 0.7 THEN 'workout_heart_rate' ELSE 'manual' END))
FROM workout w CROSS JOIN LATERAL generate_series(1, w.versions) v(j);

INSERT INTO ev (id, kind, provenance, occurred_at, recorded_at, source_id, external_id, payload)
SELECT pg_temp.u('oura-tag:' || d.day), 'observation', 'observed', d.t + interval '15 hours', d.t + interval '1 day 9 hours', 'oura',
       'enhanced_tag:' || md5('oura-tag:' || d.day)::uuid::text || ':' || pg_temp.hex('tag:' || d.day, 16) || ':first',
       jsonb_build_object('oura_tag', jsonb_build_object(
           'id', md5('oura-tag:' || d.day)::uuid::text, 'day', to_char(d.day, 'YYYY-MM-DD'), 'end_day', NULL,
           'type_code', (ARRAY['tag_generic_caffeine', 'tag_generic_alcohol', 'tag_generic_late_meal'])[1 + floor(pg_temp.r('tag-kind:' || d.day) * 3)::int],
           'custom_name', NULL, 'start', pg_temp.iso(d.t + interval '15 hours'), 'end', NULL, 'comment', NULL))
FROM days d WHERE pg_temp.r('tag:' || d.day) < 0.08;

INSERT INTO ev (id, kind, occurred_at, recorded_at, payload)
SELECT pg_temp.u('workout-note:' || w.oura_id), 'note', w.started_at + interval '3 hours', w.started_at + interval '3 hours',
       jsonb_build_object('workout_note', jsonb_build_object('workout', w.oura_id, 'text', 'Felt good'))
FROM workout w WHERE pg_temp.r('workout-note:' || w.day) < 0.08;

-- ---------------------------------------------------------------- the Inbox and the journal

-- Captures through the day; most are dismissed within a few days, some filed as Ideas, and the
-- last fortnight's partly still waiting, with a few older stragglers.
CREATE TEMP TABLE capture AS
SELECT d.ago, d.day, c.n, pg_temp.u('capture:' || d.day || ':' || c.n) AS id,
       d.t + interval '8 hours' + pg_temp.r('capture-at:' || d.day || ':' || c.n) * interval '14 hours' AS at,
       pg_temp.r('capture-fate:' || d.day || ':' || c.n) AS fate
FROM days d CROSS JOIN LATERAL generate_series(1, floor(pg_temp.r('captures:' || d.day) * 6)::int) c(n);

INSERT INTO art (id, content, recorded_at)
SELECT pg_temp.u('capture-art:' || id), 'Capture ' || n || ' on ' || to_char(day, 'YYYY-MM-DD') || ': remember to look into this', at FROM capture;

INSERT INTO ev (id, kind, occurred_at, recorded_at, payload, artifact_id)
SELECT id, CASE WHEN pg_temp.r('voice:' || id) < 0.2 THEN 'voice' ELSE 'note' END, at, at, '{}', pg_temp.u('capture-art:' || id) FROM capture;

INSERT INTO ev (id, kind, occurred_at, recorded_at, payload)
SELECT pg_temp.u('capture-flag:' || id), 'triage', at + interval '1 second', at + interval '1 second',
       jsonb_build_object('state', 'flagged', 'item_id', id::text, 'item_kind', 'event')
FROM capture;

INSERT INTO ev (id, kind, occurred_at, recorded_at, payload)
SELECT pg_temp.u('capture-out:' || id), 'triage', out, out,
       jsonb_build_object('state', CASE WHEN fate < 0.9 THEN 'dismissed' ELSE 'promoted' END, 'item_id', id::text, 'item_kind', 'event')
FROM capture CROSS JOIN LATERAL (SELECT at + pg_temp.r('capture-out:' || id) * interval '5 days' AS out) z
WHERE out < now() AND NOT ((ago <= 14 AND pg_temp.r('capture-waits:' || id) < 0.4) OR pg_temp.r('capture-straggles:' || id) < 0.01);

INSERT INTO bsk.subject (id, urn, type, title, attributes, origin_event_id, created_at)
SELECT pg_temp.u('idea:' || c.id), pg_temp.urn('Idea', 'Idea ' || c.day || ' ' || c.n, 'idea:' || c.id), 'Idea', 'Idea ' || c.day || ' ' || c.n, '{}', NULL, e.occurred_at
FROM capture c JOIN ev e ON e.id = pg_temp.u('capture-out:' || c.id) AND e.payload->>'state' = 'promoted';

INSERT INTO edge (event_id, subject_id, relation)
SELECT c.id, pg_temp.u('idea:' || c.id), 'evidences'
FROM capture c JOIN ev e ON e.id = pg_temp.u('capture-out:' || c.id) AND e.payload->>'state' = 'promoted';

INSERT INTO art (id, content, recorded_at)
SELECT pg_temp.u('journal-art:' || d.day), 'Journal for ' || to_char(d.day, 'YYYY-MM-DD') || '. A day of ordinary things.', d.t + interval '21 hours'
FROM days d WHERE pg_temp.r('journal:' || d.day) < 0.7;

INSERT INTO ev (id, kind, occurred_at, recorded_at, payload, artifact_id)
SELECT pg_temp.u('journal:' || d.day), 'journal', d.t + interval '21 hours', d.t + interval '21 hours', '{}', pg_temp.u('journal-art:' || d.day)
FROM days d WHERE pg_temp.r('journal:' || d.day) < 0.7;

-- ---------------------------------------------------------------- the Home

CREATE TEMP TABLE space AS
SELECT * FROM (VALUES (1, 'Kitchen', 5, 3), (2, 'Living room', 4, 2), (3, 'Bedroom', 4, 3), (4, 'Bathroom', 3, 4),
                      (5, 'Office', 3, 2), (6, 'Entry', 2, 2), (7, 'Laundry', 2, 3), (8, 'Garage', 1, 1)) v(n, title, weight, pace);

INSERT INTO bsk.subject (id, urn, type, title, attributes, created_at)
SELECT pg_temp.u('space:' || n), pg_temp.urn('Space', title, 'space:' || n), 'Space', title,
       jsonb_build_object('pace', pace, 'weight', weight), (current_date - 1096)::timestamptz + interval '10 hours'
FROM space;

-- A walk-through every Sunday, and the kitchen checked again on Wednesdays.
CREATE TEMP TABLE rating AS
SELECT pg_temp.u('rating:' || d.day || ':' || s.n) AS id, d.day, d.t, s.n
FROM days d CROSS JOIN space s
WHERE extract(isodow FROM d.day) = 7 OR (s.n = 1 AND extract(isodow FROM d.day) = 3);

INSERT INTO ev (id, kind, occurred_at, recorded_at, payload)
SELECT pg_temp.u('rating-v' || v.j || ':' || r.id), 'measurement', at, at,
       jsonb_build_object('space_rating', jsonb_build_object(
           'id', r.id::text, 'day', to_char(r.day, 'YYYY-MM-DD'), 'space', pg_temp.u('space:' || r.n)::text,
           'tidiness', pg_temp.score(pg_temp.r('tidy:' || r.id || v.j), 2, 5),
           'aesthetics', pg_temp.score(pg_temp.r('pretty:' || r.id), 2.5, 5),
           'cleanliness', pg_temp.score(pg_temp.r('clean:' || r.id || v.j), 2, 5)))
FROM rating r
CROSS JOIN LATERAL generate_series(1, CASE WHEN pg_temp.r('rating-edited:' || r.id) < 0.05 THEN 2 ELSE 1 END) v(j)
CROSS JOIN LATERAL (SELECT r.t + interval '10 hours' + r.n * interval '2 minutes' + (v.j - 1) * interval '1 hour' AS at) z;

INSERT INTO edge (event_id, subject_id, relation)
SELECT id, (payload->'space_rating'->>'space')::uuid, 'concerns' FROM ev WHERE kind = 'measurement' AND payload ? 'space_rating';

-- ---------------------------------------------------------------- measurements

CREATE TEMP TABLE metric AS
SELECT * FROM (VALUES
    (1, 'Weight', '{"unit": "lb", "better": "lower", "catalog": "weight", "decimals": 1}'::jsonb),
    (2, 'Systolic', '{"unit": "mmHg", "better": "lower", "decimals": 0}'),
    (3, 'Diastolic', '{"unit": "mmHg", "better": "lower", "decimals": 0}'),
    (4, 'LDL', '{"unit": "mg/dL", "better": "lower", "decimals": 0, "panel": "lipids", "high": 100}'),
    (5, 'HDL', '{"unit": "mg/dL", "better": "higher", "decimals": 0, "panel": "lipids", "low": 40}'),
    (6, 'Triglycerides', '{"unit": "mg/dL", "better": "lower", "decimals": 0, "panel": "lipids", "high": 150}'),
    (7, 'A1C', '{"unit": "%", "better": "lower", "decimals": 1, "panel": "a1c", "high": 5.7}'),
    (8, 'Glucose', '{"unit": "mg/dL", "better": "lower", "decimals": 0, "panel": "cmp", "low": 70, "high": 99}')
) v(n, title, attributes);

INSERT INTO bsk.subject (id, urn, type, title, attributes, created_at)
SELECT pg_temp.u('metric:' || n), pg_temp.urn('Metric', title, 'metric:' || n), 'Metric', title, attributes, (current_date - 1096)::timestamptz + interval '10 hours'
FROM metric;

-- Twice-yearly lab visits, each with its results.
CREATE TEMP TABLE lab AS
SELECT pg_temp.u('lab:' || k) AS id, (current_date - 1080 + k * 180)::timestamptz + interval '9 hours' AS at
FROM generate_series(0, 5) k;

INSERT INTO ev (id, kind, occurred_at, recorded_at, payload)
SELECT pg_temp.u('lab-v:' || id), 'measurement', at, at + interval '3 days',
       jsonb_build_object('lab_visit', jsonb_build_object('id', id::text, 'lab', 'Quest', 'note', 'Fasting'))
FROM lab;

CREATE TEMP TABLE reading AS
SELECT pg_temp.u('reading:weight:' || d.day) AS id, 1 AS metric, d.t + interval '7 hours 15 minutes' AS at,
       round((190 - (1095 - d.ago) * 0.008 + pg_temp.r('weight:' || d.day) * 3)::numeric, 1) AS value, NULL::uuid AS visit
FROM days d WHERE pg_temp.r('weighed:' || d.day) < 0.85
UNION ALL
SELECT pg_temp.u('reading:bp:' || m || ':' || d.day), m, d.t + interval '8 hours',
       (CASE m WHEN 2 THEN 115 + floor(pg_temp.r('sys:' || d.day) * 20) ELSE 72 + floor(pg_temp.r('dia:' || d.day) * 12) END)::numeric, NULL
FROM days d CROSS JOIN generate_series(2, 3) m WHERE extract(isodow FROM d.day) = 7
UNION ALL
SELECT pg_temp.u('reading:lab:' || m || ':' || l.id), m, l.at,
       round((CASE m WHEN 4 THEN 110 WHEN 5 THEN 48 WHEN 6 THEN 140 WHEN 7 THEN 5.4 ELSE 92 END
              * (0.9 + pg_temp.r('lab:' || m || ':' || l.id) * 0.2))::numeric, 1), l.id
FROM lab l CROSS JOIN generate_series(4, 8) m;

INSERT INTO ev (id, kind, occurred_at, recorded_at, payload)
SELECT pg_temp.u('reading-v1:' || r.id), 'measurement', r.at, r.at + CASE WHEN r.visit IS NULL THEN interval '1 minute' ELSE interval '3 days' END,
       jsonb_build_object('metric_reading', jsonb_build_object('id', r.id::text, 'unit', m.attributes->>'unit', 'value', r.value,
                                                               'metric', pg_temp.u('metric:' || r.metric)::text)
           || CASE WHEN r.visit IS NULL THEN '{}' ELSE jsonb_build_object('visit', r.visit::text) END)
FROM reading r JOIN metric m ON m.n = r.metric;

INSERT INTO edge (event_id, subject_id, relation)
SELECT id, (payload->'metric_reading'->>'metric')::uuid, 'concerns' FROM ev WHERE kind = 'measurement' AND payload ? 'metric_reading';

-- ---------------------------------------------------------------- exception days and reviews

INSERT INTO ev (id, kind, occurred_at, recorded_at, payload)
SELECT pg_temp.u('exception:' || d.day), 'note', d.t + interval '20 hours', d.t + interval '20 hours',
       jsonb_build_object('exception_day', jsonb_build_object('day', to_char(d.day, 'YYYY-MM-DD'), 'comment', 'Sick'))
FROM days d WHERE pg_temp.r('exception:' || d.day) < 0.035;

-- A daily review most evenings, a weekly one most Sundays.
CREATE TEMP TABLE review AS
SELECT pg_temp.u('review:daily:' || d.day) AS id, 'daily' AS kind, d.day, d.t,
       'Daily review, ' || to_char(d.day, 'Dy Mon FMDD') AS title
FROM days d WHERE pg_temp.r('daily-review:' || d.day) < 0.8
UNION ALL
SELECT pg_temp.u('review:weekly:' || d.day), 'weekly', d.day, d.t, 'Weekly review, ' || to_char(d.day, 'Dy Mon FMDD')
FROM days d WHERE extract(isodow FROM d.day) = 7 AND pg_temp.r('weekly-review:' || d.day) < 0.85;

INSERT INTO bsk.subject (id, urn, type, title, attributes, created_at)
SELECT id, pg_temp.urn('Review', title, 'review:' || id), 'Review', title,
       jsonb_build_object('kind', kind, 'period', to_char(day, 'YYYY-MM-DD')), t + interval '20 hours 30 minutes'
FROM review;

INSERT INTO ev (id, kind, occurred_at, recorded_at, payload)
SELECT pg_temp.u('status:review:' || id), 'state_change', t + interval '21 hours', t + interval '21 hours',
       jsonb_build_object('status', 'Completed', 'subject_id', id::text)
FROM review WHERE pg_temp.r('review-done:' || id) < 0.85;

-- The Primary focus (focus plan G1): about two in five weekly reviews choose the newest Goal made
-- by then, and turn the review's Keep focus off.
CREATE TEMP TABLE focus AS
SELECT pg_temp.u('focus:' || r.id) AS id, r.id AS review_id, r.t,
       pg_temp.u('goal:' || least(15, greatest(1, (r.day - (current_date - 1095)) / 70))) AS goal_id
FROM review r WHERE r.kind = 'weekly' AND pg_temp.r('focus:' || r.id) < 0.4;

INSERT INTO ev (id, kind, occurred_at, recorded_at, payload)
SELECT id, 'note', t + interval '20 hours 45 minutes', t + interval '20 hours 45 minutes',
       jsonb_build_object('focus', jsonb_build_object('level', 'weekly', 'goal_id', goal_id::text, 'review_id', review_id::text))
FROM focus;

INSERT INTO edge (event_id, subject_id, relation)
SELECT id, goal_id, 'concerns' FROM focus
UNION ALL
SELECT id, review_id, 'concerns' FROM focus;

UPDATE bsk.subject SET attributes = attributes || '{"keep_focus": false}' WHERE id IN (SELECT review_id FROM focus);

-- ---------------------------------------------------------------- the reward catalog

INSERT INTO bsk.subject (id, urn, type, title, attributes, created_at)
SELECT pg_temp.u('reward:' || n), pg_temp.urn('Reward', title, 'reward:' || n), 'Reward', title,
       jsonb_build_object('price', jsonb_build_object('coins', coins)), (current_date - 1096)::timestamptz + interval '12 hours'
FROM (VALUES (1, 'Coffee out', 20), (2, 'Movie night', 60), (3, 'New book', 40)) v(n, title, coins);

INSERT INTO ev (id, kind, occurred_at, recorded_at, payload)
SELECT pg_temp.u('redeem:' || k), 'ledger', at, at,
       jsonb_build_object('entry', 'redeem', 'amounts', jsonb_build_object('coins', -coins), 'subject_id', pg_temp.u('reward:' || n)::text)
FROM generate_series(1, 40) k
CROSS JOIN LATERAL (SELECT 1 + k % 3 AS n, (current_date - 1080 + k * 26)::timestamptz + interval '19 hours' AS at) z
CROSS JOIN LATERAL (SELECT CASE n WHEN 1 THEN 20 WHEN 2 THEN 60 ELSE 40 END AS coins) c;

INSERT INTO edge (event_id, subject_id, relation)
SELECT id, (payload->>'subject_id')::uuid, 'concerns' FROM ev WHERE kind = 'ledger' AND payload->>'entry' = 'redeem';

-- ---------------------------------------------------------------- into the log, in the order it was written

INSERT INTO bsk.artifact (id, content, recorded_at) SELECT id, content, recorded_at FROM art ORDER BY recorded_at, id;

-- Nothing recorded after now: yesterday's last Oura sync may fall later this morning.
INSERT INTO bsk.event (id, kind, provenance, occurred_at, recorded_at, source_id, external_id, payload, derived_from, artifact_id)
SELECT id, kind, provenance, occurred_at, least(recorded_at, now() - interval '1 minute'), source_id, external_id, payload, derived_from, artifact_id
FROM ev ORDER BY least(recorded_at, now() - interval '1 minute'), id;

INSERT INTO bsk.subject_event (event_id, subject_id, relation, provenance)
SELECT event_id, subject_id, relation, provenance FROM edge;

-- ---------------------------------------------------------------- what the items earned

-- The bounty's defaults (BountyDefaults), for an item that sets none.
CREATE TEMP TABLE price (type text, trigger text, amounts jsonb);
INSERT INTO price VALUES
    ('Task', 'completed', '{"freedom": 10, "xp": 10}'),
    ('Project', 'completed', '{"freedom": 30, "gems": 1, "xp": 50}'),
    ('Goal', 'completed', '{"freedom": 60, "gems": 3, "xp": 150}'),
    ('Habit', 'followed', '{"freedom": 5, "xp": 5}'),
    ('Habit', 'streak_7', '{"freedom": 15, "xp": 25}'),
    ('Habit', 'streak_30', '{"freedom": 30, "xp": 75}'),
    ('Habit', 'streak_100', '{"freedom": 90, "gems": 1, "xp": 200}'),
    ('Habit', 'established', '{"freedom": 60, "gems": 1, "xp": 100}');

-- Every award the views say is due, paid at its item's bounty (a repetition's series'), or the
-- default; a partial day pays half of a followed one. A trigger that prices to nothing pays nothing.
CREATE TEMP TABLE earn AS
SELECT d.award_key, d.trigger, d.subject_id, d.source_event_ids, d.triggered_at,
       coalesce(d.occurred_on, d.triggered_at::date) AS credited_on, p.amounts
FROM bsk.v_reward_due d
JOIN bsk.subject s ON s.id = d.subject_id
LEFT JOIN bsk.subject ser ON ser.urn = s.attributes->>'series'
CROSS JOIN LATERAL (SELECT coalesce(s.attributes->'bounty', ser.attributes->'bounty', '{}') AS bounty) b
CROSS JOIN LATERAL (
    SELECT CASE WHEN d.trigger = 'partial' THEN
               (SELECT jsonb_object_agg(k, v::bigint / 2)
                FROM jsonb_each_text(coalesce(b.bounty->'followed', (SELECT amounts FROM price WHERE type = s.type AND trigger = 'followed'))) kv(k, v)
                WHERE v::bigint / 2 > 0)
           ELSE coalesce(b.bounty->d.trigger, (SELECT amounts FROM price WHERE type = s.type AND trigger = d.trigger)) END AS amounts) p
WHERE p.amounts IS NOT NULL AND p.amounts <> '{}';

INSERT INTO bsk.event (id, kind, provenance, occurred_at, recorded_at, source_id, payload, derived_from)
SELECT pg_temp.u('earn:' || award_key), 'ledger', 'derived', triggered_at, least(triggered_at + interval '2 seconds', now() - interval '1 minute'), 'api',
       jsonb_build_object('entry', 'earn', 'amounts', amounts, 'award_key', award_key, 'trigger', trigger,
                          'subject_id', subject_id::text, 'credited_on', to_char(credited_on, 'YYYY-MM-DD')),
       source_event_ids
FROM earn ORDER BY triggered_at, award_key;

INSERT INTO bsk.subject_event (event_id, subject_id, relation, provenance)
SELECT pg_temp.u('earn:' || award_key), subject_id, 'concerns', 'derived' FROM earn;
