# Product And Web Roadmap

## Current Cloud API Surface

The RimWorld mod currently expects these Cloud Worker endpoints:

- `GET /registry?t=...`
- `GET /download/{packageId}/{language}`
- `GET /download/{packageId}/{language}?recordId=...`
- `POST /upload`
- `DELETE /delete/{packageId}/{language}?recordId=...`

The cloud record shape used by the mod:

- `RecordId`
- `PackageId`
- `Language`
- `ModName`
- `LatestVersion`
- `LastUpdated`
- `ModLastUpdated`
- `UploaderID`
- `Author`
- `TranslationType`
- `IsVerified`
- `FileUrl`
- `TargetModVersion`
- `TranslationDate`
- `IsSmartMerged`
- `MergedAiCount`
- `UpdateLog`

## Mod Feature Roadmap

### Phase 1: Reliability And Safety

- Add a preflight check for RimWorld 1.6 paths, active language, pack folder existence, write permissions, and cloud connectivity.
- Add a dry-run scan mode that estimates files, keys, characters, provider cost, and estimated time before translation starts.
- Add stronger translation validation before injection: placeholders, XML tags, newline structure, grammar prefixes, and suspicious untranslated text.
- Add a safer cloud download preview with diff summary before overwriting local translation files.

### Phase 2: Long Running Workflow

- Add resumable batch translation with a durable local queue.
- Add clearer pause, resume, retry, and skip controls.
- Persist per-mod scan checkpoints so interrupted runs can continue without starting over.
- Add provider fallback policy: retry limits, alternate provider selection, and clearer failure reasons.

### Phase 3: Visibility

- Add a per-mod translation health dashboard:
  - translated key count
  - missing key count
  - outdated key count
  - last scan time
  - source mod version
  - cloud version availability
- Add local reports for validation warnings and unresolved strings.
- Add export/package validation for Steam Workshop-ready translation packs.

## Web Portal Roadmap

The website should be an actual cloud catalog and management console, not just a landing page.

### Phase 1: Public Catalog

- Search translation records by mod name, package id, language, author, version, and translation type.
- Show one page per mod/package id.
- Show available languages and versions.
- Show verified/manual/AI labels clearly.
- Show update log, smart-merge status, target mod version, upload date, and download count.
- Provide direct download links or a "copy package id" flow for players.

### Phase 2: Admin Dashboard

- Admin login or token-based session.
- Verify or unverify records.
- Delete records.
- Promote trusted translation groups.
- View upload metadata, file size, checksum, uploader id hash, and moderation history.
- Add audit log for every admin action.
- Manage dynamic privilege codes from the web dashboard.
- Allow only the master bootstrap code, currently planned as `MINI_666`, to create, revoke, rotate, or promote privilege codes.

### Phase 3: Upload Review Pipeline

- Validate uploaded ZIP before writing final records:
  - expected folder structure
  - XML parse success
  - file count and size limits
  - path traversal protection
  - package id filename match
  - language folder match
- Store the file in R2 with deterministic keys.
- Store record metadata in D1.
- Optionally quarantine suspicious uploads for manual review.

### Phase 4: Analytics And Health

- Track aggregate downloads by package id, language, and record id.
- Show popular mods/languages.
- Show recent uploads and recently updated source mods.
- Add API health checks for D1/R2 latency and Worker errors.
- Add lightweight rate-limit and abuse dashboards.

### Phase 5: Mod Integration Improvements

- Add a web URL field to cloud records so the mod can open the web page for a package id.
- Add web-side diff previews for translation versions.
- Add "latest recommended record" policy so the mod does not need to infer the best version from all records.
- Add compatibility fields for RimWorld version and mod version.

## RWMod Encyclopedia Roadmap

RWMod should evolve from a searchable catalog into a structured RimWorld mod
encyclopedia. The order below keeps the current MVP useful while reserving a
clear path toward a mcmod-style knowledge system.

### Phase 4: Launch Readiness And Production Cutover

Phase 4 prepares `rwmod.net` for a safe beta launch without deploying by
accident. The source of truth is `docs/RWMod_Phase4_Launch_Readiness.md`.

- Choose the first production topology:
  - `rwmod.net` on Cloudflare Pages for the static frontend.
  - `api.rwmod.net` on Cloudflare Workers for API, D1, R2, admin, and reports.
- Keep production deployment behind explicit operator gates:
  - Cloudflare resource inventory.
  - Read-only D1 inspection.
  - Safe RWMod catalog migration.
  - Seed preview.
  - Approved seed.
  - Staging frontend smoke.
  - Production custom-domain cutover.
