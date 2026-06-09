# RWMod Phase 4 Launch Readiness

Project: RWMod / RimNexus

Date: 2026-06-09

## Summary

Phase 4 prepares `rwmod.net` for production launch and records the first
production cutover. It turns the Phase 3 local Worker-backed preview into a
safe deployment plan with explicit operator gates for a unified Cloudflare
Worker, Workers Static Assets, D1, R2, Turnstile, DNS, and rollback.

Phase 4 is complete when the repository has a clear launch checklist, the
unified Worker production cutover is verified, and the operator knows exactly
which Cloudflare actions are still manual.

## Non-Goals

- Do not create or publish a Cloudflare Pages project.
- Do not run additional remote D1 migrations.
- Do not run additional production D1 seed steps.
- Do not enable public report submission without Turnstile.
- Do not set `RWMOD_LOCAL_PREVIEW=1` in production.

Gate 7 exception after operator approval:

- Deploy Worker `rwmod-api` to `rwmod.net`.
- Attach `rwmod.net` as the unified Worker custom domain.

## Recommended Production Topology

Use a unified full-stack Worker topology for the first public launch:

```text
https://rwmod.net
  -> Cloudflare Worker
  -> cloudflare/worker/worker-v2.js
  -> Workers Static Assets: web/rwmod-frontier
  -> same-origin /api/v1/* routes
  -> existing D1 binding: DB
  -> existing R2 binding: BUCKET
```

This keeps the first launch simple and avoids split-host frontend/API routing:

- One Worker owns the custom domain, static frontend assets, API routes, D1,
  R2, admin/report logic, and Turnstile validation.
- `web/rwmod-frontier` is configured as the Worker's static assets directory.
- The frontend uses same-origin `/api/v1/*` routes, so
  `window.RWMOD_API_BASE` is not required for production.
- Workers Assets serve matching static files directly. The Worker script is
  invoked first only for `/api/*` through `assets.run_worker_first`.
- Billing posture: static asset requests are free and unlimited under Workers
  Static Assets. Requests that match `/api/*` still invoke the Worker script and
  are billed according to Workers pricing.

## Domain Plan

Primary launch:

- `rwmod.net`: unified Worker custom domain serving frontend and API.
- `www.rwmod.net`: redirect to `rwmod.net`.
- `api.rwmod.net`: not required for the first launch.

Why this is the conservative choice after the Gate 6 architecture change:

- It removes split-host routing and CORS assumptions from the first launch.
- It avoids Pages asset path mismatch issues; CSS, JS, and API routes are
  validated under one Worker origin.
- It keeps static files on Workers Assets while `/api/*` enters Worker code.
- It leaves `api.rwmod.net` available as a future alias if needed, but not as a
  required dependency.

Before changing DNS, confirm:

- The `rwmod.net` Cloudflare zone is active. Confirmed: yes.
- The domain is using Cloudflare nameservers.
- The Worker exists and has a stable production name.
- The Worker has the `rwmod.net` custom domain or route attached.

## Cloudflare Resources

### Unified Worker Assets

Production project:

- Worker name: `rwmod-api`
- Worker script: `cloudflare/worker/worker-v2.js`
- Static assets directory: `web/rwmod-frontier`
- Static routing:
  - `assets.not_found_handling = "single-page-application"`
  - `assets.run_worker_first = ["/api/*"]`
  - `assets.binding = "ASSETS"`

Expected Wrangler configuration:

```toml
[assets]
directory = "../../web/rwmod-frontier"
binding = "ASSETS"
not_found_handling = "single-page-application"
run_worker_first = ["/api/*"]
```

Production config injection:

- Do not set `window.RWMOD_API_BASE`; same-origin API is the default.
- Set only `window.RWMOD_TURNSTILE_SITE_KEY` when public reports are enabled.
- Until Turnstile exists, public report submission must remain disabled or
  rejected by Worker-side validation.

### Worker

Worker source:

- `cloudflare/worker/worker-v2.js`
- Production Worker name: `rwmod-api`

Required bindings:

