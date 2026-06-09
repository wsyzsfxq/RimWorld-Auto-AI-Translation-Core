# AI Translation Network Cloud Worker

This folder is a paste-friendly Cloudflare Worker plan for the cloud backend.

You can still copy code into the Cloudflare dashboard manually. The goal is to stop keeping the Worker only in chat/history, and to make D1/R2/auth changes easier to review.

## Files

- `schema.sql`: Full D1 schema for a brand-new database.
- `worker-v2.js`: Worker template with dynamic privilege-code authorization.
- `manual-deploy.md`: Step-by-step instructions for dashboard/manual deployment.
- `admin-api-tests.md`: HTTP test examples for privilege-code administration.
- `wrangler.toml.example`: Starter config for file-based Wrangler deployment.
- `d1-tools/inspect-existing-registry.sql`: Read-only checks for an existing production D1 database.
- `d1-tools/preview-rwmod-seed-from-registry.sql`: Read-only preview for seeding RWMod pages from `TranslationRegistry`.
- `d1-tools/seed-rwmod-from-registry.sql`: Safe RWMod catalog upsert from `TranslationRegistry`.
- `migrations/0001_add_rbac_tables.sql`: Safe RBAC/audit table migration for an existing D1 database.
- `migrations/0002_add_feedback_tables.sql`: Safe player feedback/bug-report migration for an existing D1 database.
- `migrations/0004_add_rwmod_catalog_tables.sql`: Safe RWMod catalog table migration for an existing D1 database.

## Existing Production D1 Warning

If your D1 database already has workshop/player translations, do not use
`schema.sql` as a production migration. `CREATE TABLE IF NOT EXISTS` will not
delete old rows, but it also will not add missing columns to an existing table.

Use this order for an existing database:

1. Run `d1-tools/inspect-existing-registry.sql` and save the output.
2. Run `migrations/0001_add_rbac_tables.sql` to add privilege-code/audit tables.
3. Run `migrations/0002_add_feedback_tables.sql` to add player feedback tables.
4. Run `migrations/0003_add_applications_and_attachments.sql` to add group-key applications and attachment metadata.
5. Run `migrations/0004_add_rwmod_catalog_tables.sql` to add RWMod catalog tables.
6. Run `d1-tools/preview-rwmod-seed-from-registry.sql` and inspect the seed preview.
7. Optionally run `d1-tools/seed-rwmod-from-registry.sql` to upsert starter RWMod catalog rows.
8. Deploy `worker-v2.js`.
9. Only after checking `PRAGMA table_info`, optionally add missing registry columns from `d1-tools/optional-registry-columns.sql`.

`worker-v2.js` starts in conservative mode:

- Upload history cleanup is disabled by default.
- Scheduled hard purge is disabled by default.
- Event logs and download counts are optional; missing tables/columns will not block core upload/download.

## Brand-New D1 Deployment Order

1. Create a new D1 database.
2. Run `schema.sql`.
3. Add Worker secrets:
   - `MASTER_SECRET`
   - `TOKEN_HASH_PEPPER`
   - `TURNSTILE_SECRET_KEY` for RWMod anonymous reports
   - `LEGACY_OFFICIAL_SECRETS` if you need old fixed official codes to keep working during migration
4. Bind D1 as `DB`.
5. Bind R2 as `BUCKET`.
6. Copy `worker-v2.js` into the Worker editor.
7. Test:
   - `GET /api/v1/health`
   - `GET /api/v1/registry`
   - `POST /api/v1/upload`
   - admin privilege-code endpoints.

## Current Compatibility

The Worker keeps the mod-facing endpoints:

- `GET /api/v1/registry`
- `GET /api/v1/download/{packageId}/{language}`
- `POST /api/v1/upload`
- `DELETE /api/v1/delete/{packageId}/{language}?recordId=...`

The mod does not need to change immediately.

`GET /api/v1/registry` also returns public contributor hints for the website:

- `ContributorName`
- `ContributorKind`: `player` or `group`

These are derived from existing registry fields, so no extra D1 migration is
needed for the leaderboard feature.

## RWMod Catalog API

After running `migrations/0004_add_rwmod_catalog_tables.sql`, the Worker also
serves the Phase 1 RWMod catalog contract:

- `GET /api/v1/rwmod/mods?q=&language=&gameVersion=&limit=`
- `GET /api/v1/rwmod/mods/{packageId}`
- `GET /api/v1/rwmod/mods/{packageId}/localization`
- `GET /api/v1/rwmod/mods/{packageId}/compatibility`
- `GET /api/v1/rwmod/mods/{packageId}/performance`
- `POST /api/v1/rwmod/reports`

The read endpoints prefer `RWMod*` catalog tables, then safely fall back to
`TranslationRegistry` so early MOD pages can exist before full seeding. Missing
compatibility or performance data is returned as `unknown`.

For local-only validation without touching Cloudflare, run the RWMod smoke test:

```powershell
npm run rwmod:local-smoke
```

This uses an in-memory SQLite database as a D1-like binding, applies the
RWMod migration, seeds mock catalog rows, imports `worker-v2.js`, and verifies
the RWMod read endpoints. It also checks that the 9 `RWMod*` tables, 24
`idx_rwmod*` indexes, and `idx_rwmod_mods_workshop` on `PrimaryWorkshopId`
exist locally. It does not run Wrangler, deploy the Worker, or touch remote D1.

