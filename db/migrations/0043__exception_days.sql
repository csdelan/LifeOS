-- 0043__exception_days.sql
-- Days Chris marks as exceptions, folded from append-only events (xp-domains plan X9, §7.4.3;
-- REWARD-8, D17).
--
--   * An exception day is Chris's own `note` event (declared), with no subject and no edge.
--     The payload has one key:
--       exception_day  {day, comment}
--     `day` is a home-zone date; `comment` says why (sickness, injury, travel), one line.
--   * The NEWEST version per day WINS, by recorded_at (then id). Changing the comment writes a
--     newer version; unmarking writes {day, voided: true}, as deleting a meal does, and marking
--     the day again writes a newer version still.
--
-- From X10 the life-domain fold skips these days: they neither help nor hurt consistency, the
-- multiplier's window or a falling level. Until then nothing reads them but `bsk domains
-- exceptions` and the API. None of these events concerns an item, so the inbox, the
-- diagnostics, the habit and timer views and the ledger never see them.

CREATE OR REPLACE VIEW bsk.v_exception_day AS
WITH version AS (
    SELECT
        e.id                                            AS event_id,
        e.payload->'exception_day'                      AS x,
        bsk.try_to_date(e.payload->'exception_day'->>'day') AS day,
        e.recorded_at,
        row_number() OVER (
            PARTITION BY bsk.try_to_date(e.payload->'exception_day'->>'day')
            ORDER BY e.recorded_at DESC, e.id DESC)     AS rn
    FROM bsk.event e
    WHERE e.kind = 'note'
      AND jsonb_exists(e.payload, 'exception_day')
      AND jsonb_typeof(e.payload->'exception_day') = 'object'
      AND bsk.try_to_date(e.payload->'exception_day'->>'day') IS NOT NULL
)
SELECT
    v.day,
    btrim(v.x->>'comment')                              AS comment,
    v.recorded_at                                       AS marked_at,
    v.event_id
FROM version v
WHERE v.rn = 1
  AND coalesce(v.x->>'voided', 'false') <> 'true'
  AND jsonb_typeof(v.x->'comment') = 'string'
  AND btrim(v.x->>'comment') <> '';

COMMENT ON VIEW bsk.v_exception_day IS
    'REWARD-8 (D17): one row per day Chris has marked as an exception — the newest version of each day by recorded_at, unless voided. marked_at is when that version was written. From X10 the life-domain fold skips these days.';

-- Keep the view off a full scan of the stream, as 0037 and 0039 do for their collections.
CREATE INDEX IF NOT EXISTS event_exception_day_idx
    ON bsk.event ((payload->'exception_day'->>'day'), recorded_at)
    WHERE kind = 'note' AND jsonb_exists(payload, 'exception_day');

GRANT SELECT ON bsk.v_exception_day TO bsk_reader;