- Require Turnstile before public report submission.
- Forbid `RWMOD_LOCAL_PREVIEW=1` in production.
- Keep rollback simple:
  - Pages rollback for frontend.
  - Worker rollback for API.
  - No emergency `DROP TABLE`; hide or disable RWMod surfaces instead.

### Phase 5: Precision Homepage Portal

The homepage should stop presenting an unfiltered wall of every indexed mod.
Its first screen should behave as a precise entry portal:

- Keep the central visual state clean and search-led.
- Make the multifunction search box the primary stage, supporting keywords,
  PackageId, Workshop ID, and full Steam Workshop URLs.
- Keep broad catalog browsing below the primary search experience, not above it.
- Add a dynamic visual recommendation matrix with image-backed cards:
  - Popular
  - Newly Indexed
- Add a nine-cell tag navigation matrix for fast category entry:
  - RimWorld version filters such as 1.4, 1.5, and 1.6.
  - Content categories such as framework/library mods, story expansion, weapons,
    scenario/storyteller content, buildings/defenses, race/faction content,
    QoL optimization, performance tools, and translation/localization helpers.
- Recommendation cards must be data-driven and explain why they appear, such as
  downloads, recent indexing, report activity, or moderator curation.

### Phase 6: Advanced Mod Detail Structure

The mod detail page should become a layered encyclopedia page with four fixed
major zones from top to bottom:

1. Above-the-fold command zone:
   - Support game versions.
   - Steam Workshop direct link.
   - Localization progress/status.
   - Performance impact rating such as Light, Medium, Heavy, or Unknown.
   - Compatibility and safety warning summary.
2. Mod Synergy Graph:
   - A draggable interactive topology graph.
   - Shows dependencies, optional dependencies, incompatible dependencies,
     load-order hints, and synergy/extension relationships.
   - Must clearly distinguish verified relationships from player reports or
     inferred metadata.
3. Micro Database:
   - Indexes the concrete game content added by the mod.
   - Supports drill-down detail pages or panels for individual entries.
   - Initial content groups should include weapons/equipment, buildings and
     defenses, traits/backgrounds, skills/abilities, factions/races, research,
     recipes, apparel, implants, incidents, quests, and scenario additions.
4. Player Salon:
   - A dedicated discussion/comment area for bug reports, localization help,
     play notes, load-order experience, and modlist advice.
   - Moderation and trust labels must remain visible so raw comments do not
     automatically become public conclusions.

### Phase 7: Micro Database And Community Knowledge Expansion

Phase 7 deepens RWMod from mod-level metadata into entity-level knowledge:

- Build importer/scanner pipelines for mod definitions when legally and
  technically appropriate.
- Store entity records separately from mod catalog records, with source,
  version, confidence, and moderation metadata.
- Provide entity pages for weapons, equipment, buildings, defenses, traits,
  backgrounds, skills, abilities, races, factions, recipes, research projects,
  incidents, quests, and scenario parts.
- Link entity records back to the parent mod detail page and to related mods.
- Add comparison and filtering tools for entity-level data, such as weapon DPS,
  armor stats, building cost, ability cooldown, work type impact, or research
  prerequisites.
- Keep third-party copyright boundaries: index metadata and player-authored
  explanations, but do not mirror unauthorized files or redistribute assets.
- Promote comments, reports, and player notes into curated public knowledge only
  after review.

## Dynamic Privilege Code Plan

The current Worker uses a hardcoded master code and a hardcoded official-code list. The web portal should replace the static official-code list with D1-backed privilege codes.

### Goals

- Keep `MINI_666` as the master bootstrap authority.
- Let the website create and manage privilege codes without redeploying the Worker.
- Give trusted localization groups their own revocable codes.
- Track who uploaded, verified, deleted, or changed records.
- Avoid storing raw privilege codes in D1.

### Roles

- `master`: full access. Can create, revoke, rotate, promote, delete, hard-delete, and manage all privilege codes.
- `official_group`: can upload verified official-group translations and soft-delete or update records within allowed scopes.
- `reviewer`: can verify or unverify uploads, but cannot create admin codes.
- `uploader`: can upload manual translations, but does not auto-verify records.

### Scopes

- `upload:ai`
- `upload:manual`
- `upload:official`
- `record:verify`
- `record:delete:own`
- `record:delete:any`
- `record:nuke`
- `token:create`
- `token:revoke`
- `token:rotate`
- `audit:read`

### Storage Rules

- Store only a hash of each privilege code.
- Never store raw codes in D1.
- Show newly generated codes only once in the web UI.
- Use a Worker secret pepper such as `TOKEN_HASH_PEPPER`.
- Normalize codes before hashing.
- Add expiration dates and manual revocation.
- Add usage counts and last-used timestamps.

### Recommended D1 Table: `privilege_codes`

