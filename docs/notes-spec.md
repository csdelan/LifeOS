# Notes Spec (draft for Chris to freeze)

**Status: draft, 2026-11-12.** This is the one-page spec the [notes system plan](notes-system-plan.md)
calls for. Chris marks it up, then freezes it. Once frozen it moves into the vault as
`99 System/Notes Spec.md`, the vault's `CLAUDE.md` points to it, and nothing in it changes for
90 days. The open questions are at the end.

It governs the Obsidian vault `Chris2.0` (`C:\Users\csdel\Vaults\Chris2.0` on PICARD, synced to
the other devices by Obsidian Sync). LifeOS is the system of record for anything with a status;
the vault holds text worth keeping.

## 1. What goes where

| Kind of thing | Lives in | Why |
|---|---|---|
| Tasks, projects' status and dates, habits, ideas, decisions, appointments | **LifeOS** | They have a status and a due date, earn XP and show on Focus. |
| Reflections: daily, weekly and monthly reviews, journaling | **Vault** (`reflection`) | Chris decided this on 2026-11-12 (reviews plan §4). The LifeOS review links to the note. |
| Working notes for a LifeOS Project, Goal or Area: brainstorming, research, support material | **Vault** (`project`) | Long text; the item itself stays in LifeOS. |
| Chris's own understanding of a topic: lessons, frameworks, course takeaways | **Vault** (`knowledge`) | |
| Facts to look up later: the house, appliances, supplements, how-tos, account details (never passwords) | **Vault** (`reference`) | |
| Something someone else made: articles, clippings, books, videos, courses | **Vault** (`source`) | |
| Documents and scans | **Paperless** | Linked by `paperless:<id>`, never copied in. |

No checkboxes that mean "to do" in the vault. A checklist inside a note is fine as content; work
to be done goes to LifeOS.

## 2. Note types

Five types, no more. Every note has `type` and `created`; each type adds only what it needs.

| `type` | Folder | Required front matter | Optional |
|---|---|---|---|
| `reflection` | `10 Reflections/` | `kind` (`daily`, `weekly`, `monthly`, `journal`), `period`, `summary` | `review`, `transcript`, `tags` |
| `project` | `20 Projects/` | `related` (at least one LifeOS URN) | `tags` |
| `knowledge` | `30 Knowledge/` | — | `area`, `related`, `tags` |
| `reference` | `40 Reference/` | — | `area`, `related`, `tags` |
| `source` | `50 Sources/` | `source` (URL or citation), `summary` | `author`, `published`, `area`, `related`, `tags` |

```yaml
---
type: reflection
created: 2026-11-12
kind: daily
period: 2026-11-12          # the day; a weekly's Sunday; a monthly's yyyy-MM
review: urn:bsk:review:...  # the LifeOS review it answers, when there is one
transcript: urn:bsk:...     # the raw dictation (a LifeOS capture), or a note in 95 Transcripts
summary: Two or three sentences an agent wrote and Chris can correct.
---
```

- **Field names** are lowercase single words. No spaces, no emoji in values.
- **Dates** are `yyyy-MM-dd`, days in Pacific time, like LifeOS.
- **`related`** is a list of LifeOS URNs (`urn:bsk:project:...`); **`area`** is one of the LifeOS
  Areas by name: Entrepreneur, Personal Growth, Software Engineer Career, Trader.
- No `status`, `priority` or `due date` anywhere: LifeOS owns those.

### Reflection body

A review's reflection has the same three parts as the LifeOS review page, after the summary:

```markdown
## Summary
## What worked well
## What I'd do differently
## Plan
```

A weekly adds `## The week` (links to its daily reflections); a monthly adds `## The month` (links
to its weekly reflections and any daily ones they don't cover, as REVIEW-3 asks). A `journal`
reflection has a summary and free text.

## 3. Folders and names

```
00 Inbox/          raw captures and anything not yet filed
10 Reflections/
20 Projects/
30 Knowledge/
40 Reference/
50 Sources/
90 Attachments/    every image, PDF, audio and video file
95 Transcripts/    raw dictations that didn't come through LifeOS, kept untouched
98 Archive/        finished or retired notes of any type; the type stays in front matter
99 System/         this spec, templates, schemas, dashboards
```

- **Flat:** no subfolders inside a type folder. Area and topic are front matter, not folders.
- **Names** are the note's title in plain words: no emoji, no `[ ] # ^ | \ / : ?`. Every name is
  unique in the vault, since Obsidian links by name.
- **Reflections** are named by period: `2026-11-12 Daily reflection`,
  `2026-11-15 Weekly reflection` (its Sunday), `2026-11 Monthly reflection`,
  `2026-11-12 Journal - <topic>`.

## 4. Links and tags

- **Between notes:** wiki-links in the body (`[[Note name]]`), only for real relationships.
- **To LifeOS:** URNs in `related` or `review`. **From LifeOS:** the note's path in the vault,
  opened with an `obsidian://` link.
- **Tags** come from this list only; anything else is a validation failure:
  `health`, `home`, `finance`, `trading`, `career`, `blueskies`, `learning`, `family`, `games`,
  `adhd`, `mindset`, `moc` (a note that's mainly an index of other notes).

## 5. Agents in the vault

- **Modes:** the vault's existing rules hold. APPEND-ONLY is the default; DIRECT-MODIFY logs every
  change in `99 System/change_logs/`. An agent adds new notes and sections; restructuring a note
  Chris wrote needs his review.
- **Formatting:** an agent turns a capture into a note of the right type, with the front matter,
  summary and links above, and keeps the raw text untouched in `transcript`.
- **When unsure,** it files nothing and asks through the inbox with two to four options and its
  recommendation.
- **Git:** the vault is a Git repository on PICARD only. One commit per processed item, naming the
  capture. `90 Attachments/` and `.obsidian/workspace*` are not committed. Obsidian Sync doesn't
  sync `.git`.
- **Moves and renames** happen inside Obsidian, or the agent rewrites every link to the note in
  the same commit.
- **Never:** secrets or passwords in a note; the Local REST API's settings in a commit or a reply.

## 6. Enforcement

A validator checks each note against a JSON Schema for its type (`99 System/schemas/`): the
required front matter, the folder matching the type, unique names, tags from the list, and a
reflection's sections. A note that fails goes to a "needs fixing" list instead of being accepted
quietly.

## 7. Freeze rule

Once frozen, no changes for 90 days. After that, changes happen only at a quarterly review and
are recorded in the changelog below.

## Open before freezing

1. **Folders by type, not by area.** Today's vault is organised PARA-style by area (Areas, My Life,
   Health, Market). This spec files by type and puts the area in front matter. LifeOS already
   gives the by-area view, so the vault doesn't need to.
2. **Tasks leave the vault.** The vault has 154 open checkboxes, mostly in project notes. The
   cleanup lists them for triage into LifeOS rather than moving them silently.
3. **Large videos leave the vault.** About 3.6 GB of course videos (300–500 MB each) sit in
   attachments. They likely exceed Obsidian Sync's file-size limit, so they may exist only on
   PICARD. Proposed: move them to OneDrive and link them from a `source` note.
4. **Plugins.** Proposed keep list: Templater (a template per type, for notes made by hand),
   Dataview (read-only views), the theme and Style Settings. Proposed removals: Tasks, Kanban, Full
   Calendar, Big Calendar, Make.md (it leaves `.mdc`, `.mdb` and `.space` files), the column, color
   and icon plugins. Local REST API stays only if an agent uses it.
5. **The tag list** in §4.

## Changelog

- 2026-11-12: first draft.
