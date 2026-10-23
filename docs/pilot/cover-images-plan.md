# Cover Images Plan — Unsplash photos on identity statements, goals and projects

The design and build order for **cover images**: a photo chosen from Unsplash inside
LifeOS, shown as a banner at the top of a subject's detail and as an image on the cards
where it appears. The requirements are COVER-1 to COVER-3, and the decision behind them is
**D15**, both in [ui-requirements.md](ui-requirements.md). The requirements say *what*; this
doc says *how* and *in what order*, written so an agent with no session context can pick up
any phase.

**Agreed with Chris on 2026-10-23.** He asked whether there are free image libraries like
Notion's, chose Unsplash over Pexels, and asked for both banners and card images (§5).

**Keep it small.** Covers are visual only. They earn no XP, they're not part of any
review, and nothing reads them except the pages that show them. No migration: a cover is
one more key in the subject's `attributes`.

## 0. Read these first

- [ontology.md](../ontology.md): `bsk.subject` is interpreted truth, changed in place;
  `attributes` is its typed jsonb bag.
- `BountyService` (`src/LifeOs.Application/Rewards/BountyService.cs`) and the
  `bounty_json` column in `src/LifeOs.Api/Read/SubjectReader.cs`: the precedent for a
  structured object in `attributes` with its own service, endpoint and read column. Covers
  copy it.
- `OuraHttpClient` and `IntegrationSettings` (`src/LifeOs.Infrastructure/Integrations`) and
  `SupabaseStorageOptions`: an outbound HTTP client behind a port, with settings that fail
  startup when malformed and never print a secret.
- [deploy.md](deploy.md) and `scripts/fly-setup.ps1`: how a server-only key travels from
  `.env` to Fly.
- [production-ui-plan.md](production-ui-plan.md) §5.2: there are no Goal, Project or
  Identity Statement pages. A subject's detail is `SubjectDetail`, opened as a side panel
  (desktop) or sheet (phone) with `?selected=<id>`. That's where the banner goes.
- §7 below: the Unsplash facts this plan relies on, with sources.

## 1. What Unsplash gives us

Checked against Unsplash's API documentation and guidelines on 2026-10-23 (§7).

