# RWMod Phase 1 Data Model

Version: 0.1 draft
Project: RWMod / RimNexus
Domain: rwmod.net
Backend: existing Cloudflare Worker + shared D1

## Purpose

This document defines the first RWMod catalog data model. The model is designed
to extend the current AI Translation Network D1 database without modifying or
deleting existing translation records, feedback reports, privilege codes, R2
files, or mod-facing API behavior.

RWMod Phase 1 focuses on the MVP catalog:

- searchable RimWorld mod pages
- localization status
- source links
- dependencies and compatibility notes
- performance impact reports
- guide/video/article links
- moderation and trust metadata

Full item, weapon, building, race, and technology wiki data is out of scope for
this phase.

## Locked Decisions

- Use the existing Cloudflare Worker and shared D1 database.
- Keep `/api/v1/registry` unchanged for AI Translation Core and the current web portal.
- Use `TranslationRegistry` as the first real data source for RWMod mod pages.
- Add new RWMod-specific tables with the `RWMod` prefix.
- Use anonymous public reports protected by Cloudflare Turnstile.
- Do not mirror, unzip, rehost, or redistribute third-party localization packs.

## Existing Data Source

`TranslationRegistry` remains the source of cloud translation records.

Fields already useful to RWMod:

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
- `TargetModVersion`
- `TranslationDate`
- `IsSmartMerged`
- `MergedAiCount`
- `UpdateLog`
- `DownloadCount`

RWMod catalog records should be seeded or refreshed from `TranslationRegistry`
by grouping on normalized `PackageId`.

## New Tables

The migration `cloudflare/worker/migrations/0004_add_rwmod_catalog_tables.sql`
adds these tables:

- `RWModMods`
- `RWModModSources`
- `RWModLocalizationStatus`
- `RWModDependencies`
- `RWModCompatibilityReports`
- `RWModPerformanceReports`
- `RWModGuideLinks`
- `RWModAliases`
- `RWModModerationEvents`

The migration must only create tables and indexes. It must not insert, update,
delete, drop, or alter existing production data.

## Table Responsibilities

### RWModMods

The canonical mod catalog table.

Primary key:

- `PackageId`: normalized lowercase package id.

Stores:

- display package id
- mod name
- author
- Workshop id
- supported RimWorld versions
- latest known source version
- public aggregate statuses for localization, compatibility, and performance
- trust and confidence metadata

This table is optimized for search results and the mobile mod detail first
screen.

### RWModModSources

External source links for a mod.

Examples:

- Steam Workshop page
- localization Workshop page
- GitHub repository
- official site
- author page
- localization group page
- wiki page

RWMod stores links and attribution only. It does not mirror unauthorized files.

### RWModLocalizationStatus

Localization status by package id and language.

This table can reference `TranslationRegistry.RecordId` for AI Translation Core
cloud records. Traditional localization packs should use `SourceUrl` and
attribution fields instead of file mirrors.

Allowed public status values:

- `complete`
- `mostly_complete`
- `partial`
- `missing`
- `outdated`
- `unknown`

### RWModDependencies

Dependency and load-order knowledge.

Examples:

- required dependency
- optional dependency
- load before
- load after
- incompatible dependency
- patch mod recommendation

Dependency rows are evidence records, not final drama labels.

### RWModCompatibilityReports

Reviewed or aggregated compatibility records.

Allowed report types:

- `missing_dependency`
- `wrong_load_order`
- `hard_conflict`
- `soft_conflict`
- `version_mismatch`
- `duplicate_feature`
- `red_error_pattern`
- `needs_patch_mod`

Public summaries must carry trust and confidence. A single unverified player
report must not become a high-confidence warning.

### RWModPerformanceReports

Performance impact records.

Allowed public impact values:

- `unknown`
- `light`
- `medium`
- `heavy`

Reports should preserve context such as RimWorld version, mod version, mod
count, pawn count, CPU model, and scenario. Public labels must stay professional.

### RWModGuideLinks

Optional guide, article, video, and tutorial links.

Guide links are useful in MVP but should not block catalog launch.

### RWModAliases

Search aliases for mod pages.

Examples:

- Traditional Chinese name
- Simplified Chinese name
- English alias
- abbreviation
- Workshop id
- author alias
- tag keyword

