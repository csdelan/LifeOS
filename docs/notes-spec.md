# Notes Spec (draft, iterating toward the first freeze)

**Status: draft v0.7, 2026-11-12.** This is the spec the [notes system plan](notes-system-plan.md)
calls for. It isn't frozen yet: Chris freezes it only after he has seen the vault reorganized to
it (notes plan N3). Until then each round of the reorganization can change it, and the
changelog at the end records each change. Once frozen it moves into the vault as
`99 System/Notes Spec.md`, the vault's `CLAUDE.md` points to it, and nothing in it changes for 90
days.

It governs the Obsidian vault `Chris2.0` (`C:\Users\csdel\Vaults\Chris2.0` on PICARD, synced to
the other devices by Obsidian Sync). LifeOS is the system of record for anything with a status;
the vault holds text worth keeping, and agents keep it organized and linked.

## 1. What goes where

| Kind of thing | Lives in | Why |
|---|---|---|
| Tasks, projects' status and dates, habits, ideas, decisions, appointments | **LifeOS** | They have a status and a due date, earn XP and show on Focus. |
| Reflections: daily, weekly and monthly reviews, journaling | **Vault** (`reflection`) | Chris decided this on 2026-11-12 (reviews plan §4). The LifeOS review links to the note. |
| Working notes for a LifeOS Project, Goal or Area: brainstorming, research, support material | **Vault** (`project`) | Long text; the item itself stays in LifeOS. |
| Chris's own thinking: his rules, ideas and conclusions, not tied to one work | **Vault** (`knowledge`) | It may say where an idea came from in `source`. |
| Facts to look up later: the house, appliances, supplements, how-tos, account details (never passwords) | **Vault** (`reference`) | |
| Something someone else made, or Chris's notes on it: articles, clippings, books, videos, courses | **Vault** (`source`) | Chris decided on 2026-11-12 that notes on one work are a source, even in his own words. |
| Videos and other large media | **OneDrive** `Vault Media` (§5) | Too big for the vault and for Obsidian Sync; a note links to each one. |
| Documents and scans | **Paperless** | Linked by `paperless:<id>`, never copied in. |
| Checklists | **Vault** (`reference` tagged `checklist`) | Chris decided on 2026-11-12 to read and follow them first (CHECKLIST-1): a checklist is a numbered list with no checkboxes. Runs aren't recorded; a reflection mentions anything skipped. A LifeOS Checklist type that records runs may come later. |

**Zero task objects in the vault** (Chris, 2026-11-12). No `- [ ]` checkboxes anywhere: the
validator fails a note that has one. Work to be done goes to LifeOS.

**Source or knowledge** (Chris, 2026-11-12). Ask whether the note is about one outside work. If it
is, it's a `source`, whether it holds the work's own text or Chris's notes on it: his notes on
Tom Bilyeu's courses and on each day of the Wisey ADHD course are sources, with `source` naming
the course. If it's his own thinking, drawn from many places or none (My Rules in Life), it's
`knowledge`. The line lets an agent tell an author's view from Chris's own, and keeps long
clippings out of the list of his own ideas.

## 2. Note types

Five content types. Every note has `type` and `created`; each type adds only what it needs.
Generated maps (§6) are system notes with `type: moc`, not a sixth content type.

| `type` | Folder | Required front matter | Optional |
|---|---|---|---|
| `reflection` | `10 Reflections/` | `kind` (`daily`, `weekly`, `monthly`, `journal`), `period`, `summary` | `review`, `transcript`, `tags` |
| `project` | `20 Projects/` | `related` (at least one LifeOS URN) | `tags` |
| `knowledge` | `30 Knowledge/` | — | `source` (where an idea came from), `area`, `related`, `tags` |
| `reference` | `40 Reference/` | — | `area`, `related`, `tags` |
| `source` | `50 Sources/` | `source` (URL or citation), `summary` | `author`, `published`, `media`, `area`, `related`, `tags` |

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

Folders matter less than they used to: agents file notes and the generated maps (§6) are how
Chris finds them. The folders below exist so every note has one obvious home and the validator
can check it. Today's folders (`00 - Inbox`, `10 - Projects`, `200-Areas`, `300-Resources`,
`900-Journals` and the rest) are retired as their notes move.

