# RWMod Frontend Design System And UX Rules

Project: RWMod / RimNexus
Chinese subtitle: Bianyuan Mailuo
Domain: rwmod.net
Phase: 1 - Design Standards And Data Model
Status: Draft 0.1

## Design North Star

RWMod should feel like a high-end starship tactical control dashboard for RimWorld mod intelligence.

The interface must be dark-mode first, dense, precise, and fast. It should communicate that the site is a serious mod database for searching localization status, compatibility risks, performance impact, and modlist diagnostics.

The highest aesthetic standard is readable operational data. Decorative visuals are allowed only when they improve scanning, hierarchy, or state recognition.

## Non-Negotiable UX Priorities

1. Search is the primary action.
2. Mobile readability is the primary quality gate.
3. Critical mod status must be visible above the fold.
4. Data density must stay high without becoming visually noisy.
5. Warning states must be impossible to miss.
6. Reporting must be quick and must not interrupt browsing.
7. Animation must never slow down scanning, input, or navigation.

## Theme And Aesthetics

### Visual Scene

The full site should evoke a precise starship tactical HUD:

- Deep space dark background.
- Clean geometric alignment.
- Thin instrument-like dividers.
- Controlled glow on status indicators only.
- Flat, disciplined data surfaces.
- Strong contrast between core facts, secondary metadata, and warnings.

Do not create a marketing landing page look. RWMod is a working database and diagnostic console, not a product brochure.

### Color Tokens

Base colors:

- `--color-bg-space`: `#0a0f1d`
- `--color-bg-deck`: `#0f1726`
- `--color-bg-panel`: `#141d2c`
- `--color-bg-card`: `#1a2333`
- `--color-bg-card-strong`: `#202b3e`
- `--color-border-soft`: `#2b3950`
- `--color-border-strong`: `#3d5272`

Text colors:

- `--color-text-primary`: `#eef6ff`
- `--color-text-secondary`: `#a9b8cc`
- `--color-text-muted`: `#71839c`
- `--color-text-disabled`: `#4f6178`

Primary technology colors:

- `--color-tech-blue`: `#2f8cff`
- `--color-tech-blue-soft`: `#73b7ff`
- `--color-cyan-signal`: `#22d3ee`

State colors:

- `--color-safe`: `#35e68a`
- `--color-safe-dim`: `#1f8f5b`
- `--color-info`: `#2f8cff`
- `--color-warning`: `#ffb020`
- `--color-danger`: `#ff4d4f`
- `--color-critical`: `#ff2a2a`
- `--color-unknown`: `#7c8da6`

Use color sparingly. The background and cards should stay restrained; bright colors are for status, focus, charts, and urgent signals.

### Color Usage Rules

- Blue means interactive, selected, scan-ready, or primary.
- Green means complete, safe, verified, no known conflicts, or healthy.
- Orange means degraded, medium risk, moderate performance cost, or needs review.
- Red means conflict, heavy performance impact, broken version, missing dependency, or high-risk warning.
- Gray means unknown, unverified, unavailable, or insufficient data.

Never rely on color alone. Every status must also have a text label or icon.

### Background Rules

- The page background must be `#0a0f1d` or a near-equivalent deep blue-black.
- Large gradients, orbs, bokeh, decorative glow blobs, and heavy animated space scenes are forbidden.
- Subtle linear depth is allowed only in fixed interface surfaces, such as headers or dashboards.
- Use grid lines only if extremely faint and non-distracting.

### Surface Rules

Use a restrained three-layer surface model:

- Page background: `#0a0f1d`
- Work surface or dashboard band: `#0f1726`
- Data cards and panels: `#1a2333`

Cards must have a border. Borders should be more visible than shadows.

Shadows are allowed only for overlays, dialogs, sticky headers, and menus.

## Typography

### Font Stack

Use a system-first stack for speed and readability:

```css
font-family: Inter, "Noto Sans TC", "Microsoft JhengHei", "PingFang TC", system-ui, sans-serif;
```

Monospace data such as package ids, Workshop ids, hashes, and version strings:

```css
font-family: "JetBrains Mono", "Cascadia Mono", Consolas, monospace;
```

### Type Scale

Mobile first:

- Page title: `24px`, line-height `32px`
- Section title: `18px`, line-height `26px`
- Card title: `16px`, line-height `24px`
- Body: `14px`, line-height `22px`
- Dense metadata: `12px`, line-height `18px`
- Micro label: `11px`, line-height `16px`

