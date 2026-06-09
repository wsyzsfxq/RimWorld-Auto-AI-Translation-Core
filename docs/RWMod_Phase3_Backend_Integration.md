# RWMod Phase 3 Backend Integration

Project: RWMod / RimNexus

Date: 2026-06-09

## Summary

Phase 3 connects the RWMod frontend MVP to the real local Worker contract
without deploying Cloudflare Workers, running remote D1 migrations, or changing
DNS. The frontend can now run in two local modes:

- Mock preview: static UI plus mock same-origin API.
- Worker-backed preview: static UI plus `cloudflare/worker/worker-v2.js`
  handling `/api/*` through an in-memory SQLite D1-like binding.

The Worker-backed path validates the Phase 1 catalog schema, the Phase 2
frontend contract, `TranslationRegistry` fallback behavior, and RWMod report
insertion before any production deployment.

## Local Architecture

Worker-backed preview flow:

```text
web/rwmod-frontier/index.html
  -> same-origin /api/v1/rwmod/*
  -> web/rwmod-frontier/preview-server.mjs --worker
  -> cloudflare/worker/worker-v2.js
  -> in-memory SQLite loaded from cloudflare/worker/schema.sql
  -> RWMod* catalog tables + TranslationRegistry fallback + FeedbackReports
```

The preview database is seeded from `web/rwmod-frontier/mock-api-data.json`.
This keeps local scenarios deterministic while still exercising the real Worker
route handlers and SQL queries.

## Commands

Mock preview:

```powershell
cd web/rwmod-frontier
node preview-server.mjs
```

Worker-backed preview:

```powershell
cd web/rwmod-frontier
node preview-server.mjs --worker
```

Worker-backed preview from the Worker folder:

```powershell
cd cloudflare/worker
npm run rwmod:frontier-worker-preview
```

Automated Worker-backed smoke:

```powershell
cd cloudflare/worker
npm run rwmod:frontier-worker-smoke
```

In this Codex environment, use the bundled Node executable directly if `npm` is
unavailable:

```powershell
C:\Users\g1061\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe cloudflare/worker/d1-tools/frontier-worker-preview-smoke.mjs
```

## Verified Behaviors

- `GET /api/v1/health` is served by `worker-v2.js`.
- `GET /api/v1/rwmod/mods?q=2009463077` returns Harmony from `RWModMods` with
  `DataSource: rwmod_catalog`.
- `GET /api/v1/rwmod/mods/brrainz.harmony` returns the above-the-fold status
  fields required by the frontend.
- `GET /api/v1/rwmod/mods?q=rwmod.registry.seedonly` proves
  `TranslationRegistry` fallback with `DataSource: translation_registry`.
- RimThreaded remains `conflict` / `heavy`.
- Performance Optimizer remains `unknown`; unknown is not treated as safe.
- True no-result queries return an empty result and allow missing-mod reporting.
- `POST /api/v1/rwmod/reports` writes to local `FeedbackReports` through the
  real Worker report handler.

## Safety Boundaries

- Phase 3 does not deploy Workers.
- Phase 3 does not edit Cloudflare DNS or `rwmod.net`.
- Phase 3 does not execute remote D1 migrations or seed production D1.
- `RWMOD_LOCAL_PREVIEW=1` is only for local Worker-backed preview. It allows the
  frontend preview Turnstile token only for local hostnames such as
  `rwmod.local`, `127.0.0.1`, `localhost`, or `::1`.
- Production Workers must not set `RWMOD_LOCAL_PREVIEW`.
- RWMod still does not mirror or redistribute third-party localization files.

## Next Production Steps

These require explicit operator confirmation before running:

1. Inspect the existing production D1 schema.
2. Apply `migrations/0004_add_rwmod_catalog_tables.sql` to the real D1 database.
3. Run `d1-tools/preview-rwmod-seed-from-registry.sql` and inspect counts.
4. Run `d1-tools/seed-rwmod-from-registry.sql` only after preview output is
   accepted.
5. Configure the production Turnstile secret.
6. Deploy `worker-v2.js`.
7. Point the hosted RWMod frontend at the production Worker origin.