- **Free, with a key.** Chris registers LifeOS once as an app at
  [unsplash.com/developers](https://unsplash.com/developers) and gets an **Access Key**.
  LifeOS needs only that key; the Secret Key is for signing users in to Unsplash, which
  LifeOS doesn't do.
- **Demo mode is enough.** A new app gets 50 API requests an hour. A search is one request,
  "More photos" is one more, and picking a photo is two (§3.3). **Showing images costs
  nothing:** image loads from `images.unsplash.com` don't count. One person choosing covers
  won't reach the limit, so LifeOS doesn't apply for production.
- **Four rules come with the API:**
  1. **Show the image from Unsplash.** The app must use the image URLs the API returns
     (`photo.urls`), for every use, not only search results. So LifeOS stores a reference,
     never the bytes. The browser loads the picture from `images.unsplash.com`.
  2. **Count the download.** When someone picks a photo (for a header, a post), the app
     calls the photo's `download_location`. It's a counter for the photographer, nothing
     more.
  3. **Credit the photographer and Unsplash,** linking to the photographer's Unsplash
     profile, with `?utm_source=<app name>&utm_medium=referral` on every link back. The
     suggested wording is "Photo by Annie Spratt on Unsplash".
  4. **Keep the key secret.** Calls go through the LifeOS API, never from the browser.

  Also: the app's name can't contain "Unsplash", and it can't copy Unsplash's own
  experience (a wallpaper app, say). LifeOS is neither.
- **Resizing is free.** `urls.raw` takes `w`, `h`, `fit`, `crop`, `q`, `fm`, `auto=format`
  and `dpr`, and the `ixid` already in the URL must stay. LifeOS asks for the width it
  needs (§3.6).
- **Each photo carries** an id, its average `color`, `width` and `height`, an
  `alt_description`, the photographer's name and profile link, and its own page link.
  Search filters by `orientation` (`landscape` here) and returns at most 30 a page.

## 2. What Chris chose (2026-10-23)

- **Library: Unsplash.** Recommended was Pexels plus uploading his own photos, because
  Pexels lets the picture be copied into LifeOS's own storage. Chris picked Unsplash. The
  cost is rule 1: the picture stays on Unsplash's servers, so the security policy lets in
  `images.unsplash.com`, and a photo the photographer later deletes from Unsplash stops
  showing (§3.6 shows its colour instead).
- **Both places:** a **banner** on the subject's own view, and **card images** wherever the
  subject shows as a card.
- **Which subjects:** identity statements (`Value`), goals and projects.

## 3. Design

### 3.1 Storage: `attributes.cover` (D15)

A cover is one object under the subject's `cover` attribute:

```json
{
  "source": "unsplash",
  "photo_id": "Dwu85P9SOIk",
  "url": "https://images.unsplash.com/photo-1417325384643-aac51acc9e5d?ixid=…",
  "color": "#6c7a89",
  "width": 6000,
  "height": 4000,
  "alt": "mountain range under a cloudy sky",
  "photographer": "Annie Spratt",
  "photographer_url": "https://unsplash.com/@anniespratt",
  "photo_url": "https://unsplash.com/photos/Dwu85P9SOIk",
  "position_y": 40,
  "set_at": "2026-10-23T18:04:00Z"
}
```

- **`url` is `urls.raw`,** kept whole so `ixid` stays. Pages add size parameters to it.
- **`position_y`** (0–100, default 50) is where the banner is centred vertically, as CSS
  `object-position: 50% <n>%`. Repositioning changes only this.
- **Only the three types.** `Value`, `Goal` and `Project` may have a cover. Anything else is
  refused in words. Areas are an easy later addition (§6).
- **Changed in place, no history.** Like a rename, a cover change rewrites the attribute and
  writes no event. Changing the cover forgets the old one. It's presentation, not a record
  of what happened.
- **One writer.** `CoverService` alone writes `cover`. The generic setter
  (`AttributeService`, behind `bsk set` and `POST /api/attributes`) refuses the key with
  "Cover is set with `bsk cover`", the way it refuses `title`. The web edit form already
  can't overwrite it: `parseAttrBag` and `pickApplicableAttrs` only keep string attributes
  listed per type.
- **The server looks the photo up itself.** The browser sends only the photo id. The API
  fetches the photo from Unsplash and stores what Unsplash says, so a stored `url` is always
  `https://images.unsplash.com/…` and the photographer links are always `unsplash.com`.
  `Cover.Validate` checks both hosts, so a hand-edited row can't put another site's image
  on a page.
- **Room for more sources.** `source` is `unsplash` today. An uploaded photo would be
  `source: "upload"` with an `artifact_id` instead of `url`, without changing the rest (§6).

### 3.2 The Unsplash client and its settings

- **Settings** (`UnsplashOptions`, Infrastructure/Integrations), following
  `SupabaseStorageOptions`:
  - `Unsplash:AccessKey`: the Access Key. Without it, covers are off and the picker says so.
  - `Unsplash:AppName`: the `utm_source` on credit links; defaults to `lifeos`. It should
    match the app name Chris registers.
  - `Unsplash:ApiUrl`: defaults to `https://api.unsplash.com`. Only tests and the local
    fake change it.
  - A key with spaces, or an ApiUrl that isn't http(s), fails startup. `ToString` never
    prints the key.
- **Port** `IUnsplashClient` (Application/Abstractions/Ports.cs):
  - `SearchAsync(query, page, perPage)`: `GET /search/photos?query=…&page=…&per_page=24&orientation=landscape&content_filter=high`.
  - `GetPhotoAsync(photoId)`: `GET /photos/{id}`.
  - `TrackDownloadAsync(downloadLocation)`: `GET` the photo's `links.download_location`.
- **Adapter** `UnsplashHttpClient` sends `Authorization: Client-ID <key>`,
  `Accept-Version: v1` and `User-Agent: LifeOS/1.0`.
  - Results whose `urls.raw` isn't on `images.unsplash.com` are dropped (Unsplash+ photos,
    say), so nothing can be chosen that LifeOS won't show.
  - Errors become clear messages: 401 "The Unsplash key isn't valid", 403 with
    `X-Ratelimit-Remaining: 0` "Unsplash's hourly limit is used up. Try again in a few
    minutes", 404 "That photo is no longer on Unsplash", anything else "Unsplash didn't
    answer".
  - `TrackDownloadAsync` must only ever call `api.unsplash.com`: a `download_location` on
    any other host is refused.

