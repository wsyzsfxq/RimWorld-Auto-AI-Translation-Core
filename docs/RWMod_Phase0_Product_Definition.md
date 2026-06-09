# RWMod Phase 0 Product Definition

Version: 0.1 draft
Domain: rwmod.net
Status: Planning

## One-Line Vision

RWMod aims to become the first Chinese-language place RimWorld players check when they need to search mods, understand localization status, avoid compatibility traps, and diagnose heavily modded saves.

## Core Positioning

RWMod is a RimWorld mod knowledge and coordination platform. It is not just a landing page for AI Translation Core, and it is not a mirror site for other localization teams' files.

The first useful version should solve three concrete player problems:

- "Does this mod have Chinese localization, and where should I get it?"
- "Is this mod known to conflict with my setup or require special load order?"
- "Will this mod hurt performance in large colonies or heavy mod lists?"

## Product Boundaries

### RWMod Is

- A searchable RimWorld mod catalog.
- A localization status index for official, community, and cloud-assisted translations.
- A compatibility and load-order report hub.
- A performance-impact report hub.
- A bridge between players, localization groups, mod authors, and AI Translation Core cloud records.
- A neutral public entry point for `rwmod.net`.

### RWMod Is Not

- A replacement for Steam Workshop.
- A file mirror for third-party localization packs.
- A place to repackage or redistribute other teams' work without permission.
- A full Def/item/weapon/race wiki in the MVP.
- A general forum in the MVP.
- A site that declares accusations against mod authors based on unverified reports.

## Brand And Site Separation

RWMod should be a standalone site and product:

- `rwmod.net`: mod catalog, localization status, compatibility, performance, reports.
- AI Translation Core site or Web Portal: product page, cloud translation service, upload/download workflow, admin tools.

The two can share data and cross-link, but the public identity should remain separate. This keeps RWMod neutral enough for localization groups and other creators to participate.

## Target Users

### Normal Players

Players who want to know whether a mod has Chinese localization, whether a translation is current, and where to find the correct link.

### Heavy Modpack Players

Players running dozens or hundreds of mods who need dependency checks, known conflict notes, load-order hints, and performance warnings.

### Localization Groups

Groups or individuals who maintain traditional translation packs and want proper credit, traffic, and a stable place to show status.

### AI Translation Core Users

Players who use cloud-assisted localization and want live translation coverage, missing-key status, update logs, and correction feedback.

### Site Moderators

Trusted maintainers who verify reports, correct metadata, handle takedown requests, and prevent abuse.

## MVP Scope

The first public MVP should include:

- Search by mod name, package id, Workshop id, author, and tags.
- A mod detail page for each package id.
- Basic metadata: mod name, package id, Workshop id, author, supported RimWorld versions, update time.
- Localization status: official Chinese, community localization, AI/cloud-assisted translation, missing/translated key counts when available.
- Source links: Steam Workshop, localization Workshop page, official site, GitHub, author page.
- Compatibility summary: dependencies, known conflicts, load-order notes, report count, confidence level.
- Performance summary: light, medium, heavy, unknown, with report count and context.
- Related guide/video links as optional metadata, not as a launch blocker.
- Public report forms for localization issues, compatibility issues, and performance issues.
- Clear attribution and report/takedown links on every mod page.

## Explicit Non-MVP Items

These should wait until the catalog and report flow prove useful:

- Full item/weapon/building/race/technology wiki.
- Large tutorial article system.
- User profiles, badges, forums, private messages.
- Paid promotion, sponsorship placement, or hardware service ads.
- Automatic claim that AI translations are official or superior.
- Automatic conflict/performance labels without enough report context.

## Future Encyclopedia Expansion

The long-term RWMod goal is not only to list mods, but to become a structured
RimWorld mod encyclopedia. These features are intentionally deferred until the
catalog, report flow, and backend trust model are stable.

### Precision Homepage Portal

Future homepage versions should not show an indiscriminate flat wall of all
mods as the default experience. The homepage should become a search-led portal:

- Initial state: a clean central command view with the multifunction search box
  as the main stage.
- Search must support keywords, PackageId, Workshop ID, author, known aliases,
  and full Steam Workshop URLs.
- Add image-backed recommendation sections for:
  - Popular
  - Newly Indexed
- Add a category navigation matrix with fast filters for:
  - RimWorld versions such as 1.4, 1.5, and 1.6.
  - Content categories such as frameworks, story expansions, weapons,
    scenarios, QoL optimization, buildings/defenses, race/faction content,
    and performance tools.

### Advanced Mod Detail Page

Future mod detail pages should follow a four-layer structure:

1. Above-the-fold command zone:
   - Support game versions.
   - Steam Workshop direct link.
   - Localization progress/status.
   - Performance impact rating.
   - Compatibility and safety warning summary.
2. Mod Synergy Graph:
   - Interactive draggable topology graph.
   - Shows dependencies and synergy/extension relationships.
   - Separates verified relationships from reports or inferred metadata.
3. Micro Database:
   - Lists the concrete game content added by the mod.
   - Initial groups include weapons, equipment, buildings, defenses, traits,
     backgrounds, skills, abilities, races, factions, research, recipes,
     apparel, implants, incidents, quests, and scenario additions.
   - Each entry should support a detail view with values and source context.