Desktop can increase page title and key dashboard numbers, but do not scale text with viewport width.

### Text Rules

- Letter spacing must be `0`.
- Avoid all-caps paragraphs.
- All IDs and exact keys must use monospace.
- Do not truncate critical warnings.
- Long package ids may wrap, but must remain selectable and copyable.
- Never let text overlap badges, buttons, or neighboring cards.

## Spacing And Layout

### 8px Grid

All spacing must follow an 8px grid:

- `4px` allowed only for tight inline gaps.
- `8px` for compact spacing.
- `16px` for normal card padding and field groups.
- `24px` for section separation.
- `32px` for major layout separation.
- `48px` for page-level breaks.

### Container Width

- Mobile: full width with `16px` side padding.
- Tablet: `24px` side padding.
- Desktop: max content width `1280px`, centered.
- Data-heavy pages may use full width up to `1440px`, but must keep readable columns.

### Radius

- Buttons: `6px`
- Cards: `8px`
- Inputs: `6px`
- Pills/badges: `999px` only for tiny status chips.
- Large rounded decorative cards are forbidden.

### Borders

Use 1px borders for cards, tables, inputs, panels, and toolbar elements.

Border color should normally be `#2b3950`; selected states can use `#2f8cff`.

## Mobile-First UX Rules

### Primary Quality Gate

Every core page must be designed and tested first at:

- `360 x 740`
- `390 x 844`
- `430 x 932`

Desktop layouts are enhancements, not the baseline.

### Above-The-Fold Requirement

On a mobile mod detail page, the first screen must show:

- Mod name.
- PackageId.
- RimWorld version support.
- Localization completion status.
- Performance impact.
- Conflict/dependency warning summary.
- Primary action to copy PackageId or open source link.

The user must not need to scroll to learn whether the mod is safe, translated, heavy, or risky.

### Search As The Soul Of The Site

The search box must be central and prominent on:

- Home page.
- Catalog page.
- Modlist analyzer.
- Mobile sticky header after scrolling.

Search requirements:

- Supports mod name, package id, Workshop id, author, tag, and known alias.
- Shows results quickly while typing.
- Has a paste button on mobile.
- Has a clear button.
- Supports keyboard submit.
- Allows copy/paste of full modlist text into analyzer mode.
- Keeps the last query visible after navigation.

### Mobile Navigation

Use a compact top header and optional bottom action bar:

- Header: logo/name, search trigger, menu.
- Bottom action bar: search, catalog, analyzer, reports.

Do not hide search inside a deep menu.

### Touch Targets

- Minimum touch target: `44px`.
- Icon-only buttons must have tooltip text on desktop and accessible labels.
- Critical actions must not be placed too close to destructive actions.

## Page-Level UX Requirements

### Home / Search Page

Purpose: get the player to an answer immediately.

Required first screen:

- RWMod identity.
- Large search input.
- Mode selector: Mods, Modlist, Reports.
- Fast status shortcuts: Translated, No known conflict, Heavy performance, Needs Chinese.
- Recently updated or popular mods may appear below the first screen.

Do not build a hero marketing page. The search experience is the hero.

Future Phase 5 homepage rule:

- The homepage must not default to a flat all-mod wall once the catalog grows.
- The initial state remains a clean search-led command view.
- The primary search box remains the visual and interaction center.
- Search supports keywords, PackageId, Workshop ID, author, known aliases, and
  full Steam Workshop URLs.
- Below the search command zone, add a visual recommendation matrix with
  image-backed cards for Popular and Newly Indexed.
- Add a nine-cell tag navigation matrix for fast entry into version and content
  filters.
- Version entries should include current first-class RimWorld versions such as
  1.4, 1.5, and 1.6 as data allows.
- Content entries should include framework/library mods, story expansions,
  weapons/equipment, scenarios/storytellers, QoL optimization,
  buildings/defenses, race/faction content, performance tools, and
  translation/localization helpers.
- Recommendation and category cards must remain compact data cards, not
  marketing tiles.

### Mod Detail Page

Required structure:

1. Status header.
2. Key facts grid.
3. Localization panel.
4. Compatibility panel.
5. Performance panel.
6. Source links.
7. Reports and corrections.
8. Optional guide/video links.