### 3.3 Picking, moving and removing a cover

`CoverService` (Application/Covers):

- **`SetFromUnsplashAsync(subject, photoId, positionY?)`:**
  1. Resolve the subject; refuse a type other than Value, Goal or Project.
  2. `GetPhotoAsync` (one request) and build the `Cover`; `Validate` it.
  3. Write `attributes.cover` through `ISubjectRepository.UpdateAttributesAsync`.
  4. `TrackDownloadAsync` (one request). It runs after the write and is best effort: if
     Unsplash doesn't answer, the cover is still set and the failure is logged. Picking the
     same photo again counts again; that's what a download is.
- **`MoveAsync(subject, positionY)`:** changes `position_y` only. No Unsplash call, no
  download counted. Refused when the subject has no cover.
- **`ClearAsync(subject)`:** removes the key. Nothing to tell Unsplash.

### 3.4 API and CLI

All writes are `POST`s under `/api` behind the allow-listed user policy, as every other
write is.

| Endpoint | Body / query | Answers |
|---|---|---|
| `GET /api/covers/search` | `q` (1–100 characters), `page` (1–50) | `{ configured, total, totalPages, photos: [{ id, thumbUrl, color, alt, width, height, photographer, photographerLink }] }`. Without a key: `configured: false` and no photos, so the picker can say so. |
| `POST /api/cover` | `{ subject, photoId, positionY? }` | the subject's new `cover` |
| `POST /api/cover/position` | `{ subject, positionY }` | the subject's `cover` |
| `POST /api/cover/clear` | `{ subject }` | 204 |

- `thumbUrl` is `urls.raw` plus `&w=400&q=70&auto=format`, so the picker's tiles load
  small.
- Unsplash's errors arrive as 4xx/5xx problem details carrying §3.2's words.
- **Reads:** `SubjectListItem` and `SubjectDetail` gain `cover` (`CoverView?`), read from
  `s.attributes->'cover'` the way `bounty_json` is. `CoverView` carries the stored fields
  plus `photographerLink` and `unsplashLink`, with the UTM parameters already on, so the web
  and the CLI credit the same way. A malformed `cover` reads as `null`.
- **CLI:** `bsk cover search <words>`, `bsk cover set <subject> <photo-id> [--position N]`,
  `bsk cover move <subject> <N>`, `bsk cover clear <subject>`, `bsk cover show <subject>`.
  `search` prints each photo's id, photographer and description.

### 3.5 The security policy and the key on Fly

- **`img-src`** in `SecurityHeaders.cs` becomes `'self' data: blob: https://images.unsplash.com`.
  Nothing else changes: the browser never calls `api.unsplash.com`, so `connect-src` stays
  as it is. Development sends no policy, so local dev needs nothing.
- **Referrer:** the existing `strict-origin-when-cross-origin` means image loads tell
  Unsplash only the site's origin, never a page path or a subject id.
- **`fly-setup.ps1`** imports `Unsplash__AccessKey` from `.env` when it's there, and prints
  "Covers ON" or "Covers OFF: no Unsplash__AccessKey". `.env.example` lists it.

### 3.6 Showing a cover: the shared image

`components/cover/cover-image.tsx` and `lib/cover.ts`:

- **Size by width.** `coverSrc(cover, width)` adds `w=<width>&q=75&auto=format&fit=max` to
  the stored URL and a `srcset` for 1× and 2×. URLs not on `images.unsplash.com` (the mock's
  bundled pictures) are used as they are.