4. Player Salon:
   - Dedicated discussion space for bug reports, localization help, play notes,
     and load-order experience.
   - Raw comments must not automatically become high-confidence public
     compatibility or performance conclusions.

### Phase Placement

- Phase 5 should focus on the precision homepage portal and recommendation
  matrix.
- Phase 6 should focus on the advanced mod detail structure and Mod Synergy
  Graph.
- Phase 7 should focus on the Micro Database and reviewed community knowledge
  expansion.

## Localization And Copyright Boundary

RWMod must be respectful to existing localization groups from day one.

Rules:

- Do not unzip, mirror, or rehost third-party localization packs unless explicitly authorized.
- Do not provide direct third-party `.zip` downloads from RWMod for someone else's localization work.
- Link players to the original Steam Workshop page, mod page, GitHub release, or localization group page.
- Clearly show attribution for translation groups and authors.
- Provide a correction/takedown channel for authors and localization groups.
- Mark source type clearly: official, community group, player report, AI Translation Core cloud record, inferred metadata.
- Never present cloud-assisted translations as the original author's official localization unless verified.

## Traditional Localization Vs Cloud Translation

RWMod should separate these concepts clearly:

### Traditional Localization Packs

These are static files or Workshop entries maintained by authors or localization groups. RWMod indexes them, credits them, links to them, and shows status where possible.

### RWMod / AI Translation Core Cloud Records

These are dynamic translation records uploaded through AI Translation Core or related cloud workflows. They can show current coverage, missing keys, target mod version, upload date, contributor, verification status, and smart-merge status.

The user-facing message should be:

"RWMod helps players find existing localization work and provides cloud-assisted coverage where traditional localization is missing, outdated, or incomplete."

## Performance Impact Reports

Performance information should be useful but fair.

Public labels:

- Unknown
- Light
- Medium
- Heavy

Avoid insulting labels on public pages. Internally, players can still describe pain points in notes, but the aggregated label should stay professional.

Report context should include, when available:

- RimWorld version
- Mod version or Workshop update date
- Approximate mod count
- Pawn count
- Colony age or save age
- CPU model
- Whether the issue appears at startup, map generation, normal gameplay, late game, raids, pathfinding, or world map ticks
- Whether removing the mod improved TPS/load time

RWMod should show confidence:

- Low: one or two unverified reports.
- Medium: several reports with similar context.
- High: repeated reports, moderator review, or benchmark evidence.

## Compatibility Reports

Compatibility reports should be evidence-based.

Report types:

- Missing dependency
- Wrong load order
- Hard conflict
- Soft conflict
- Version mismatch
- Duplicate feature overlap
- Red error log pattern
- Needs patch mod

Every public conflict label should show source and confidence. The site should avoid turning one angry report into a permanent verdict.

## Data Sources

Potential sources for Phase 1 and beyond:

- Existing AI Translation Core cloud registry.
- AI Translation Core local scanner/export output.
- Steam Workshop metadata by Workshop id.
- Player-submitted reports.
- Trusted localization group submissions.
- Moderator-curated corrections.

Existing cloud record fields that already matter:

- `PackageId`
- `ModName`
- `Language`
- `LatestVersion`
- `TargetModVersion`
- `ModLastUpdated`
- `TranslationType`
- `IsVerified`
- `IsSmartMerged`
- `MergedAiCount`
- `Author`
- `UploaderID`
- `UpdateLog`

## Trust Levels

Each visible data point should carry a source or confidence signal:

- Official: from mod author or official Workshop metadata.
- Trusted group: submitted or verified by a known localization group.
- RWMod verified: reviewed by a site moderator.
- Cloud record: uploaded through AI Translation Core.
- Player report: submitted by users and not yet verified.
- Inferred: derived from scanner or metadata and should be treated as tentative.

## Phase 0 Decisions

Locked for now:

- Build RWMod as a separate `rwmod.net` product.
- Keep AI Translation Core as a related product, not the whole identity of RWMod.
- Start with Traditional Chinese-first planning, with room for Simplified Chinese and English later.
- Make localization status, compatibility, and performance the first three public value pillars.
- Respect existing localization groups by linking and crediting instead of mirroring their files.
- Keep guide/video links optional in MVP.

Open questions:

- Chinese display name: `RWMod`, a Traditional Chinese title, or another name?
- Should the first backend reuse the existing `cloudflare/worker` D1 data model, or use a separate RWMod database with API links to the translation registry?
- Should public reports be anonymous at launch, or protected with Turnstile/login?
- Who can verify localization group identity in the first moderation model?
- Which RimWorld versions should be first-class at launch: 1.5, 1.6, or both?

## Phase 0 Exit Criteria

Phase 0 is complete when:

- The product boundary is agreed.
- The MVP scope is agreed.
- The copyright/localization-group boundary is agreed.
- The first data model list is ready for Phase 1.
- The initial deployment direction is chosen: Cloudflare Pages + Worker/D1, Supabase, or a separate self-hosted stack.

## Recommended Next Step

Move to Phase 1 by drafting the initial data schema:

- `mods`
- `mod_sources`
- `localization_status`
- `cloud_translation_records`
- `compatibility_reports`
- `performance_reports`
- `mod_dependencies`
- `guide_links`
- `moderation_events`