First screen must prioritize conclusions over history.

Future Phase 6/7 advanced detail structure:

1. Above-the-fold command zone:
   - Mod name, PackageId, and supported RimWorld versions.
   - Steam Workshop direct link.
   - Localization progress/status.
   - Performance impact rating.
   - Compatibility and safety warning summary.
   - Primary copy/open/report actions.
2. Mod Synergy Graph:
   - Draggable, interactive topology graph for dependencies and mod
     relationships.
   - Distinguish required dependencies, optional dependencies, incompatible
     dependencies, load-order notes, and synergy/extension relationships.
   - Use color, labels, and line styles together; never encode graph meaning by
     color alone.
   - Unknown or unverified graph edges must remain visually tentative.
3. Micro Database:
   - Dense indexed lists of game entities added by the mod.
   - Initial groups: weapons/equipment, buildings/defenses, traits/backgrounds,
     skills/abilities, races/factions, recipes, research, apparel, implants,
     incidents, quests, and scenario additions.
   - Each entity row should expose a compact summary and open a detail panel or
     page with exact values and source context.
   - Tables must support filtering, sorting, and compact mobile views.
4. Player Salon:
   - Comment/discussion area for bug reports, localization help, play notes,
     modlist advice, and load-order experience.
   - Raw player comments are community context, not verified catalog truth.
   - Promote only reviewed evidence into public compatibility, performance, or
     localization conclusions.

### Modlist Analyzer

Purpose: paste a mod list and immediately see risks.

Required UX:

- Large paste area.
- One primary analyze button.
- Results grouped by severity.
- Missing dependencies and hard conflicts first.
- Performance-heavy mods second.
- Localization gaps third.
- Copy/share result action.

Do not require login to run basic analysis.

### Report Flow

Report buttons must open a compact sheet or dialog:

- Type: localization, compatibility, performance, metadata correction.
- Minimal required fields.
- Optional evidence details.
- Submit without leaving the mod detail page.

The form must never erase the user's current browsing state.

## Component Rules

### Data Cards

Use data cards for repeated mod summaries and compact status clusters.

Required anatomy:

- Title row with mod name and primary status.
- PackageId or Workshop id in monospace.
- 2 to 4 key metrics.
- Source/trust indicator.
- One primary action.

Data cards must not be nested inside other cards.

### Status Chips

Status chips should be compact and readable:

- Complete
- Partial
- Missing
- Verified
- Player report
- Conflict
- Heavy
- Unknown

Chips must include a text label, not just an icon.

### Compact Tables

Use compact tables for high-density lists:

- Compatibility entries.
- Dependencies.
- Localization records.
- Analyzer results.

Table rules:

- Sticky header on desktop if the table is long.
- Horizontal scroll allowed on mobile only when columns cannot collapse cleanly.
- Severity column should be visually locked to the left where possible.
- Rows must have clear hover/focus states on desktop.

### Warning Blocks

Critical conflict warnings must be visually dominant:

- Red border or left rail.
- Short severity title.
- Plain-language explanation.
- Source and confidence.
- Action hint when known.

Example:

```text
High conflict risk
Missing dependency: Harmony.
Source: 12 player reports, 2 moderator verified.
Action: install Harmony before this mod.
```

### Metric Tiles

Use metric tiles for:

- Localization coverage.
- Missing key count.
- Conflict report count.
- Performance impact.
- Last updated.

Metric tiles should be stable in size and never jump when data loads.

### Inputs

Inputs must feel like instrument fields:

- Dark surface.
- 1px border.
- Blue focus ring.
- Clear placeholder.
- Dedicated clear icon.
- Paste icon where paste is a primary action.

### Buttons

Button hierarchy:

- Primary: blue filled.
- Secondary: outlined blue/gray.
- Safety: green accent.
- Warning: orange.
- Dangerous: red.
- Ghost: icon or low-emphasis utility.

Use icons for repeated tool actions such as copy, paste, clear, search, open link, filter, sort, and report.

## Status Semantics

### Localization Status

Allowed public labels:

- Complete
- Mostly complete
- Partial
- Missing
- Outdated
- Unknown

Show source:

- Official
- Community group
- AI Translation Core
- Player report
- RWMod verified

### Compatibility Status

Allowed public labels:

- No known conflict
- Needs dependency
- Load-order note
- Soft conflict
- Hard conflict
- Version mismatch
- Unknown