- **Cropped by the page, not by Unsplash:** `object-fit: cover` with
  `object-position: 50% <position_y>%`. That's what makes Reposition cheap.
- **No flash and no hole.** The box's background is the photo's `color`, so it shows that
  colour while loading. If the image fails (the photo was deleted from Unsplash, or the
  phone is offline), it stays that colour, and the banner's menu offers Change cover.
- **Decorative.** The image is `alt=""`: the subject's title right beside it says what it's
  about. The credit is real text with real links.
- **Lazy below the fold:** cards use `loading="lazy"`; the banner loads eagerly.

### 3.7 The banner (COVER-2)

On `SubjectDetail`, the detail panel every page opens (`components/detail/subject-detail.tsx`,
header at lines 278–369, hosted by `PeekPanel` in `components/shell/panels.tsx`):

- **With a cover:** a full-bleed image across the top of the panel, above the type badge
  and title. 128 px tall on a phone, 144 px in the desktop panel (448 px wide by default, up
  to 800). Asked for at 900 px wide (1,800 at 2×).
- **Credit:** "Photo by *Annie Spratt* on *Unsplash*" in the bottom-left corner, on a dark
  pill (`bg-black/60`, white text) so it meets 4.5:1 over any photo. Both names are links,
  opening in a new tab.
- **Cover menu:** a small "Cover" button in the top-right corner, always visible (touch
  has no hover), opening Change cover, Reposition and Remove cover.
- **Reposition** works like Notion's: the banner shows "Drag to reposition" with Save and
  Cancel. Dragging up or down moves `position_y`, and so do the Up and Down arrow keys
  (5% a press) while the banner has focus. Save sends `POST /api/cover/position`.
- **Without a cover:** no banner and no placeholder. The header's buttons gain **Add
  cover** (image icon) for identity statements, goals and projects, outside Edit mode.
- **The picker** (`components/cover/cover-picker.tsx`), a `Dialog` titled "Choose a cover":
  - A search box filled in with the subject's title, and a Search button (Enter works).
  - "Photos from Unsplash" at the top, linked with the UTM parameters.
  - A grid of landscape tiles (2 across on a phone, 3 on desktop), each with its
    photographer's name under it. "More photos" loads the next page.
  - Tapping a tile sets the cover, closes the dialog and shows "Cover set".
  - Words for every miss: "No photos for 'xyz'. Try fewer or simpler words." / "Unsplash
    isn't set up on this server." / §3.2's errors.

### 3.8 Card images (COVER-3)

Covers show where these subjects appear as cards. A subject without a cover looks exactly
as it does today, so the two can sit side by side.

| Where | What changes |
|---|---|
| **Vision** (`/vision`), identity statement cards | The image across the top of each card (112 px on a phone, 144 px from `md`), the card's text below. These cards are large, so the credit pill shows in the image's corner. |
| **Vision**, long-term goals | A 40 px rounded thumbnail before each title. |
| **Today** (`/`), the primary focus card | A strip across the top of the card (96 px). |
| **Today**, Active goals and Active projects | `ListRow` gains an optional leading thumbnail (36 px). |
| **Map, graph view** | A node with a cover shows it in its art slot (`.lifeos-node__art`, 280 × 84) in place of the type's drawn backdrop, under a scrim so the title keeps 4.5:1. |

- **Not changed:** the Map outline (kept dense), the ⌘K palette, the Relationships tab's
  rows, the Pilot desktop app.
- **Credit on thumbnails.** A 36–40 px thumbnail has no room for a line. Its credit is in
  the image's tooltip, and in full on the banner one tap away and on the Vision card. The
  large images (banner, Vision cards) always show it.
- **The Vision page's line** "Not a mood board — alignment." comes out in C4: photos on the
  cards make it read oddly. The page is still ordered by alignment.

### 3.9 The mock and the tests

- **The mock never touches the network.** About eight small landscape scenes are drawn as
  SVG files in `web/src/lib/mock/covers/`, each with a made-up photographer. The mock seeds
  covers on `value-steward`, `goal-lifeos` and `proj-web`; mock search returns the scenes
  that match the words (each has a few keywords), or all of them.