### RWModModerationEvents

Audit log for catalog moderation actions.

Examples:

- create
- update
- verify
- hide
- restore
- merge
- reject
- promote_report

This table should be used for RWMod catalog moderation, while existing
`ModerationActions` can continue to track translation record moderation.

## Trust Levels

All important public data should expose a trust level.

Allowed values:

- `official`
- `trusted_group`
- `rwmod_verified`
- `cloud_record`
- `player_report`
- `inferred`
- `unknown`

## Confidence Levels

Allowed values:

- `low`
- `medium`
- `high`

Rules:

- Default is `low`.
- Player reports start at `low`.
- Multiple matching reports can become `medium`.
- `high` requires moderator review, trusted group confirmation, or strong benchmark/evidence.

## Public Status Defaults

Unknown is the safe default. The frontend must never infer a safe or complete
state from missing data.

Defaults:

- localization: `unknown`
- compatibility: `unknown`
- performance: `unknown`
- trust: `unknown`
- confidence: `low`

## Report Flow

Phase 1 public reporting should use anonymous submission with Cloudflare
Turnstile.

Recommended first implementation:

1. `POST /api/v1/rwmod/reports` validates Turnstile.
2. The Worker writes the submission into existing `FeedbackReports`.
3. Moderators review the report in the existing feedback/admin workflow.
4. Validated reports can be promoted into `RWModCompatibilityReports`,
   `RWModPerformanceReports`, or `RWModLocalizationStatus`.

This keeps raw user reports separate from public catalog conclusions.

## API Shape

Existing endpoints remain unchanged:

- `GET /api/v1/registry`
- `GET /api/v1/feedback`
- `POST /api/v1/feedback`
- mod upload/download/delete endpoints

Phase 1 RWMod endpoints:

- `GET /api/v1/rwmod/mods?q=&language=&gameVersion=&limit=`
- `GET /api/v1/rwmod/mods/{packageId}`
- `GET /api/v1/rwmod/mods/{packageId}/localization`
- `GET /api/v1/rwmod/mods/{packageId}/compatibility`
- `GET /api/v1/rwmod/mods/{packageId}/performance`
- `POST /api/v1/rwmod/reports`

The mod detail endpoint should return enough above-the-fold data for mobile:

- mod name
- package id
- supported RimWorld versions
- localization aggregate
- performance aggregate
- compatibility aggregate
- primary source links

Implementation notes:

- Catalog reads prefer `RWModMods` and related `RWMod*` tables.
- If a package is not yet in `RWModMods`, read endpoints can still expose a
  safe starter page from `TranslationRegistry`.
- Registry fallback rows use `cloud_record` trust and low/medium confidence.
- Missing compatibility and performance records remain `unknown`.
- `POST /api/v1/rwmod/reports` requires the Worker secret
  `TURNSTILE_SECRET_KEY` and writes anonymous reports into `FeedbackReports`
  with RWMod metadata for moderation.

## Initial Seeding Strategy

Initial RWMod data can be seeded from `TranslationRegistry`:

- group rows by lowercase `PackageId`
- choose the newest non-deleted row as a display hint
- store `ModName`, `Author`, `TargetModVersion`, `LastUpdated`, and `ModLastUpdated`
- create `RWModLocalizationStatus` rows per language and package id
- store `TranslationRegistry.RecordId` in `RWModLocalizationStatus.RegistryRecordId`

No seed SQL is included in the schema migration. Seeding should be a separate
safe admin action or Worker maintenance route.

## Frontend Alignment

The schema supports the Phase 1 frontend design system:

- `RWModMods` powers search cards and mobile above-the-fold status.
- `RWModLocalizationStatus` powers localization chips and progress tiles.
- `RWModCompatibilityReports` powers warning blocks.
- `RWModPerformanceReports` powers performance impact tiles.
- `RWModModSources` and `RWModGuideLinks` power source/action panels.
- `RWModAliases` powers fast search and pasted identifiers.

## Safety Requirements

- Never expose unauthorized third-party localization downloads.
- Never mark missing compatibility data as safe.
- Never mark a single unverified report as high confidence.
- Never mutate existing `TranslationRegistry` rows during RWMod schema setup.
- Keep public labels professional and evidence-based.
