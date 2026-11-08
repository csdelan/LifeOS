# Checking a change in the running app

Recipes for agents checking LifeOS changes by hand, beyond the test suites. They're written for
Chris's Windows PC; the Linux parts work anywhere with Docker. Rule one for all of them: **never
write test data to Chris's dev database (`lifeos`) or to staging.** Both are his real data, and
the event log is append-only.

## Pick the lightest check

| Change | Check |
|---|---|
| Web UI only | Mock mode (below). No API or database needed. |
| Web plus API, or a new endpoint | Live check: throwaway database, API on a spare port, faked sign-in. |
| WinForms Pilot | Scratch build against a throwaway database, driven with UI Automation. |
| Staging after a deploy | Ask Chris to look in his own browser (see the last section). |

## Mock mode

`npm --prefix web run dev -- --mode mock --port 5174 --strictPort` (the `web-mock` entry in
`.claude/launch.json`). The mock store lives in memory, so any reload or `page.goto` resets the
seed data: make the change and click through it in one page load.

## Live check: web + API + a throwaway database

1. **Database.** Create one in the dev container:
   `docker exec lifeos-postgres psql -U lifeos -d lifeos -c "CREATE DATABASE lifeos_<x>"`. Migrate
   it with `dotnet run --project src/LifeOs.Cli -- migrate -c "<conn>"`. Build the connection
   strings from the `LocalDevelopmentDefault` constants in `src/LifeOs.Infrastructure`
   (`KernelConnectionString.cs` and `ReaderConnectionString.cs`) with the database name swapped. Seed it through `bsk` with
   `BSK_CONNECTION_STRING` set. Afterwards, `DROP DATABASE lifeos_<x> WITH (FORCE)`.
2. **API.** Run `src/LifeOs.Api` with `--no-launch-profile` on a spare port (5281). Environment
   variables beat user secrets, and the user secrets point at the real Supabase, so override all
   of these:
   - `ALLOWED_EMAILS`, `Auth__Issuer`, `Auth__Audience`;
   - `Auth__JwksUrl` pointing at a fake endpoint that answers `{"keys":[]}`;
   - `Auth__JwtSecret`, then mint an HS256 token with header `kid: "supabase-legacy-hs256"` and
     claims `email`, `app_metadata: {provider: "google", providers: ["google"]}` and
     `amr: [{method: "oauth", timestamp}]`. The API requires a Google sign-in and ignores
     `email_verified` and `user_metadata`. `tests/LifeOs.Tests/SupabaseTokens.cs` shows the shape.
   - `Cors__AllowedOrigins__0` (and `__1`) for the web port;
   - for Oura: every `Oura__*Url` pointing at the fake below, plus fake `Oura__ClientId`,
     `Oura__ClientSecret` and a throwaway `Integrations__TokenKey`;
   - for covers: `Unsplash__ApiUrl` pointing at a fake.

   `dotnet run` leaves `LifeOs.Api.exe` listening after the task stops. Kill it by PID. While it
   runs, its exe in `src/LifeOs.Api/bin` is locked, so stop it before building the solution or
   running the .NET tests, and restart it with `--no-build` after a rebuild.
3. **Fakes.** One small node http server can serve all of them:
   - the empty JWKS at `/auth/v1/.well-known/jwks.json`;
   - Oura: `/oauth/authorize` redirects back with `code` and `state`; `/oauth/token` returns JSON
     with `scope: "extapi:daily extapi:workout"`; `/oauth/revoke` answers;
     `/v2/usercollection/{daily_activity,daily_sleep,daily_readiness,sleep,workout}` serve records
     cloned from `tests/LifeOs.Tests/OuraFixtures/*.json`. **Never real Oura data** (it's against
     Oura's API agreement).
   - Unsplash: any `/photos/{id}` from `UnsplashFixtures/photo.json` with the id swapped.
4. **Web.** Put the values in `web/.env.<mode>.local` (it outranks `.env.local`; `.env.*` is
   gitignored) and run Vite with `--mode <mode>` on a free port (5175 or up). Use a fake
   `VITE_SUPABASE_URL` such as `http://127.0.0.1:5399`. Sign in by writing the session into
   `localStorage["sb-127-auth-token"]` as `{access_token, expires_at, refresh_token, user}`.
   Delete the env file and any temporary launch entry afterwards.
5. **Driving it.** A Playwright script in a scratch folder is more reliable than the Claude browser
   pane. Import `@playwright/test` by absolute path:
   `file:///<repo>/web/node_modules/@playwright/test/index.mjs`.

### Gotchas

- Git Bash rewrites environment values that start with `/`, which breaks a base64 secret. Use hex,
  or regenerate until there's no leading `/`.
- When the Claude browser pane is hidden, `requestAnimationFrame` doesn't run. A Radix sheet then
  stays mounted (`data-state=closed`) with an overlay that eats clicks. That's the pane, not the
  app. Dispatch `animationend` to clear it, and read state with page text or JS rather than
  screenshots.
- A sonner toast under the pointer never hides. Move the mouse off it first.
- While a sheet animates, read several boxes in one `evaluate`.

## Regenerating the web API types

Run the API with `--no-launch-profile`, `ASPNETCORE_URLS=http://127.0.0.1:5281`,
`ASPNETCORE_ENVIRONMENT=Development` and the HS256 auth variables above (`Auth__JwksUrl` empty);
it needs no database. Then
`./scripts/generate-web-client.ps1 -OpenApiUrl http://127.0.0.1:5281/openapi/v1.json`, which writes
`web/src/api/schema.d.ts`. Kill the API by port afterwards. Don't commit an `openapi.json`
snapshot.

## The WinForms Pilot

Chris usually has the Pilot open, often on staging, and its exe in `bin/` is locked while it runs.

- Build to a scratch folder to get around the lock: `dotnet build src/LifeOs.Pilot -o <scratch>`.
- The Pilot's Dev mode is hard-wired to the real dev database. To click through a change, use a
  scratch launcher csproj that compiles the Pilot sources (minus `Program.cs`) and constructs
  `MainForm` with a reader pointed at a throwaway database (made as in the live check).
- Stop only processes you started, and kill rather than close them, so their window placement and
  view state don't overwrite Chris's.
- The computer-use tool can't be granted a non-installed exe. Capture the window with PrintWindow
  and drive it with UI Automation from PowerShell (`Add-Type -AssemblyName UIAutomationClient`).
  For DataGridView rows, post WM_LBUTTONDOWN/UP to the grid's window handle: UIA Invoke highlights
  a row without making it current.

## Staging

Staging has Chris's real data, so check it by reading only. As of 2026-10 the Claude browser
pane's Google account was the wrong one for signing in, so ask Chris to look in his own browser.
To check whether a Fly secret is set without printing it:
`fly ssh console -a lifeos-mz55-staging -C "sh -c 'printenv NAME | wc -c'"` (0 means missing).