- `code_id`
- `code_hash`
- `label`
- `owner_name`
- `group_name`
- `role`
- `scopes_json`
- `is_active`
- `expires_at`
- `created_by`
- `created_at`
- `revoked_by`
- `revoked_at`
- `last_used_at`
- `usage_count`
- `notes`

### Recommended D1 Table: `privilege_code_events`

- `event_id`
- `code_id`
- `actor_code_id`
- `action`
- `ip_hash`
- `user_agent`
- `metadata_json`
- `created_at`

### Worker Auth Flow

1. Read token from `X-Admin-Token` or upload payload `AdminToken`.
2. If token matches the master bootstrap secret, grant `master`.
3. Otherwise hash the submitted token and look it up in `privilege_codes`.
4. Reject inactive, revoked, expired, or missing codes.
5. Check role and scopes before performing the action.
6. Update `last_used_at` and `usage_count`.
7. Write an audit event.

### Upload Behavior

- `master` and codes with `upload:official` can upload as `Official_Group`.
- Codes with `upload:manual` can upload as `Manual`.
- Normal users can upload as `AI_Auto` or `Manual` based on existing policy.
- Records uploaded with official privilege should set `IsVerified = 1`.
- Records uploaded without official privilege should set `IsVerified = 0`.

### Delete Behavior

- `record:delete:own`: can soft-delete records uploaded by the same code or group.
- `record:delete:any`: can soft-delete any record.
- `record:nuke`: can hard-delete all matching package/language records and R2 files. This should be master-only by default.
- All delete actions must be audited.

### Web UI For Privilege Codes

- Master-only page: "Privilege Codes".
- Create a code with label, group, role, scopes, expiration, and notes.
- Copy newly generated code once.
- Revoke, rotate, or disable a code.
- Show last used time, usage count, owner, group, and recent audit events.
- Filter codes by active, expired, revoked, role, or group.
- Optional: export code list without raw secrets.

### Security Notes

- Do not hardcode `MINI_666` or official group codes in source code long-term.
- Put the master bootstrap code in Worker secrets.
- If the code has ever been committed or shared publicly, rotate it.
- Prefer generated long random codes for distributed group codes.
- Keep human-friendly labels separate from the actual secret value.

## Suggested D1 Tables

### `translation_records`

- `record_id`
- `package_id`
- `language`
- `mod_name`
- `latest_version`
- `mod_last_updated`
- `target_mod_version`
- `translation_date`
- `translation_type`
- `is_verified`
- `is_smart_merged`
- `merged_ai_count`
- `author`
- `uploader_id_hash`
- `update_log`
- `r2_key`
- `file_sha256`
- `file_size`
- `download_count`
- `created_at`
- `updated_at`
- `deleted_at`

### `upload_events`

- `event_id`
- `record_id`
- `package_id`
- `language`
- `uploader_id_hash`
- `ip_hash`
- `user_agent`
- `status`
- `error_message`
- `created_at`

### `download_events`

- `event_id`
- `record_id`
- `package_id`
- `language`
- `ip_hash`
- `created_at`

### `moderation_actions`

- `action_id`
- `record_id`
- `admin_id`
- `action`
- `reason`
- `created_at`

### `privilege_codes`

- `code_id`
- `code_hash`
- `label`
- `owner_name`
- `group_name`
- `role`
- `scopes_json`
- `is_active`
- `expires_at`
- `created_by`
- `created_at`
- `revoked_by`
- `revoked_at`
- `last_used_at`
- `usage_count`
- `notes`

### `privilege_code_events`

- `event_id`
- `code_id`
- `actor_code_id`
- `action`
- `ip_hash`
- `user_agent`
- `metadata_json`
- `created_at`

## Useful Worker API Additions

- `GET /mods`
- `GET /mods/{packageId}`
- `GET /mods/{packageId}/records`
- `GET /records/{recordId}`
- `POST /records/{recordId}/verify`
- `POST /records/{recordId}/unverify`
- `GET /admin/audit-log`
- `GET /health`
- `GET /admin/privilege-codes`
- `POST /admin/privilege-codes`
- `PATCH /admin/privilege-codes/{codeId}`
- `POST /admin/privilege-codes/{codeId}/revoke`
- `POST /admin/privilege-codes/{codeId}/rotate`
- `GET /admin/privilege-codes/{codeId}/events`

## Code Needed To Plan The Website Accurately

Please provide sanitized versions of:

- Worker source code.
- D1 schema and migrations.
- `wrangler.toml` without secrets.
- R2 bucket key naming policy.
- Current admin token/auth logic.
- Current CORS and rate-limit logic.
- Example `/registry` response JSON.
- Example upload payload accepted by `/upload`.
- Any existing frontend or dashboard code.