- Static assets as `ASSETS`: `web/rwmod-frontier`
- D1 database as `DB`: `atc-database`
  (`7d266a58-6690-45c5-a6f7-48eccf9c41e4`)
- Translation R2 bucket as `BUCKET`: `rimworld-translation-hub`
- RWMod public asset R2 bucket as `RWMOD_ASSETS`: `rwmod-assets`

Required secrets:

- `MASTER_SECRET`
- `TOKEN_HASH_PEPPER`
- `TURNSTILE_SECRET_KEY` deferred until public report submission is enabled

Optional transition secret:

- `LEGACY_OFFICIAL_SECRETS`

Forbidden in production:

- `RWMOD_LOCAL_PREVIEW=1`

### D1

Production D1 must be handled in this order:

1. Inspect the existing database:

   ```powershell
   npx wrangler d1 execute YOUR_D1_DATABASE_NAME --remote --file d1-tools/inspect-existing-registry.sql
   ```

2. Apply only safe migrations that use `CREATE TABLE IF NOT EXISTS` and
   `CREATE INDEX IF NOT EXISTS`:

   ```powershell
   npx wrangler d1 execute YOUR_D1_DATABASE_NAME --remote --file migrations/0004_add_rwmod_catalog_tables.sql
   ```

3. Preview the registry seed output:

   ```powershell
   npx wrangler d1 execute YOUR_D1_DATABASE_NAME --remote --file d1-tools/preview-rwmod-seed-from-registry.sql
   ```

4. Seed only after the preview looks correct:

   ```powershell
   npx wrangler d1 execute YOUR_D1_DATABASE_NAME --remote --file d1-tools/seed-rwmod-from-registry.sql
   ```

Do not run `schema.sql` against an existing production database. It is for a
brand-new database only.

### R2

R2 storage must be split early so translation files, encyclopedia assets, and
future player attachments do not collapse into one long-term storage bucket.

Required bucket boundaries:

- `rimworld-translation-hub`: existing cloud translation ZIP/files only. This
  remains bound to the Worker as `BUCKET` for current mod-facing download
  endpoints.
- `rwmod-assets`: RWMod encyclopedia public assets such as mod thumbnails,
  curated screenshots, generated preview images, guide images, and future
  cached catalog media. Create this during Phase 4 even if the Worker does not
  write to it yet.
- `rwmod-report-uploads`: future private player report attachments. Do not
  create or expose it until the report-attachment feature exists.

RWMod catalog pages must not mirror third-party localization packs. Traditional
localization groups should receive outbound links and attribution, not
duplicated downloads.

Phase 4 binding posture:

- Keep `BUCKET = rimworld-translation-hub`.
- Reserve `RWMOD_ASSETS = rwmod-assets`.
- Do not add public write paths to `rwmod-assets` until Phase 5+ asset
  ingestion rules exist.
- Do not bind or create `rwmod-report-uploads` yet unless report attachments are
  explicitly started.

### Turnstile

Public RWMod reports require:

- A public Turnstile site key injected into the frontend.
- A `TURNSTILE_SECRET_KEY` Worker secret.
- Worker-side verification through Cloudflare Turnstile Siteverify.

Preview-only token bypass is allowed only when `RWMOD_LOCAL_PREVIEW=1` and the
request comes from local preview hostnames. Production must not enable that
flag.

## Launch Gates

### Gate 1: Local Freeze

Required before any Cloudflare work:

- `node --check web/rwmod-frontier/assets/rwmod.js`
- `node --check web/rwmod-frontier/preview-server.mjs`
- `node --check cloudflare/worker/worker-v2.js`
- `node --check cloudflare/worker/d1-tools/frontier-worker-preview-smoke.mjs`
- `node cloudflare/worker/d1-tools/local-rwmod-smoke.mjs`
- `node cloudflare/worker/d1-tools/frontier-worker-preview-smoke.mjs`
- `git diff --check -- web/rwmod-frontier cloudflare/worker docs`

Expected behavior:

