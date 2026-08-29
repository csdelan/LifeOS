-- 0001__baseline.sql
-- The baseline migration establishes the schema that later Stage 1 migrations
-- build on. Domain tables (event, artifact, subjects, relations) are introduced
-- by their own migrations in subsequent issues; this file only creates the
-- namespace so those scripts have somewhere to land.
--
-- Every statement here must be idempotent on its own so a partially-applied
-- baseline can be re-run safely.

CREATE SCHEMA IF NOT EXISTS bsk;

COMMENT ON SCHEMA bsk IS 'Life Kernel (bsk) application schema.';