- **Unsplash fixtures** for the C# tests live in `tests/LifeOs.Tests/UnsplashFixtures/`
  (`search.json`, `photo.json`), shaped like the documentation's examples. No test calls
  Unsplash.
- **Live check:** the usual recipe (a throwaway database, the API on a spare port, a
  minted test sign-in), with either a small node fake for `Unsplash:ApiUrl` or Chris's real
  key. Unsplash's photos aren't private data, so the real key is fine for checking; the
  Oura rule about real data doesn't apply here.

## 4. Phases

Each phase is one commit:

- kernel phases: `./test.ps1` and a clean strict build
- web phases go mock-first, then live, with `npm run gen:api` after API changes, plus
  `npm run test:e2e` and `npm run a11y` (both themes, and on a phone)
- each phase updates the Build fields of the COVER requirements it advances

| # | Phase | Delivers | Depends on |
|---|---|---|---|
| C0 | Requirements + plan | COVER-1 to COVER-3, D15, the backlog line, this doc. | — |
| C1 | Kernel | `Cover` (Domain: JSON in and out, `Validate`, credit links), `UnsplashOptions`, `IUnsplashClient` + `UnsplashHttpClient`, `CoverService`, `AttributeService` refusing `cover`, `bsk cover`. No migration. Tests: settings fail startup when malformed and never print the key; the client's headers, paging and parsing on the fixtures; off-host results dropped; 401, rate limit and 404 in words; a `download_location` off `api.unsplash.com` refused; set on each of the three types and refused on a Task; the download counted on set and never on move; move without a cover refused; clear removes the key; `bsk set cover=…` refused; other attribute edits and renames leave the cover alone; a malformed stored cover reads as none. | — |
| C2 | API | §3.4's endpoints, `cover` on `SubjectListItem` and `SubjectDetail`, the `img-src` change, `fly-setup.ps1` and `.env.example`, `gen:api`. Tests: search without a key answers `configured: false`; a blank or 101-character `q` is a 400; set, then the list item and detail carry the cover with its UTM links; move; clear; an unknown photo is a 404 in words; the production policy allows `images.unsplash.com` and Development sends none. | C1 |
| C3 | Picker and banner | §3.6 and §3.7: `lib/cover.ts`, `CoverImage`, the banner with its credit, menu and Reposition, Add cover, the picker; the mock's covers, search and writes; the write hooks refreshing the subject, lists, dashboard and map. Vitest; e2e (add a cover to a goal from search, see it and its credit, reposition with the keyboard, remove it); a11y with the banner and the picker open, both themes and on a phone. | C2 (mock-first can start after C1) |
| C4 | Card images | §3.8: Vision cards and goal thumbnails, Today's primary focus strip and `ListRow` thumbnails, the Map graph's art slot; the Vision line removed. Vitest, e2e, a11y on `/vision`, `/` and `/map?view=graph`, both themes and on a phone. | C3 |
| C5 | Live on staging | Chris registers the app on Unsplash and puts `Unsplash__AccessKey` in `.env`; `fly-setup.ps1` stages it; Chris picks real covers on staging, on his phone and on desktop. | C4 (registering can happen any time) |

- **C1 and C2 are kernel and API only.** From C1, `bsk cover search` and `bsk cover set`
  work end to end, so a cover can be set before the page exists.
- **No migrations.** The next free number stays 0041.

## 5. How this design was settled

**Chris's brief (2026-10-23)**, in his words:

- "I would like to add images to long standing subjects such as identity statements, goals,
  and even projects, for the visual enhancement it provides. My first question is whether
  there are standard libraries of free images that I can choose from similar to how Notion
  provides image libraries. Or do I need to find the image myself?"

**The answer and the recommendation (2026-10-23):** free libraries with search APIs exist:
Unsplash, Pexels and Pixabay, each free with a key. Notion's cover picker has an Unsplash
tab. The recommendation was Pexels search plus uploading his own, because LifeOS's security
policy only shows images it serves itself and food photos already go to Supabase Storage,
so a Pexels photo could be copied in. Unsplash was offered as the option with the most
"inspirational" look, at the cost of the picture staying on Unsplash's servers.