- `unknown` compatibility is never displayed as safe.
- Heavy performance remains visibly warned.
- Report Cancel/X closes without requiring Summary.
- `POST /api/v1/rwmod/reports` works only through preview or verified
  Turnstile paths.

### Gate 2: Cloudflare Inventory

Operator must provide or confirm:

- Cloudflare account and zone ownership for `rwmod.net`: confirmed active.
- D1 database name and id: `atc-database`
  / `7d266a58-6690-45c5-a6f7-48eccf9c41e4`.
- R2 bucket names and intended boundaries:
  - `rimworld-translation-hub`: existing translation files.
  - `rwmod-assets`: RWMod public encyclopedia assets.
  - `rwmod-report-uploads`: future private report attachments, not created for
    Phase 4.
- Worker production name: `rwmod-api`.
- Pages project: cancelled after the unified Worker Assets decision.
- Turnstile site key and secret: deferred.
- `www.rwmod.net`: redirect to `rwmod.net`.

Gate 2 status: resource inventory complete except Turnstile, which is
intentionally deferred. Public anonymous report submission must stay disabled
until Turnstile is created and wired.

Gate 2 update after unified Worker decision:

- Cloudflare Pages project `rwmod-frontier` is no longer required.
- `api.rwmod.net` is no longer required for the first launch.
- `rwmod.net` should attach directly to Worker `rwmod-api`.

### Gate 3: Remote Read-Only Check

Allowed after operator confirmation:

- Run only read-only D1 inspect SQL.
- Check `/api/v1/health` on a staging Worker.
- Do not mutate D1 yet.

Gate 3 status: completed on 2026-06-09 against remote D1 `atc-database`
(`7d266a58-6690-45c5-a6f7-48eccf9c41e4`).

Executed read-only SQL:

- `PRAGMA table_info(TranslationRegistry);`
- Translation registry count summary.
- Translation registry language/type count summary.

Observed D1 safety result:

- First file-based inspect: 3 queries, 2749 rows read, 0 rows written.
- Follow-up schema query: rows written 0.
- Follow-up count query: rows written 0.
- Follow-up language/type query: rows written 0.

Observed `TranslationRegistry` shape:

- 18 columns.
- Primary key: `RecordId`.
- Existing columns include `PackageId`, `Language`, `ModName`,
  `LatestVersion`, `LastUpdated`, `ModLastUpdated`, `UploaderID`, `Author`,
  `TranslationType`, `IsVerified`, `FileUrl`, `TargetModVersion`,
  `TranslationDate`, `IsSmartMerged`, `MergedAiCount`, `UpdateLog`, and
  `IsDeleted`.

Observed registry counts:

- Total records: 916.
- Soft-deleted records: 494.
- Active Simplified Chinese records: 420.

Observed language/type distribution:

- `ChineseSimplified` / `AI_Auto`: 908.
- `ChineseTraditional` / `AI_Auto`: 5.
- `ChineseSimplified` / `Manual`: 1.
- `ChineseSimplified` / `Official_Group`: 1.
- `ChineseTraditional` / `Official_Group`: 1.

### Gate 4: Remote Migration

Allowed after inspect output is accepted:

- Run `0004_add_rwmod_catalog_tables.sql`.
- Re-run read-only table/index checks.
- Do not run seed until the migration output is clean.

Gate 4 status: completed on 2026-06-09 against remote D1 `atc-database`
(`7d266a58-6690-45c5-a6f7-48eccf9c41e4`).

Pre-migration SQL safety check:

- `0004_add_rwmod_catalog_tables.sql` contains only
  `CREATE TABLE IF NOT EXISTS` and `CREATE INDEX IF NOT EXISTS` statements.
- No `DROP`, `DELETE`, `ALTER`, `UPDATE`, `INSERT`, `REPLACE`, or `TRUNCATE`
  statements were present.

Migration execution result:

- 33 queries processed.
- 57 rows read.
- 51 rows written.
- Database bookmark:
  `000004d1-00000007-00005085-07d130c245b804e7e790c8f34a4b5827`.

Post-migration verification:

