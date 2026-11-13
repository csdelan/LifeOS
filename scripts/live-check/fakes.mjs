// Fakes of every outside service the API calls, for a live check (scripts/live-check.ps1,
// code review CR40). One http server on 127.0.0.1 serves:
//
// - Supabase Auth: the empty JWKS, so the API trusts only the HS256 secret the script signs with.
// - Oura: authorize (redirects straight back with a code), token, revoke, and the collections,
//   from tests/LifeOs.Tests/OuraFixtures with their dates moved so the newest is today. Never real
//   Oura data: it's against Oura's API agreement.
// - Unsplash: one photo by id and search, from tests/LifeOs.Tests/UnsplashFixtures, with download
//   links pointing back here (the API follows them only on its own Unsplash host).
// - Supabase Storage, in memory: an upload before the bucket exists is refused as Supabase
//   refuses it, so the API makes the bucket and tries again.
//
// Usage: node fakes.mjs <port> <fixtures folder>

import { readFileSync } from "node:fs";
import { createServer } from "node:http";
import { join } from "node:path";

const port = Number(process.argv[2] ?? 5399);
const fixtures = process.argv[3] ?? join(import.meta.dirname, "..", "..", "tests", "LifeOs.Tests");
const self = `http://127.0.0.1:${port}`;

const read = (...parts) => JSON.parse(readFileSync(join(fixtures, ...parts), "utf8"));

// --- Oura ----------------------------------------------------------------------------------

const ouraCollections = ["daily_activity", "daily_readiness", "daily_sleep", "enhanced_tag", "sleep", "workout"];
const DAY = 86_400_000;

/** Today on this machine's calendar, as Oura names a day. */
function today() {
  const now = new Date();
  return new Date(Date.UTC(now.getFullYear(), now.getMonth(), now.getDate())).toISOString().slice(0, 10);
}

/** Every string that starts with a date, moved by `days`: days, timestamps and datetimes alike. */
function shift(value, days) {
  if (typeof value === "string") {
    const match = /^(\d{4}-\d{2}-\d{2})(.*)$/s.exec(value);
    if (!match) return value;
    const moved = new Date(Date.parse(`${match[1]}T00:00:00Z`) + days * DAY).toISOString().slice(0, 10);
    return moved + match[2];
  }
  if (Array.isArray(value)) return value.map((item) => shift(item, days));
  if (value && typeof value === "object") {
    return Object.fromEntries(Object.entries(value).map(([key, item]) => [key, shift(item, days)]));
  }
  return value;
}

const dayOf = (record) => record.day ?? record.start_day;

/** A collection's fixture records, moved so the newest is today, inside [start, end]. */
function collection(name, start, end) {
  const records = read("OuraFixtures", `${name}.json`).data;
  const newest = records.map(dayOf).sort().at(-1);
  const days = Math.round((Date.parse(today()) - Date.parse(newest)) / DAY);
  return records
    .map((record) => shift(record, days))
    .filter((record) => (!start || dayOf(record) >= start) && (!end || dayOf(record) <= end));
}

let tokens = 0;
function ouraTokens() {
  tokens += 1;
  return {
    access_token: `fake-oura-access-${tokens}`,
    refresh_token: `fake-oura-refresh-${tokens}`,
    expires_in: 86_400,
    token_type: "bearer",
    scope: "extapi:daily extapi:workout extapi:tag",
  };
}

// --- Unsplash ------------------------------------------------------------------------------

/** A fixture photo as `id`, its download link pointing back here. */
function unsplashPhoto(photo, id = photo.id) {
  return {
    ...photo,
    id,
    links: { ...photo.links, download_location: `${self}/photos/${encodeURIComponent(id)}/download` },
  };
}

// --- Supabase Storage ----------------------------------------------------------------------

const buckets = new Map(); // bucket -> Map(key -> { type, bytes })

/** Supabase Storage answers most refusals 400, with the real status in the body. */
function storageError(res, status, error, message) {
  json(res, 400, { statusCode: String(status), error, message });
}

// --- Server --------------------------------------------------------------------------------