**Chris's choice:** "Let's do unsplash, and both banners and card images" (2026-10-23).

**Settled in planning, not asked (one option was clearly better):**

- **No upload for now.** Chris chose Unsplash; uploading stays possible later without
  changing the cover's shape (§6).
- **The banner goes at the top of the detail panel.** There are no Goal, Project or Identity
  Statement pages (production-ui-plan §5.2): every list opens the subject in the side panel
  or phone sheet, so that's the subject's own view. A full page per subject would be a
  bigger change to navigation and is listed under §6.
- **Credit** shows in full on the banner and on Vision cards; thumbnails carry it in a
  tooltip (§3.8).
- **No history** for cover changes, as for renames.

## 6. Later

- **Upload your own photo** as a second picker tab, through the Food Log's resize and
  storage path: `source: "upload"` with an `artifact_id`.
- **Areas** get covers by adding `Area` to the allowed types, plus their cards on `/areas`.
- **A full page per subject**, Notion-style, with the banner across the page.
- **Focal-point crops** from Unsplash (`crop=focalpoint`) if CSS positioning ever wastes
  too many bytes. Not officially supported by Unsplash today.

## 7. Unsplash API notes

Read 2026-10-23.

- [Unsplash API documentation](https://unsplash.com/documentation): `Client-ID`
  authorization and `Accept-Version: v1`; demo mode is 50 requests an hour and only
  `api.unsplash.com` requests count; `GET /search/photos` (`query`, `page`, `per_page` up to
  30, `orientation`, `content_filter`); `GET /photos/:id`; `GET /photos/:id/download` is a
  counter only; resizing parameters on `urls.raw`, keeping `ixid`.
- [Unsplash API guidelines](https://help.unsplash.com/en/articles/2511245-unsplash-api-guidelines):
  hotlink `photo.urls` for every use; call `download_location` when a photo is used
  "similar to a download", such as a header; credit Unsplash and the photographer with a
  profile link and UTM parameters; keep the keys confidential, proxying if needed; no
  "Unsplash" in the app's name; don't replicate Unsplash.
- [Guideline: attribution](https://help.unsplash.com/articles/2511315-guideline-attribution):
  "Photo by Annie Spratt on Unsplash", both names linked, UTM on both links.

## Status

- [x] C0 — Requirements (COVER-1 to COVER-3, D15) + this plan
- [x] C1 — Kernel: the cover attribute, the Unsplash client, `bsk cover`
- [x] C2 — API, the security policy and the key on Fly
- [x] C3 — Picker and banner
- [ ] C4 — Card images
- [ ] C5 — Live on staging

### As built

**C1 (2026-10-23).** As planned, plus:

- **Where things live.** `Cover` is in `src/LifeOs.Domain/Covers.cs`. `IUnsplashClient`,
  `UnsplashPhoto` and `UnsplashSearchPage` are in `Application/Abstractions/Ports.cs`, and
  `CoverService` is in `Application/Covers`. `UnsplashOptions` and `UnsplashHttpClient` are
  in `Infrastructure/Integrations`.
- **The settings ride on `IntegrationSettings`** (a third, optional member), so the API host
  and the CLI read `Unsplash__AccessKey` the same way they read Oura's settings.
- **One error type, five kinds.** `UnsplashException.Failure` is `NotSetUp`, `BadKey`,
  `RateLimited`, `NotFound` or `Unavailable`. C2 maps them: `NotSetUp` to 409, `NotFound`
  to 404, and the rest to 502.
- **The hourly limit** is a 429, or a 403 with `X-Ratelimit-Remaining: 0` or "Rate Limit"
  in the body, so any of the ways Unsplash might signal it reads the same.
- **The key only goes to the API's host.** A `download_location` with a different scheme,
  host or port from `Unsplash:ApiUrl` is never followed.
- **The client drops what can't be shown or credited.** A photo is left out if it lacks an
  id, an image on `images.unsplash.com`, a size, a photographer, or credit links on
  `unsplash.com`. Names and descriptions are read with single spaces.
- **Setting a cover** checks the id and position, then the subject's type, then looks the
  photo up, writes the cover, and counts the download. If the download count fails, the
  cover stays set: `CoverChange.DownloadProblem` says why the count failed, and `bsk` prints
  it to stderr.
- **Clearing works on any subject,** so a cover hand-edited into the wrong shape can always
  be removed. It reads as none and can't be moved.
- **`bsk set` refuses `cover`**, both setting it and blanking it, and points at `bsk cover`.
  The Pilot desktop app only sends its own form fields, so it's unaffected.
- **Tests:**
  - `CoverTests`: round trips, off-host and hand-edited covers, positions, referral links,
    allowed types.
  - `UnsplashSettingsTests`.
  - `UnsplashHttpClientTests`, on fixtures in `tests/LifeOs.Tests/UnsplashFixtures`
    (`search.json` includes an Unsplash+ photo and one with no photographer).
  - `CoverServiceTests`: Postgres, with a fake Unsplash.
- **Live-checked** with a throwaway database and a fake Unsplash in node:
  - `bsk cover search`, `set --position`, `show`, `move` and `--json show` all worked.
  - `bsk set cover=…` was refused, and a missing photo answered in words.
  - The fake saw `Client-ID` and `Accept-Version: v1` on every request, one download count
    on set, and no call on move.

**C2 (2026-10-23).** As planned, except where noted:

- **Endpoints** are in `src/LifeOs.Api/Http/CoverEndpoints.cs` (tag "Covers"), and their
  shapes are in `Read/CoverModels.cs`. A subject can be named by id, urn or title, as in
  every other write.
- **Two answers differ from §3.4:**
  - `POST /api/cover` and `/api/cover/position` answer `{ subject, cover, downloadCounted,
    downloadProblem }`, not the bare cover. The page can then say when the download count
    failed, though the cover is set.
  - `POST /api/cover/clear` answers 200 with `{ subject, removed }`, not 204. That matches
    the API's other writes (none answers 204), and `removed` says whether there was a cover.
- **Search** answers `query` (the words as searched, spaces squeezed) and `page` too. It
  checks the words even without a key, so a blank search is a 400 either way. `thumbUrl`
  adds `&fit=max`, so a small photo is never stretched.
- **Errors:** `NotSetUp` is 409, `NotFound` is 404, and Unsplash's other failures are 502,
  each with C1's words. Asking to move a cover that isn't there, or for a type that can't
  have one, is a 400.
- **The cover on the reads.** `cover` (`CoverRead`) is on `SubjectListItem` (the list and
  `/list-item`) and `SubjectDetail`. It's always null for a type other than Value, Goal or
  Project, and for a cover in the wrong shape.
  - The list reads give the cover its own Dapper split column, `cover_json`, after
    `bounty_json`. Dapper hands back a null object for a split whose first column is null,
    so a cover sharing the bounty's split would vanish on every subject without a bounty
    of its own.
  - The credit links use `Unsplash__AppName`, the same as the CLI.
- **Security policy:** `img-src 'self' data: blob: https://images.unsplash.com`.
  `connect-src` is unchanged. A test checks both, and that Development sends no policy.
- **`fly-setup.ps1`** imports `Unsplash__AccessKey`, and `Unsplash__AppName` when it's set.
  It refuses a key with spaces, an app name without a key, or a malformed app name. It
  prints "Covers ON" or "Covers OFF". `.env.example` lists both settings.
- **`gen:api`** regenerated `web/src/api/schema.d.ts`; it only adds the cover shapes and
  routes.
- **Tests:**
  - `CoverEndpointTests` (Postgres, with `FakeUnsplashClient`, now shared with
    `CoverServiceTests`). It covers:
    - search without a key, and its refusals
    - search paging and thumbnails
    - set by id and by title, then the list item and detail, with and without a bounty
    - move and clear
    - refusals in words, including a photo that's gone (404) and the hourly limit (502)
    - a malformed stored cover reading as none
  - `ApiHostTests.Csp_lets_cover_images_load_from_unsplash_and_nothing_more`.

**C3 (2026-10-23).** As planned, except where noted:

- **Where things live.** The helpers are in `web/src/lib/cover.ts`. The components are in
  `web/src/components/cover/`: `cover-image.tsx`, `cover-banner.tsx` and
  `cover-picker.tsx`. The mock's scenes are in `web/src/lib/mock/covers.ts` and
  `covers/*.svg`. The client types (`CoverRead`, `CoverPhoto`, `CoverSearchRead`,
  `CoverResult`, `CoverCleared`) are in `production-ui-types.ts`, and both data seams read
  covers through `normalizeCover`, so a cover with no image or credit reads as none.
- **Add cover sits beside the type badge,** as a small ghost button, not in the header's
  button row. In the 448 px panel, a fourth button there wrapped the title onto two lines.
- **Reposition:**
  - Choosing it from the menu puts the focus on the banner. The banner is then a vertical
    slider (0–100), and its value is in words, e.g. "Showing the photo 40% of the way
    down".
  - Keys: Up and Down move 5, Page Up and Page Down move 20, and Home and End go to the
    top and bottom.
  - The photo moves the way the arrow points: Up raises `position_y`, which shows more of
    the photo's lower part. A drag follows the pointer, scaled by how much of the photo is
    hidden above and below.
  - Escape isn't bound. On a phone it would close the sheet; Cancel is there instead.
  - Save writes only if the position changed.
- **The picker:**
  - The Search button stays enabled while a search loads, because a disabled submit
    button swallows Enter.
  - Results are kept for 30 minutes and never retried, since Unsplash's demo limit is 50
    requests an hour. Reopening the picker on the same words costs nothing.
  - An error shows in words in place of the grid. An error loading more photos shows
    under the grid and keeps the photos already there.
  - If the download count failed, a warning toast says so. The cover is still set.
- **Keyed state.** The banner is keyed by subject and the picker by each opening. Moving to
  another subject never inherits a reposition in progress, and every opening starts again
  from the title.
- **The write hooks** refresh the subject, its list item, the subject lists, the dashboard
  and the map after a set, move or clear.
- **The mock:**
  - It has eight scenes, served six to a page, so More photos shows.
  - A search word of two or more letters matches a scene's keyword by prefix either way.
    With no match, the search gives every scene.
  - It refuses in the server's words.
  - The made-up photographers' links go to unsplash.com with the referral parameters.
  - `applyAttrs` refuses `cover`, as `bsk set` does.
- **Tests:**
  - Vitest:
    - `cover.test.ts`: sizing, positions, keys, drag, words, paging and reading the API.
    - `mock/covers.test.ts`: search, the seeds, set, move and clear, the refusals, and the
      cover kept through other edits.
    - A "SubjectDetail covers" block in `subject-detail.test.tsx`: the credit links; adding
      a cover from a search; repositioning with the keyboard, saving and removing; and no
      Add cover on a Task.
  - An e2e golden path: add a cover to a goal from search, see its credit, reposition it
    with the keyboard, and remove it.
  - a11y scans of the banner and the picker in both themes, and of the banner,
    Reposition and the picker on a 375 px phone.
- **a11y scans now wait for transitions to finish.** `index.html` starts dark, and
  next-themes switches to light only after the page mounts; its own early script never
  runs in a client-only app. Under reduced motion every element still takes a 0.01 ms
  transition, and the colour reaches each level of the page a frame after its parent.
  For a moment, deep text sits between the two themes, and axe reported that moment as
  1.14:1 contrast.
  - The cover light-theme scan failed this way every time.
  - The Character Reports light-theme scan failed this way once.
  - `settled()` in `a11y.spec.ts` now waits until no transition is running, before every
    scan.
- **Live-checked** with a throwaway database, the API with a fake Unsplash, and a test
  sign-in:
  - The picker searched the goal's title through the server.
  - Picking a photo set the cover with one download count, and showed the credit with both
    links.
  - Moving it with the keyboard saved 40. It kept 40 across a reload and on the list read.
  - Removing it brought Add cover back. A Task had no Add cover.