- 9 RWMod tables exist:
  - `RWModAliases`
  - `RWModCompatibilityReports`
  - `RWModDependencies`
  - `RWModGuideLinks`
  - `RWModLocalizationStatus`
  - `RWModModSources`
  - `RWModModerationEvents`
  - `RWModMods`
  - `RWModPerformanceReports`
- 24 `idx_rwmod*` indexes exist.
- `RWModMods` row count: 0.
- `TranslationRegistry` row count remains 916.
- Verification queries wrote 0 rows.

### Gate 5: Seed Preview Then Seed

Allowed after migration is clean:

- Run preview seed SQL first.
- Review row counts and sample PackageIds.
- Run real seed only after approval.

Gate 5 preview status: completed on 2026-06-09 against remote D1
`atc-database` (`7d266a58-6690-45c5-a6f7-48eccf9c41e4`).

Preview safety check:

- `d1-tools/preview-rwmod-seed-from-registry.sql` uses CTE-backed `SELECT`
  queries only.
- No real seed SQL was executed.
- No `RWMod*` catalog rows were written during preview.

Preview SQL file result:

- 3 queries processed.
- 6772 rows read.
- 0 rows written.
- Latest observed preview bookmark:
  `000004d3-00000004-00005085-623e5dfbea71ff297592ca8762bb3eb6`.

Preview metrics:

- `active_registry_records`: 422.
- `distinct_seedable_packages`: 422.
- `packages_with_verified_registry_record`: 0.
- `packages_already_in_rwmod_catalog`: 0.
- `registry_localization_rows_already_seeded`: 0.

Sample package review:

- A short read-only sample query returned recent registry candidates such as
  `owlchemist.cleanpathfinding`, `kindseal.ufli`,
  `vanillaexpanded.temperature`, `vr.missilegirl`,
  `syrchalis.processor.framework`, and `smashphil.vehicleframework`.
- All sampled rows had low confidence because no verified registry records were
  present in the current preview set.
- Some latest registry candidates include mature-content PackageIds. This is a
  catalog policy concern, not a seed-blocking SQL issue. Public browse surfaces
  should gain content classification, filtering, or moderation rules before
  treating every seeded row as front-page eligible.

Post-preview row check:

- `RWModMods` row count: 0.
- `RWModLocalizationStatus` row count: 0.

Real seed status: completed on 2026-06-09 after operator approval.

Executed seed command:

```powershell
npx wrangler d1 execute atc-database --remote --file d1-tools/seed-rwmod-from-registry.sql
```

Seed execution result:

- 2 queries processed.
- 5210 rows read.
- 5064 rows written.
- Reported changes: 845.
- Database size after seed: 3.69 MB.
- Seed bookmark:
  `000004d8-00000008-00005085-fe96cb70ddf3e2799cd0b0733e26deeb`.

Post-seed verification:

- `RWModMods` row count: 422.
- `RWModLocalizationStatus` row count: 422.
- `TranslationRegistry` row count remains 916.
- `RWModMods` status distribution:
  - 422 rows are `partial` localization.
  - 422 rows are `unknown` compatibility.
  - 422 rows are `unknown` performance.
  - 422 rows are `cloud_record` trust level.
  - 422 rows are `low` confidence.
- `RWModLocalizationStatus` language distribution:
  - `ChineseSimplified`: 420 rows.
  - `ChineseTraditional`: 2 rows.

Gate 5 is complete. No R2 bucket was written during this seed; the first seed
created relational catalog rows in D1 only.

### Gate 6: Unified Worker Assets Staging

Allowed after API is healthy:

- Configure Workers Static Assets on `rwmod-api`.
- Verify `/`, `/assets/rwmod.css`, `/assets/rwmod.js`, SPA deep links, and
  `/api/v1/*` routes under the same Worker origin.
- Inject public Turnstile site key only after Turnstile exists.
- Verify search, detail, analyzer, missing-mod report, and Cancel/X behavior.

Gate 6 local unified-assets status: completed on 2026-06-09 without deployment.

Configuration validated:

- `cloudflare/worker/wrangler.toml` uses `[assets]`.
- `directory = "../../web/rwmod-frontier"`.
- `binding = "ASSETS"`.
- `not_found_handling = "single-page-application"`.
- `run_worker_first = ["/api/*"]`.

Local verification:

- `wrangler deploy --dry-run --keep-vars` read 7 frontend asset files and
  exposed `env.ASSETS`.
- `npm run rwmod:unified-assets-smoke` passed.
- Local Worker+Assets smoke returned 200 for:
  - `/`
  - `/assets/rwmod.css`
  - `/assets/rwmod.js`
  - `/catalog/deep-link`
  - `/?mod=owlchemist.cleanpathfinding`
  - `/api/v1/health`
  - `/api/v1/rwmod/mods?q=owlchemist.cleanpathfinding&limit=5`

Gate 6 is stopped before any Cloudflare deployment.

### Gate 7: Production Cutover

Allowed only after staging acceptance:

- Deploy Worker `rwmod-api` with static assets.
- Attach `rwmod.net` to Worker `rwmod-api`.
- Keep `www.rwmod.net` redirected to `rwmod.net`.
- Verify TLS, custom domain, static assets, and health endpoints.
- Announce the first public preview as beta/data-preview, not a finished
  encyclopedia.

Gate 7 status: completed on 2026-06-10 after operator approval.

Production deployment:

```powershell
npx wrangler deploy --keep-vars --domain rwmod.net
```

Deployment results:

- First unified production deployment succeeded on Worker `rwmod-api`.
- First observed production version ID:
  `fba51400-1326-4cf2-b402-c521821f4d04`.
- A follow-up Worker compatibility fix was deployed after live RWMod catalog
  API smoke exposed an old `TranslationRegistry` schema mismatch.
- Current production version ID after the fix:
  `dbaf1d10-b1ea-4e3e-898a-326178c10674`.
- Custom domain attached by Wrangler: `rwmod.net`.
- No Cloudflare Pages deployment was performed.

Live issue found and fixed:

- Symptom: `/api/v1/rwmod/mods` and
  `/api/v1/rwmod/mods/{packageId}` returned HTTP 500 while the frontend,
  static assets, and `/api/v1/health` were healthy.
- Cause: the remote `TranslationRegistry` table does not currently include the
  optional `DownloadCount` column. RWMod registry fallback SQL referenced it
  directly for `TotalDownloadCount`.
- Fix: `worker-v2.js` now inspects `TranslationRegistry` columns and projects
  `0 AS DownloadCount` / `0 AS TotalDownloadCount` when the optional column is
  absent. This preserves old D1 compatibility without running a production
  `ALTER TABLE`.

Local verification before redeploy:

- `node --check worker-v2.js`: passed.
- `node --check d1-tools/local-rwmod-smoke.mjs`: passed.
- `node --check d1-tools/frontier-worker-preview-smoke.mjs`: passed.
- `node --check d1-tools/unified-worker-assets-smoke.mjs`: passed.
- `npm run rwmod:local-smoke`: passed.
- `npm run rwmod:frontier-worker-smoke`: passed.
- `npm run rwmod:unified-assets-smoke`: passed.
- `wrangler deploy --dry-run --keep-vars`: passed and read 7 static asset
  files from `web/rwmod-frontier`.

Live smoke after redeploy:

- `https://rwmod.net/`: HTTP 200, HTML.
- `https://rwmod.net/assets/rwmod.css`: HTTP 200, CSS.
- `https://rwmod.net/assets/rwmod.js`: HTTP 200, JavaScript.
- `https://rwmod.net/?q=owlchemist.cleanpathfinding`: HTTP 200, SPA HTML.
- `https://rwmod.net/api/v1/health`: HTTP 200, JSON.
- `https://rwmod.net/api/v1/rwmod/mods?q=owlchemist.cleanpathfinding&limit=5`:
  HTTP 200, first result `owlchemist.cleanpathfinding` from `rwmod_catalog`.