function json(res, status, body) {
  res.writeHead(status, { "content-type": "application/json" });
  res.end(JSON.stringify(body));
}

async function bodyOf(req) {
  const chunks = [];
  for await (const chunk of req) chunks.push(chunk);
  return Buffer.concat(chunks);
}

async function handle(req, res) {
  const url = new URL(req.url, self);
  const path = url.pathname;
  const q = url.searchParams;
  let m;

  if (path === "/health") return json(res, 200, { ok: true });

  // Supabase Auth
  if (path === "/auth/v1/.well-known/jwks.json") return json(res, 200, { keys: [] });

  // Oura
  if (req.method === "GET" && path === "/oauth/authorize") {
    const back = new URL(q.get("redirect_uri"));
    back.searchParams.set("code", "fake-oura-code");
    back.searchParams.set("state", q.get("state") ?? "");
    res.writeHead(302, { location: back.toString() });
    return res.end();
  }
  if (req.method === "POST" && path === "/oauth/token") {
    await bodyOf(req);
    return json(res, 200, ouraTokens());
  }
  if (path === "/oauth/revoke") return json(res, 200, {});
  if (req.method === "GET" && (m = /^\/v2\/usercollection\/([a-z_]+)$/.exec(path))) {
    if (!/^Bearer fake-oura-access-/.test(req.headers.authorization ?? "")) {
      return json(res, 401, { detail: "The fake Oura wants a token it issued." });
    }
    if (!ouraCollections.includes(m[1])) return json(res, 404, { detail: `No collection ${m[1]}.` });
    return json(res, 200, { data: collection(m[1], q.get("start_date"), q.get("end_date")), next_token: null });
  }

  // Unsplash
  if (req.method === "GET" && path === "/search/photos") {
    const page = read("UnsplashFixtures", "search.json");
    return json(res, 200, { ...page, results: page.results.map((photo) => unsplashPhoto(photo)) });
  }
  if (req.method === "GET" && (m = /^\/photos\/([^/]+)\/download$/.exec(path))) {
    return json(res, 200, { url: `${self}/photos/${m[1]}` });
  }
  if (req.method === "GET" && (m = /^\/photos\/([^/]+)$/.exec(path))) {
    return json(res, 200, unsplashPhoto(read("UnsplashFixtures", "photo.json"), decodeURIComponent(m[1])));
  }

  // Supabase Storage
  if (req.method === "POST" && path === "/storage/v1/bucket") {
    const { id, name } = JSON.parse((await bodyOf(req)).toString("utf8") || "{}");
    const bucket = id ?? name;
    if (buckets.has(bucket)) return storageError(res, 409, "Duplicate", "The resource already exists");
    buckets.set(bucket, new Map());
    return json(res, 200, { name: bucket });
  }
  if ((m = /^\/storage\/v1\/object\/([^/]+)\/(.+)$/.exec(path))) {
    const [bucket, key] = [decodeURIComponent(m[1]), decodeURIComponent(m[2])];
    const objects = buckets.get(bucket);
    if (req.method === "POST" || req.method === "PUT") {
      const bytes = await bodyOf(req);
      if (!objects) return storageError(res, 404, "Bucket not found", "Bucket not found");
      if (objects.has(key) && req.headers["x-upsert"] !== "true") {
        return storageError(res, 409, "Duplicate", "The resource already exists");
      }
      objects.set(key, { type: req.headers["content-type"] ?? "application/octet-stream", bytes });
      return json(res, 200, { Key: `${bucket}/${key}` });
    }
    if (req.method === "GET") {
      const object = objects?.get(key);
      if (!object) return storageError(res, 404, "not_found", "Object not found");
      res.writeHead(200, { "content-type": object.type });
      return res.end(object.bytes);
    }
  }

  return json(res, 404, { message: `The fakes don't serve ${req.method} ${path}.` });
}

createServer((req, res) => {
  handle(req, res)
    .catch((error) => json(res, 500, { message: String(error?.message ?? error) }))
    .finally(() => console.log(`${new Date().toISOString()} ${req.method} ${req.url} ${res.statusCode}`));
}).listen(port, "127.0.0.1", () => console.log(`fakes listening on ${self}`));
