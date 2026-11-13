# LifeOS docs

Where to find what. Each feature plan has numbered phases, their status and Chris's decisions;
a plan's decisions section settles a question, so read it before asking again.

## The model and the spec

- [ontology.md](ontology.md): the model. The three layers (source, subject, derived), the subject
  types, the event kinds, the relations, provenance, and the invariants that keep the record
  honest.
- [pilot/ui-requirements.md](pilot/ui-requirements.md): the living spec, with the requirement IDs
  (TASKS-3, MEASURE-1, ...) and the decisions (D1, D2, ...).
- [pilot/inbox-attention-vs-status.md](pilot/inbox-attention-vs-status.md): why an Inbox flag is
  attention, not status.

## Feature plans

| Plan | What it covers |
| --- | --- |
| [kernel-build-plan.md](pilot/kernel-build-plan.md) | The Pilot's ontology backlog: shared primitives, creation flows, recurrence and Habits, Appointments. |
| [ui-refactor-plan.md](pilot/ui-refactor-plan.md) | The WinForms Pilot's UI. |
| [production-ui-plan.md](pilot/production-ui-plan.md) | The React web app and the .NET API behind it. |
| [rewards-plan.md](pilot/rewards-plan.md) | Currencies, the reward ledger and redeeming (D11). |
| [xp-domains-plan.md](pilot/xp-domains-plan.md) | Life-domain XP, levels, ranks and consistency (D12, D17). |
| [food-log-plan.md](pilot/food-log-plan.md) | Meals, fasts, drinks and diet XP (D13). |
| [activity-plan.md](pilot/activity-plan.md) | Oura steps, exercise, sleep, readiness and activity XP (D14). |
| [cover-images-plan.md](pilot/cover-images-plan.md) | Unsplash covers and the rules for using them (D15). |
| [recurring-tasks-plan.md](pilot/recurring-tasks-plan.md) | Repeating Tasks (D16). |
| [environment-plan.md](pilot/environment-plan.md) | Spaces, room ratings, photos and the Home Score (D18). |
| [measurements-plan.md](pilot/measurements-plan.md) | Weight, any metric, lab visits and charts (D20). |
| [reviews-plan.md](pilot/reviews-plan.md) | Daily and weekly reviews (D3, D8). |
| [agent-access-plan.md](pilot/agent-access-plan.md) | Backups, then the `/mcp` connector, its write levels, and an assistant. |
| [code-review-plan.md](pilot/code-review-plan.md) | The code review fixes, CR1 to CR26. |

[pilot/production-ui-types.ts](pilot/production-ui-types.ts) is the interim, hand-written type
seed the web app started from. The web's API types are now generated
(`scripts/generate-web-client.ps1`).

## Storage, hosting and operations

- [pilot/binary-artifacts.md](pilot/binary-artifacts.md): where an artifact's bytes live, in
  Postgres or object storage.
- [pilot/supabase-hosting.md](pilot/supabase-hosting.md): the staging database on Supabase, the
  pooler and the roles.
- [pilot/deploy.md](pilot/deploy.md): Fly, GitHub environments, secrets and the deploy workflow.
- [pilot/backup-restore.md](pilot/backup-restore.md): the nightly encrypted backup and how to
  restore it (OPS-1).

## The notes vault

- [notes-system-plan.md](notes-system-plan.md): Paperless, sync, the frozen spec, and how
  LifeOS links to them.
- [notes-spec.md](notes-spec.md): the frozen Notes Spec the vault's notes follow.

## For agents working on the code

- [../AGENTS.md](../AGENTS.md): rules and gotchas that are true today.
- [agents/verifying.md](agents/verifying.md): checking a change in the running app, with the real
  API, a throwaway database or the Pilot.