### Performance Status

Allowed public labels:

- Unknown
- Light
- Medium
- Heavy

Avoid mocking or insulting wording on public pages. Use notes for detailed player experience, but keep the aggregate label professional.

### Trust And Confidence

Every important status should show trust:

- Verified
- Trusted group
- Cloud record
- Player report
- Inferred
- Unknown

Confidence:

- Low
- Medium
- High

## Accessibility Rules

- Body text contrast must meet WCAG AA where practical.
- Focus states must be visible on every interactive element.
- Color must never be the only status signal.
- All icon buttons need accessible labels.
- Keyboard users must be able to search, open results, filter, and submit reports.
- Motion-sensitive users must not be forced through animated effects.

## Performance Rules

- No heavy decorative animation.
- No autoplay video backgrounds.
- No canvas/WebGL background for primary pages.
- Prefer static CSS and lightweight components.
- Load only visible data first.
- Virtualize very long result lists where needed.
- Keep mobile interaction responsive before adding visual polish.

## Data Loading Rules

Use skeletons only for layout-stable elements. Avoid pulsing full-page loaders.

Loading states:

- Search: inline spinner or small HUD pulse inside input.
- Cards: fixed-height skeleton rows.
- Tables: fixed row placeholders.
- Errors: explicit message with retry action.

Never show a success/safe state before data is actually loaded.

## Empty And Error States

Empty search:

- Show recent/popular mods and a clear hint about searchable fields.

No result:

- Offer report missing mod, paste Workshop URL, or submit package id.

API error:

- Show the API state and retry.
- Keep cached local results if available.

Unknown mod:

- Do not invent status. Mark data as unknown and invite contribution.

## Prohibited Patterns

The frontend must not use:

- Light-mode-first implementation.
- Marketing-style hero page as the main experience.
- Decorative gradient blobs, orbs, or bokeh backgrounds.
- Excessive glassmorphism.
- Oversized cards with sparse content.
- Long explanatory text blocks inside the app UI.
- Animation that delays search, filtering, or report submission.
- Warning labels that insult mod authors or create drama.
- Color-only status indicators.
- Nested cards.
- Layout shifts caused by loading text, badges, or hover states.

## Implementation Tokens

Recommended CSS variables:

```css
:root {
  --space-1: 4px;
  --space-2: 8px;
  --space-3: 16px;
  --space-4: 24px;
  --space-5: 32px;
  --space-6: 48px;

  --radius-control: 6px;
  --radius-card: 8px;
  --radius-pill: 999px;

  --color-bg-space: #0a0f1d;
  --color-bg-deck: #0f1726;
  --color-bg-panel: #141d2c;
  --color-bg-card: #1a2333;
  --color-bg-card-strong: #202b3e;
  --color-border-soft: #2b3950;
  --color-border-strong: #3d5272;

  --color-text-primary: #eef6ff;
  --color-text-secondary: #a9b8cc;
  --color-text-muted: #71839c;
  --color-text-disabled: #4f6178;

  --color-tech-blue: #2f8cff;
  --color-tech-blue-soft: #73b7ff;
  --color-cyan-signal: #22d3ee;
  --color-safe: #35e68a;
  --color-safe-dim: #1f8f5b;
  --color-info: #2f8cff;
  --color-warning: #ffb020;
  --color-danger: #ff4d4f;
  --color-critical: #ff2a2a;
  --color-unknown: #7c8da6;
}
```

## First-Phase Design Deliverables

Before building the real frontend, Phase 1 should produce:

1. Mobile-first wireframe for home/search.
2. Mobile-first wireframe for mod detail above-the-fold.
3. Desktop layout for catalog search results.
4. Component inventory:
   - Search bar
   - Data card
   - Status chip
   - Metric tile
   - Warning block
   - Compact table
   - Report sheet
5. Data model aligned to the visible UI states.

## Acceptance Checklist

A RWMod frontend screen passes design review only if:

- It is usable at `360px` width.
- Search is visible or one tap away.
- Core mod risk/status information is visible above the fold on detail pages.
- Status colors match the defined semantics.
- Warnings are impossible to miss but still professional.
- Text does not overlap, overflow, or become unreadable.
- Cards are not nested.
- The screen feels like a precise tactical dashboard, not a decorative sci-fi poster.
