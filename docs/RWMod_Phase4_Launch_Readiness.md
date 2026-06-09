# RWMod Phase 4 Launch Readiness

Project: RWMod / RimNexus

Date: 2026-06-09

## Summary

Phase 4 prepares `rwmod.net` for production launch without deploying anything
yet. It turns the Phase 3 local Worker-backed preview into a safe deployment
plan with explicit operator gates for Cloudflare Pages, Workers, D1, R2,
Turnstile, DNS, and rollback.

Phase 4 is complete when the repository has a clear launch checklist and the
operator knows exactly which Cloudflare actions are still manual.

## Non-Goals

- Do not deploy the Worker.
- Do not publish Cloudflare Pages.
- Do not change `rwmod.net` DNS.
- Do not run remote D1 migrations.
- Do not seed production D1.
- Do not enable public report submission without Turnstile.
- Do not set `RWMOD_LOCAL_PREVIEW=1` in production.

## Recommended Production Topology

Use a separated frontend/API topology for the first public launch:

```text
https://rwmod.net
  -> Cloudflare Pages
  -> web/rwmod-frontier static frontend

https://api.rwmod.net
  -> Cloudflare Worker
  -> cloudflare/worker/worker-v2.js
  -> existing D1 binding: DB
  -> existing R2 binding: BUCKET
```

This keeps the first launch simple:

- Pages owns static frontend hosting and the custom domain.
- Worker owns API routes, D1, R2, admin/report logic, and Turnstile validation.
- The frontend sets `window.RWMOD_API_BASE = "https://api.rwmod.net"` before
  loading `assets/rwmod.js`.
- Same-origin API routing can be revisited later, but it is not required for
  Phase 4.

## Domain Plan

Primary launch:

- `rwmod.net`: Pages production frontend.
- `www.rwmod.net`: redirect to `rwmod.net`.
- `api.rwmod.net`: Worker custom domain.

Why this is the conservative choice:

- It avoids route-order ambiguity between Pages and Workers at `/api/*`.
- It makes API smoke tests independent from frontend deployments.
- It lets us roll back Pages and Worker separately.

Before changing DNS, confirm:

- The `rwmod.net` Cloudflare zone is active. Confirmed: yes.
- The domain is using Cloudflare nameservers.
- The Pages project exists and is attached to the desired production branch.
- The Worker exists and has a stable production name.
- The API host has a custom domain or route attached to the Worker.

## Cloudflare Resources

### Pages

Recommended project:

- Project name: `rwmod-frontier`
- Production branch: the branch chosen after Phase 3.5 review
- Build command: none for direct static upload, or a no-op/static copy command
- Output directory: `web/rwmod-frontier`

Required production config injection:

```html
<script>
  window.RWMOD_API_BASE = "https://api.rwmod.net";
  window.RWMOD_TURNSTILE_SITE_KEY = "PUBLIC_TURNSTILE_SITE_KEY";
</script>
```

This script must load before:

```html
<script src="./assets/rwmod.js"></script>
```

### Worker

Worker source:

- `cloudflare/worker/worker-v2.js`
- Production Worker name: `rwmod-api`

Required bindings:

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
- Pages project name: `rwmod-frontier`.
- Turnstile site key and secret: deferred.
- `www.rwmod.net`: redirect to `rwmod.net`.

Gate 2 status: resource inventory complete except Turnstile, which is
intentionally deferred. Public anonymous report submission must stay disabled
until Turnstile is created and wired.

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

Gate 5 is now stopped at the approval boundary. The real seed command remains
unexecuted:

```powershell
npx wrangler d1 execute atc-database --remote --file d1-tools/seed-rwmod-from-registry.sql
```

### Gate 6: Staging Frontend

Allowed after API is healthy:

- Publish Pages preview or staging environment.
- Inject `RWMOD_API_BASE` pointing at staging or production API.
- Inject public Turnstile site key.
- Verify search, detail, analyzer, missing-mod report, and Cancel/X behavior.

### Gate 7: Production Cutover

Allowed only after staging acceptance:

- Attach `rwmod.net` to Pages.
- Attach `api.rwmod.net` to Worker.
- Verify TLS, custom domains, and health endpoints.
- Announce the first public preview as beta/data-preview, not a finished
  encyclopedia.

## Smoke URLs

Production smoke targets after cutover:

```text
https://rwmod.net/
https://rwmod.net/?q=harmony
https://rwmod.net/?q=2009463077
https://rwmod.net/?mod=brrainz.harmony
https://api.rwmod.net/api/v1/health
https://api.rwmod.net/api/v1/rwmod/mods?q=2009463077&limit=5
https://api.rwmod.net/api/v1/rwmod/mods/brrainz.harmony
```

Expected production smoke:

- Harmony returns from `rwmod_catalog` after seed.
- Registry-only entries may return from `translation_registry`.
- Missing entries return empty result lists.
- Report submission rejects missing/invalid Turnstile.
- Report submission accepts valid Turnstile and enters review, not public
  conclusions.

## Rollback Plan

Pages rollback:

- Revert to the previous Pages deployment.
- Remove or disable the `rwmod.net` custom domain if needed.
- Keep `*.pages.dev` preview private or unadvertised.

Worker rollback:

- Roll back to the previous Worker deployment.
- Remove the `api.rwmod.net` custom domain/route if needed.
- Keep D1 migrations in place if they only added RWMod tables and indexes.

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
- Gate 5 seed preview is accepted.
- A clear stop/go decision exists for production seed, staging deployment, and
  production cutover.

## Official Reference Links

- Cloudflare Pages custom domains:
  <https://developers.cloudflare.com/pages/configuration/custom-domains/>
- Cloudflare Workers custom domains:
  <https://developers.cloudflare.com/workers/configuration/routing/custom-domains/>
- Cloudflare D1 Wrangler commands:
  <https://developers.cloudflare.com/d1/wrangler-commands/>
- Cloudflare Workers secrets:
  <https://developers.cloudflare.com/workers/configuration/secrets/>
- Cloudflare Turnstile server-side validation:
  <https://developers.cloudflare.com/turnstile/get-started/server-side-validation/>
