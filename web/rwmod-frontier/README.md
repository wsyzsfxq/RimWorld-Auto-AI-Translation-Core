# RWMod Frontier

Phase 2 local-display frontend MVP for RWMod / RimNexus.

This folder is intentionally separate from `web/player-portal`, which remains
the AI Translation Network translation portal. RWMod Frontier is the mod catalog
and diagnostic interface prototype for `rwmod.net`.

## Scope

Current prototype:

- mobile-first search page
- Worker API-first mod catalog data loading
- local mock fallback for static preview when the Worker is unavailable
- search accepts mod names, PackageIds, Workshop IDs, and Steam Workshop URLs
- mod detail above-the-fold status panel
- shareable `?mod=package.id` detail links with preserved search/filter state
- localization, compatibility, performance, and source panels
- Modlist Analyzer smart parser for ModsConfig.xml, RimSort/RimPy-style lines, PackageIds, Workshop IDs, and Steam Workshop URLs
- compact report sheet with Worker-compatible POST payload
- no-result missing mod report flow for PackageId or Workshop URL intake
- desktop and wide-screen dashboard layout improvements
- HUD-style dark UI using `docs/RWMod_Frontend_Design_System.md`

Not included yet:

- live D1 seed flow
- production routing
- Worker deployment
- production D1 migration

## Local Preview

Mock API preview:

```powershell
cd web/rwmod-frontier
node preview-server.mjs
```

Open:

```text
http://127.0.0.1:8794
```

Open a specific preview mod directly:

```text
http://127.0.0.1:8794/?mod=brrainz.harmony
```

Search accepts direct identifiers and pasted Steam Workshop URLs:

```text
http://127.0.0.1:8794/?q=2009463077
http://127.0.0.1:8794/?q=https%3A%2F%2Fsteamcommunity.com%2Fsharedfiles%2Ffiledetails%2F%3Fid%3D2009463077
```

Modlist Analyzer accepts mixed input:

```xml
<li>brrainz.harmony</li>
<li>vanillaexpanded.vfe.core</li>
```

```text
001: brrainz.harmony
- https://steamcommunity.com/sharedfiles/filedetails/?id=2023507013
[3] rimthreaded.core
```

This serves static files and same-origin mock RWMod API routes:

```text
/api/v1/rwmod/mods
/api/v1/rwmod/mods/{packageId}
/api/v1/rwmod/mods/{packageId}/localization
/api/v1/rwmod/mods/{packageId}/compatibility
/api/v1/rwmod/mods/{packageId}/performance
/api/v1/rwmod/reports
```

The preview report endpoint returns a mock review queue response. Production
submission still requires Cloudflare Turnstile.

Worker-backed Phase 3 preview:

```powershell
cd web/rwmod-frontier
node preview-server.mjs --worker
```

Or from `cloudflare/worker`:

```powershell
npm run rwmod:frontier-worker-preview
```

This mode serves the same frontend, but same-origin `/api/*` requests are
handled by `cloudflare/worker/worker-v2.js` with an in-memory SQLite database
initialized from `cloudflare/worker/schema.sql` and seeded from the local RWMod
fixture. It validates the Worker contract, RWMod D1 tables,
`TranslationRegistry` fallback, and report insertion without Wrangler, remote
D1, Cloudflare DNS, or Worker deployment.

Automated Worker-backed smoke test:

```powershell
cd ../../cloudflare/worker
npm run rwmod:frontier-worker-smoke
```

Static fallback preview:

```powershell
cd web/rwmod-frontier
python -m http.server 8793 --bind 127.0.0.1
```

Open:

```text
http://127.0.0.1:8793
```

By default the page calls same-origin Worker routes:

```text
/api/v1/rwmod/mods
/api/v1/rwmod/mods/{packageId}
```

The static preview intentionally falls back to `mock-api-data.json` because it
does not serve API routes.

For a separate Worker origin, set `window.RWMOD_API_BASE` before
`assets/rwmod.js` loads.

For production report submission, also set the public Turnstile site key before
`assets/rwmod.js` loads:

```html
<script>
  window.RWMOD_TURNSTILE_SITE_KEY = "your-public-site-key";
</script>
```

## Design References

- `docs/RWMod_Frontend_Design_System.md`
- `docs/RWMod_Phase1_Data_Model.md`