To run the Phase 3 frontend against the real local Worker handler instead of
the mock preview API:

```powershell
npm run rwmod:frontier-worker-preview
```

This starts `web/rwmod-frontier/preview-server.mjs --worker`. The server still
serves static frontend files locally, but `/api/*` requests are routed through
`worker-v2.js` with an in-memory SQLite D1-like binding loaded from
`schema.sql`. The local preview sets `RWMOD_LOCAL_PREVIEW=1`, which only allows
the preview Turnstile token used by the frontend; production Workers must not
set that variable.

To run the same Worker-backed preview path as an automated smoke test:

```powershell
npm run rwmod:frontier-worker-smoke
```

The smoke test chooses a free localhost port, starts the preview server,
verifies catalog search, mod detail, `TranslationRegistry` fallback, heavy and
unknown states, and a local RWMod report insert, then stops the server.

To create the first RWMod catalog pages from existing cloud translation records,
run the preview first:

```powershell
npm run d1:rwmod:seed-preview
```

If the preview looks correct, run the seed:

```powershell
npm run d1:rwmod:seed
```

The seed only upserts `RWModMods` and `RWModLocalizationStatus`. It keeps
compatibility and performance as `unknown`, marks cloud translation rows as
`partial`, and does not update `TranslationRegistry`, `FeedbackReports`,
privilege-code tables, or R2 files.

`POST /api/v1/rwmod/reports` requires Cloudflare Turnstile. Add this Worker
secret before enabling the public report form:

```powershell
npx wrangler secret put TURNSTILE_SECRET_KEY
```

RWMod reports are stored in `FeedbackReports` for moderation first. They do not
become high-confidence public compatibility or performance conclusions until a
reviewed row is promoted into the RWMod catalog tables.

## Admin API

Dynamic privilege-code management supports:

- create codes
- list active or inactive codes
- inspect one code
- pause/resume codes
- revoke leaked codes
- update owner/group/notes/scopes
- inspect per-code events and recent usage

See `admin-api-tests.md` for ready-to-paste HTTP tests.

## Feedback / Bug Reports

The Worker also supports the player-facing report board used by `web/player-portal/feedback/`.

Public routes:

- `GET /api/v1/feedback`
- `GET /api/v1/feedback/{feedbackId}`
- `POST /api/v1/feedback`
- `POST /api/v1/feedback/{feedbackId}/vote`

Reviewer/admin routes:

- `GET /api/v1/admin/feedback`
- `GET /api/v1/admin/feedback/{feedbackId}`
- `PATCH /api/v1/admin/feedback/{feedbackId}`
- `GET /api/v1/admin/feedback/{feedbackId}/events`

Reviewer privilege codes should include:

```json
["record:verify", "audit:read", "feedback:moderate", "feedback:read_private"]
```

## RWMod Phase 4 Launch Readiness

Before attaching `rwmod.net`, running remote D1 migrations, or enabling public
RWMod reports, review `../../docs/RWMod_Phase4_Launch_Readiness.md`.

The recommended first public topology is now a unified full-stack Worker:

- `rwmod.net`: this Worker serving both `web/rwmod-frontier` static assets and
  `/api/v1/*` routes.
- `api.rwmod.net`: not required for the first launch.

Production must configure Workers Assets, bind D1 as `DB`, bind R2 as
`BUCKET`, set `TURNSTILE_SECRET_KEY` before public report submission, and avoid
`RWMOD_LOCAL_PREVIEW=1`.

Workers Assets configuration:

```toml
[assets]
directory = "../../web/rwmod-frontier"
binding = "ASSETS"
not_found_handling = "single-page-application"
run_worker_first = ["/api/*"]
```

This keeps the frontend and API same-origin. Static CSS/JS/HTML assets are
served by Workers Assets, while `/api/*` enters `worker-v2.js`.
Static asset requests are free and unlimited; `/api/*` requests still invoke
the Worker script and follow Workers pricing.

R2 should be split from the beginning:

- `rimworld-translation-hub`: translation ZIP/files, bound as `BUCKET`.
- `rwmod-assets`: RWMod encyclopedia public images/media, reserved as
  `RWMOD_ASSETS` when asset ingestion begins.
- `rwmod-report-uploads`: future private report attachments, not needed for
  Phase 4.

Confirmed Phase 4 names: Worker `rwmod-api`, D1 `atc-database`, frontend
assets folder `web/rwmod-frontier`.

## File-Based Deployment

1. Copy `wrangler.toml.example` to `wrangler.toml`.
2. Confirm `database_name`, `database_id`, bucket names, and `[assets]`
   directory.
3. Install dependencies:

```powershell
npm install
```

4. Login and deploy:

```powershell
npx wrangler login
npx wrangler deploy
```

Unified local smoke test:

```powershell
npm run rwmod:unified-assets-smoke
```

For D1 SQL files, use Wrangler's `d1 execute` command with `--file`. Add
`--remote` when targeting the Cloudflare-hosted production D1 database.