- `https://rwmod.net/api/v1/rwmod/mods/owlchemist.cleanpathfinding`:
  HTTP 200, above-the-fold status available.
- `https://rwmod.net/api/v1/rwmod/mods/owlchemist.cleanpathfinding/localization`:
  HTTP 200, status `partial`.
- `https://rwmod.net/api/v1/rwmod/mods/owlchemist.cleanpathfinding/compatibility`:
  HTTP 200, status `unknown`.
- `https://rwmod.net/api/v1/rwmod/mods/owlchemist.cleanpathfinding/performance`:
  HTTP 200, impact `unknown`.
- `https://rwmod.net/api/v1/rwmod/mods?q=definitely.notindexed.999&limit=5`:
  HTTP 200, empty result list.
- `POST https://rwmod.net/api/v1/rwmod/reports`: rejected with HTTP 503 while
  Turnstile is not configured. This is expected and keeps public report
  submission closed.

Current production data limitation:

- `https://rwmod.net/api/v1/rwmod/mods?q=2009463077&limit=5` currently returns
  an empty result list because the first D1 seed did not populate
  `PrimaryWorkshopId` from Workshop metadata. This is a data enrichment task
  for the next phase, not a Worker routing failure.

## Smoke URLs

Production smoke targets after cutover:

```text
https://rwmod.net/
https://rwmod.net/?q=harmony
https://rwmod.net/?q=2009463077
https://rwmod.net/?mod=brrainz.harmony
https://rwmod.net/assets/rwmod.css
https://rwmod.net/assets/rwmod.js
https://rwmod.net/api/v1/health
https://rwmod.net/api/v1/rwmod/mods?q=2009463077&limit=5
https://rwmod.net/api/v1/rwmod/mods/brrainz.harmony
```

Expected production smoke:

- Harmony returns from `rwmod_catalog` after seed.
- Registry-only entries may return from `translation_registry`.
- Missing entries return empty result lists.
- Report submission rejects missing/invalid Turnstile.
- Report submission accepts valid Turnstile and enters review, not public
  conclusions.

## Rollback Plan

Worker rollback:

- Roll back to the previous Worker deployment.
- Remove the `rwmod.net` custom domain/route if needed.
- Keep D1 migrations in place if they only added RWMod tables and indexes.
- If a Worker Assets deployment breaks frontend routing, roll back to the
  previous Worker version instead of creating a Pages fallback.

D1 rollback posture:

- Do not drop tables during an incident.
- Hide RWMod frontend links instead.
- Leave added `RWMod*` tables idle until a reviewed migration cleanup is planned.

Secrets rollback:

- Rotate any exposed `MASTER_SECRET`, `TOKEN_HASH_PEPPER`, Turnstile secret, or
  legacy official codes.
- Revoke leaked privilege codes through the admin API or D1-backed code tables.

## Phase 4 Exit Criteria

Phase 4 is ready to exit when:

- This launch readiness document is reviewed.
- The production topology is accepted.
- The operator has confirmed the Cloudflare resource names.
- Local Phase 3 smoke checks still pass.
- Gate 5 seed preview and production D1 seed are complete.
- A clear stop/go decision exists for staging deployment and production cutover.

## Official Reference Links

- Cloudflare Workers custom domains:
  <https://developers.cloudflare.com/workers/configuration/routing/custom-domains/>
- Cloudflare Workers Static Assets:
  <https://developers.cloudflare.com/workers/static-assets/>
- Cloudflare Workers Static Assets SPA routing:
  <https://developers.cloudflare.com/workers/static-assets/routing/single-page-application/>
- Cloudflare Workers Static Assets billing and limitations:
  <https://developers.cloudflare.com/workers/static-assets/billing-and-limitations/>
- Cloudflare D1 Wrangler commands:
  <https://developers.cloudflare.com/d1/wrangler-commands/>
- Cloudflare Workers secrets:
  <https://developers.cloudflare.com/workers/configuration/secrets/>
- Cloudflare Turnstile server-side validation:
  <https://developers.cloudflare.com/turnstile/get-started/server-side-validation/>
