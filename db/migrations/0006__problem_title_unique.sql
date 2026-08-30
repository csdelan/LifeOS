-- 0006__problem_title_unique.sql
-- Back the resolve-or-create contract with a real uniqueness guarantee, so a
-- duplicate can never be created silently — not by a race, and not by a future
-- non-CLI writer. The premise "the durable object is the question you return to"
-- only holds if there is exactly one such object per question.
--
-- This is deliberately NOT a blanket (type, title) unique index: types like Task
-- legitimately have many subjects with the same title. Only reuse-by-title types
-- are constrained. Problem is the first; others graduate in by extending the
-- predicate to `type IN ('Problem', ...)` as they adopt resolve-or-create.
CREATE UNIQUE INDEX IF NOT EXISTS subject_reuse_title_key
    ON bsk.subject (type, title)
    WHERE type = 'Problem';