```
Home.md            the dashboard (§6)
00 Inbox/          raw captures and anything not yet filed
05 MoC/            generated maps (§6)
10 Reflections/
20 Projects/
30 Knowledge/
40 Reference/
50 Sources/
90 Attachments/    images, PDFs and audio that are small enough to sync (§5)
95 Transcripts/    raw dictations that didn't come through LifeOS, kept untouched
98 Archive/        finished or retired notes of any type; the type stays in front matter
99 System/         this spec, MoC definitions, templates, schemas, tools, change logs
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
  `adhd`, `mindset`, `checklist`.

### Related links an agent adds

Chris doesn't link notes by hand: agents find related notes and link them (Chris, 2026-11-12). A
reflection about losing focus, for instance, gets links to the Wisey ADHD course notes.

- **Where:** a Related section at the end of the note, between `%% related:start %%` and
  `%% related:end %%`. Each line names the kind of link, the note, and the specific thing behind
  it:

  ```markdown
  %% related:start %%
  ## Related

  - Answers: [[ADHD Day 3-4]]: capture mode, to empty the mind when overwhelmed
  %% related:end %%
  ```

  The agent writes only inside the markers. Links in Chris's own text are his, and the section
  doesn't repeat them. The markers are Obsidian comments: Reading view hides them, and the
  `hide-comments` CSS snippet hides them in Live Preview too. Source mode shows them.
- **Four kinds of link, and nothing else** (Chris, 2026-11-12, after the first backfill made the
  graph a web where everything linked to everything):
  - **Part of:** a lesson and its course, or a design note and its project.
  - **Continues:** a later note picks up the same thread, like Lyn Alden's next report on the
    same theme.
  - **Answers:** the note raises a problem and the target holds a specific answer to it.
  - **Same thing:** both notes are about the same trade, repo, service, person or event.

  A shared theme ("both about trading", "both about focus") is never enough: tags and maps cover
  themes. The why names the specific thing: the technique, the repo, the trade.
- **Few:** at most two per note, best first. Most notes have none. A note that many notes would
  link for the same reason is a theme, so it isn't linked.
- **One direction:** from the more specific or later note to the more general or earlier one,
  never both ways. A series links to its hub (the ADHD days to the course note), not each member to
  the others. A reflection links out to projects, knowledge, reference and sources; nothing links to
  a reflection, since Obsidian's backlinks list the reflections that link a note. Archived notes
  aren't linked.
- **When:** as a note is filed, it gets links to the notes already there. A nightly sweep goes the
  other way: when a note arrives, older notes it fits get a line. The vault as found got one
  backfill pass, in the reorganization (notes plan N3).
- **No approval step.** Links appear; Chris deletes the ones that are wrong, and the agent doesn't
  add a link he deleted again (the vault's Git history shows it).

## 5. Media

- **Videos, and any file over 20 MB, live in OneDrive,** in `C:\Users\csdel\OneDrive\Vault Media\`,
  one folder per year (`2026\`). File names follow §3's naming, with a date in front when the
  file doesn't have one.
- **A note links to each one** the way the vault's OneDrive templates already do:
  `<video src="file:///C:/Users/csdel/OneDrive/Vault%20Media/2026/<file>" controls width="400"></video>`,
  plus a plain link to the same path. Usually that note is a `source` with `media:` set to the
  path. These embeds play on the PCs that sync OneDrive, not on the phone (see the notes plan's
  Later list).
- **Adding a new one takes no linking by hand.** Either way works:
  - Save or upload it to `Vault Media` (from a PC, or the OneDrive app on the phone). The hub
    agent finds files no note links to, writes a `source` note for each and links it. It asks in
    the inbox when it can't tell what the video is or which note it belongs to.
  - Drag it into Obsidian as usual. The agent's sweep moves any video, or anything over 20 MB,
    from `90 Attachments/` to `Vault Media` and rewrites the embed.
- Small images, PDFs and audio stay in `90 Attachments/`.

## 6. Maps (MoC) and the dashboard

Maps are generated, never hand-maintained. A script on PICARD rebuilds them from every note's
front matter after an agent files something, and once a night. They're plain Markdown, so they
work on every device and for agents, with or without plugins.

- **Chris defines the maps** in `99 System/MoC definitions.md`: for each map, its name, which notes
  it holds (by `type`, `kind`, `tags`, `area` or `related`), how it groups them (by year and month,
  by area, by tag) and how it sorts them. Adding a map is adding a definition.
- **Starting set:**
  - **Reflections:** grouped by year, then month, newest first. The current month is open; past
    months and years are folded, using Obsidian's collapsible callouts (`> [!note]- 2025`). Each
    line is the note's link and its one-line summary.
  - **Projects:** grouped by LifeOS Area, each linking its LifeOS item.
  - **Knowledge, Reference and Sources:** one map each, grouped by tag.
  - **Checklists:** the `reference` notes tagged `checklist`, in one list by name.
- **`Home.md` is the dashboard,** also generated:
  - today's and this week's reflections, or a line saying they're missing;
  - a link to every map, with its count;
  - the notes added or changed in the last 7 days;
  - what's waiting in `00 Inbox` and the validator's needs-fixing list.
- **The graph view shows only links between notes.** Its filter hides the maps, `Home.md`, tags,
  `98 Archive` and `99 System`, which link to most notes and would draw everything into one web.
  The filter is `-path:"05 MoC" -path:"Home.md" -path:"98 Archive" -path:"99 System"`, with
  tags off. Chris can switch them back on in the graph's settings.
- **Maps and `Home.md` are wholly generated.** Everything below the front matter is rebuilt each
  time, so they carry no markers; to change a map, change its definition.

## 7. Agents in the vault

- **Modes:** the vault's existing rules hold. APPEND-ONLY is the default; DIRECT-MODIFY logs every
  change in `99 System/change_logs/`. An agent adds new notes and sections; restructuring a note
  Chris wrote needs his review.
- **Formatting:** an agent turns a capture into a note of the right type, with the front matter,
  summary and links above, and keeps the raw text untouched in `transcript`.
- **Organizing and linking is the agents' job,** within this spec: filing, naming, the related
  links and their nightly sweep (§4), the maps and the media sweep.
- **When unsure,** it files nothing and asks through the inbox with two to four options and its
  recommendation.
- **Git:** the vault is a Git repository on PICARD only. One commit per processed item, naming the
  capture. `90 Attachments/` and `.obsidian/workspace*` are not committed. Obsidian Sync doesn't
  sync `.git`.
- **Moves and renames** happen inside Obsidian, or the agent rewrites every link to the note in
  the same commit.
- **Never:** secrets or passwords in a note; the Local REST API's settings in a commit or a reply.

## 8. Enforcement

A validator checks each note against a JSON Schema for its type (`99 System/schemas/`): the
required front matter, the folder matching the type, unique names, tags from the list, the
`checklist` tag only on `reference` notes, a reflection's sections, and no checkboxes. A note
that fails goes to the needs-fixing list on `Home.md` instead of being accepted quietly.

## 9. Freeze rule

The first freeze comes after Chris has seen the vault reorganized to this spec and is happy with
it. After that, no changes for 90 days; then changes happen only at a quarterly review and are
recorded in the changelog below.

## Still open

1. **Plugins.** Proposed keep list: Templater (a template per type, for notes made by hand),
   Dataview (read-only views), the theme and Style Settings. Proposed removals: Tasks, Kanban, Full
   Calendar, Big Calendar, Make.md (it leaves `.mdc`, `.mdb` and `.space` files), the column, color
   and icon plugins. Local REST API stays only if an agent uses it.
2. **The tag list** in §4.
3. **The starting maps** in §6.
4. **Proposed in round 1** (2026-11-12, for Chris to accept or change; the round's tools already
   work this way, and the copy's `99 System/Reorg round 1 report.md` explains each):
   - a reflection's four sections are required from 2026-11-12 on; older logs keep their own
     headings, since their writing stays untouched (§2);
   - `98 Archive` requires only `type` and `created` (§2, §3);
   - projects may have `area`, which the Projects map groups by; `related` may hold an Area's URN
     when there's no Project yet (§2);
   - small attachments are committed in Git, and only videos and files over 20 MB are ignored, so
     Git can restore a note's images (§7);
   - retired hubs and dashboards keep `type: moc` in the archive (§2);
   - the validator also warns about secrets: keys, webhooks, passwords in connection strings (§8);
   - templates in `99 System/templates/` are named `Template - <type>`, so no name clashes with a
     map, and use Obsidian's core `{{date}}` syntax (§3);
   - a source's `author` is a plain name, not a wiki link (§2);
   - change logs live in `99 System/change_logs/`; the vault's `CLAUDE.md` still says
     `_AI_Workspace` and is updated at cutover (§7).
5. **Which LifeOS Area Blue Skies belongs to** (round 1 used Software Engineer Career for its
   reference notes).

## Changelog

- 2026-11-12, v0.1: first draft.
- 2026-11-12, v0.2, Chris's answers: folders by type, with today's folders retired as their notes
  move; zero task objects, most of today's tasks to be deleted; large media in OneDrive's
  `Vault Media` with notes linking to it, and new files linked by the agent (§5); generated maps
  and a generated `Home.md` dashboard (§6); the first freeze only after Chris has seen the
  reorganized vault (§9); checklists recorded as CHECKLIST-1, with an interim rule (§1).
- 2026-11-12, v0.3, Chris's decision on checklists (CHECKLIST-1): read and follow first. The
  interim rule becomes the rule: a checklist is a numbered list in a `reference` note tagged
  `checklist`, runs aren't recorded, and a reflection mentions anything skipped (§1). A
  Checklists map joins the starting set (§6), and the validator fails a `checklist` tag on any
  other type (§8).
- 2026-11-12, v0.4, Chris's call on sources and knowledge: `source` and `knowledge` stay
  separate. A note about one outside work is a source, even in Chris's own words; knowledge is
  his own thinking and may name where an idea came from in `source` (§1, §2). Round 1's notes on
  Tom Bilyeu's courses, the Wisey ADHD course, Ketogenic Expert reset and Procrastination move to
  `50 Sources`.
- 2026-11-12, v0.5, Chris's call on linking (option B): agents link related notes, as each note is
  filed and in a nightly sweep that adds links to older notes, in a Related section between
  markers at the end of the note. Reflections link out, nothing links to a reflection, and there's
  no approval step (§4, §7). The existing notes get links in a backfill pass.
- 2026-11-12, v0.6, after the first backfill (204 links) made the graph a web: a link names one
  of four kinds (part of, continues, answers, same thing), and a shared theme is never enough; at
  most two per note, one direction only, and a series links to its hub (§4). The graph view
  hides the maps, Home, tags, the archive and `99 System` (§6).
- 2026-11-12, v0.7, so Chris doesn't see the `%%` markers while reading: maps and `Home.md` are
  wholly generated and carry none, and a CSS snippet hides the Related section's markers in Live
  Preview (§4, §6).
