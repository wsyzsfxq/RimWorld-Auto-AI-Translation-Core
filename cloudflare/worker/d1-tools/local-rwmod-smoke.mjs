import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { DatabaseSync } from "node:sqlite";
import worker from "../worker-v2.js";

const __dirname = dirname(fileURLToPath(import.meta.url));
const workerDir = join(__dirname, "..");
const migrationPath = join(workerDir, "migrations", "0004_add_rwmod_catalog_tables.sql");
const migrationSql = await readFile(migrationPath, "utf8");

class LocalD1Statement {
  constructor(db, sql, values = []) {
    this.db = db;
    this.sql = sql;
    this.values = values;
  }

  bind(...values) {
    return new LocalD1Statement(this.db, this.sql, values);
  }

  all() {
    const statement = this.db.prepare(this.sql);
    return {
      success: true,
      results: statement.all(...this.values),
      meta: {},
    };
  }

  first() {
    const statement = this.db.prepare(this.sql);
    return statement.get(...this.values) || null;
  }

  run() {
    const statement = this.db.prepare(this.sql);
    const info = statement.run(...this.values);
    return {
      success: true,
      meta: {
        changes: info.changes,
        last_row_id: Number(info.lastInsertRowid || 0),
      },
    };
  }
}

class LocalD1Database {
  constructor(db) {
    this.db = db;
  }

  prepare(sql) {
    return new LocalD1Statement(this.db, sql);
  }
}

function createDatabase() {
  const db = new DatabaseSync(":memory:");
  db.exec(migrationSql);
  db.exec(`
    CREATE TABLE IF NOT EXISTS TranslationRegistry (
      RecordId TEXT PRIMARY KEY,
      PackageId TEXT NOT NULL,
      ModName TEXT NOT NULL DEFAULT '',
      Author TEXT NOT NULL DEFAULT '',
      UploaderID TEXT NOT NULL DEFAULT '',
      GroupName TEXT NOT NULL DEFAULT '',
      OwnerName TEXT NOT NULL DEFAULT '',
      Language TEXT NOT NULL DEFAULT '',
      TranslationType TEXT NOT NULL DEFAULT '',
      IsVerified INTEGER NOT NULL DEFAULT 0,
      TargetModVersion TEXT NOT NULL DEFAULT 'Unknown',
      LatestVersion TEXT NOT NULL DEFAULT 'Unknown',
      ModLastUpdated TEXT,
      LastUpdated TEXT,
      TranslationDate TEXT,
      DownloadCount INTEGER NOT NULL DEFAULT 0,
      IsDeleted INTEGER NOT NULL DEFAULT 0,
      UpdateLog TEXT NOT NULL DEFAULT '',
      CreatedAt TEXT
    );
  `);
  seedDatabase(db);
  return db;
}

function seedDatabase(db) {
  const now = "2026-06-09T00:00:00.000Z";
  const mods = [
    ["brrainz.harmony", "brrainz.harmony", "Harmony", "Brrainz", "2009463077", ["1.5", "1.6"], "1.6", "partial", "warning", "light", "cloud_record", "low"],
    ["vanillaexpanded.vfe.core", "vanillaexpanded.vfe.core", "Vanilla Expanded Framework", "Vanilla Expanded", "2023507013", ["1.5", "1.6"], "1.6", "partial", "warning", "medium", "player_report", "medium"],
    ["rimthreaded.core", "rimthreaded.core", "RimThreaded", "Community", "2222907981", ["1.5"], "1.5", "missing", "conflict", "heavy", "player_report", "medium"],
    ["owlchemist.performanceoptimizer", "owlchemist.performanceoptimizer", "Performance Optimizer", "Owlchemist", "2664723367", ["1.5", "1.6"], "1.6", "unknown", "unknown", "unknown", "unknown", "low"],
  ];

  const insertMod = db.prepare(`
    INSERT INTO RWModMods
    (PackageId, DisplayPackageId, ModName, Summary, Author, PrimaryWorkshopId,
     SupportedGameVersionsJson, LatestKnownVersion, LastWorkshopUpdated,
     LastRegistryUpdated, LocalizationStatus, CompatibilityStatus,
     PerformanceImpact, TrustLevel, Confidence, IsListed, CreatedAt, UpdatedAt)
    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 1, ?, ?)
  `);

  for (const mod of mods) {
    const [
      packageId,
      displayPackageId,
      modName,
      author,
      workshopId,
      gameVersions,
      latestKnownVersion,
      localizationStatus,
      compatibilityStatus,
      performanceImpact,
      trustLevel,
      confidence,
    ] = mod;
    insertMod.run(
      packageId,
      displayPackageId,
      modName,
      `${modName} local smoke seed row.`,
      author,
      workshopId,
      JSON.stringify(gameVersions),
      latestKnownVersion,
      now,
      now,
      localizationStatus,
      compatibilityStatus,
      performanceImpact,
      trustLevel,
      confidence,
      now,
      now
    );
  }

  const insertSource = db.prepare(`
    INSERT INTO RWModModSources
    (SourceId, PackageId, SourceType, Url, Label, IsPrimary, TrustLevel, IsVisible, CreatedAt, UpdatedAt)
    VALUES (?, ?, 'steam', ?, 'Steam Workshop', 1, 'unknown', 1, ?, ?)
  `);
  for (const [packageId,,,, workshopId] of mods) {
    insertSource.run(`source:${packageId}:steam`, packageId, `https://steamcommunity.com/sharedfiles/filedetails/?id=${workshopId}`, now, now);
  }

  const insertLocalization = db.prepare(`
    INSERT INTO RWModLocalizationStatus
    (LocalizationId, PackageId, Language, LocaleLabel, Status, SourceType,
     TrustLevel, Confidence, RegistryRecordId, TranslationType, IsVerified,
     CoveragePercent, TargetModVersion, ContributorName, Notes, IsVisible,
     CreatedAt, UpdatedAt)
    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 1, ?, ?)
  `);
  insertLocalization.run(
    "loc:harmony:zh-hant",
    "brrainz.harmony",
    "ChineseTraditional",
    "ChineseTraditional",
    "partial",
    "ai_translation_core",
    "cloud_record",
    "low",
    "reg:harmony:zh-hant",
    "AI_Cloud",
    0,
    62,
    "1.6",
    "RWMod smoke",
    "Local smoke localization record.",
    now,
    now
  );
  insertLocalization.run(
    "loc:vfe:zh-hant",
    "vanillaexpanded.vfe.core",
    "ChineseTraditional",
    "ChineseTraditional",
    "partial",
    "ai_translation_core",
    "cloud_record",
    "low",
    null,
    "AI_Cloud",
    0,
    45,
    "1.6",
    "RWMod smoke",
    "Large framework smoke localization record.",
    now,
    now
  );

  const insertCompatibility = db.prepare(`
    INSERT INTO RWModCompatibilityReports
    (CompatibilityId, PackageId, RelatedPackageId, RelatedModName, ReportType,
     Severity, PublicStatus, GameVersion, Summary, Detail, ReporterName,
     SourceType, TrustLevel, Confidence, VoteCount, IsVerified, IsPublic,
     CreatedAt, UpdatedAt, ReviewedAt)
    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 1, ?, ?, ?)
  `);
  insertCompatibility.run(
    "compat:harmony:load-order",
    "brrainz.harmony",
    "",
    "",
    "wrong_load_order",
    "normal",
    "warning",
    "1.6",
    "Keep Harmony near the top before dependent mods.",
    "Local smoke warning row.",
    "RWMod smoke",
    "rwmod_seed",
    "rwmod_verified",
    "medium",
    3,
    1,
    now,
    now,
    now
  );
  insertCompatibility.run(
    "compat:rimthreaded:hard-conflict",
    "rimthreaded.core",
    "",
    "",
    "hard_conflict",
    "high",
    "conflict",
    "1.5",
    "High-risk heavily patched modlist candidate.",
    "Local smoke conflict row.",
    "RWMod smoke",
    "player_report",
    "player_report",
    "medium",
    8,
    0,
    now,
    now,
    now
  );

  const insertPerformance = db.prepare(`
    INSERT INTO RWModPerformanceReports
    (PerformanceId, PackageId, Impact, Severity, GameVersion, ModVersion,
     ModCount, PawnCount, Scenario, MetricType, Summary, Detail, ReporterName,
     SourceType, TrustLevel, Confidence, VoteCount, IsVerified, IsPublic,
     CreatedAt, UpdatedAt, ReviewedAt)
    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 1, ?, ?, ?)
  `);
  insertPerformance.run(
    "perf:harmony:light",
    "brrainz.harmony",
    "light",
    "normal",
    "1.6",
    "1.6",
    180,
    25,
    "midgame",
    "player_report",
    "Library mod. Runtime cost depends on dependent patches.",
    "Local smoke light row.",
    "RWMod smoke",
    "player_report",
    "player_report",
    "low",
    1,
    0,
    now,
    now,
    now
  );
  insertPerformance.run(
    "perf:vfe:medium",
    "vanillaexpanded.vfe.core",
    "medium",
    "normal",
    "1.6",
    "1.6",
    220,
    40,
    "startup",
    "player_report",
    "Large mod lists may report slower startup and patching.",
    "Local smoke medium row.",
    "RWMod smoke",
    "player_report",
    "player_report",
    "medium",
    4,
    0,
    now,
    now,
    now
  );
  insertPerformance.run(
    "perf:rimthreaded:heavy",
    "rimthreaded.core",
    "heavy",
    "high",
    "1.5",
    "1.5",
    260,
    60,
    "late_game",
    "player_report",
    "Can change tick behavior. Review modlist carefully before use.",
    "Local smoke heavy row.",
    "RWMod smoke",
    "player_report",
    "player_report",
    "medium",
    12,
    0,
    now,
    now,
    now
  );

  const insertAlias = db.prepare(`
    INSERT INTO RWModAliases
    (AliasId, PackageId, AliasText, AliasType, Language, SearchWeight, SourceType, CreatedAt)
    VALUES (?, ?, ?, ?, ?, ?, 'rwmod_seed', ?)
  `);
  insertAlias.run("alias:harmony:name", "brrainz.harmony", "Harmony", "name", "English", 90, now);
  insertAlias.run("alias:vfe:name", "vanillaexpanded.vfe.core", "VFE Core", "name", "English", 80, now);
  insertAlias.run("alias:perf:name", "owlchemist.performanceoptimizer", "Performance Optimizer", "name", "English", 80, now);

  const insertRegistry = db.prepare(`
    INSERT INTO TranslationRegistry
    (RecordId, PackageId, ModName, Author, UploaderID, GroupName, OwnerName,
     Language, TranslationType, IsVerified, TargetModVersion, LatestVersion,
     ModLastUpdated, LastUpdated, TranslationDate, DownloadCount, IsDeleted,
     UpdateLog, CreatedAt)
    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 0, ?, ?)
  `);
  insertRegistry.run(
    "reg:harmony:zh-hant",
    "brrainz.harmony",
    "Harmony",
    "Brrainz",
    "RWModSmoke",
    "",
    "",
    "ChineseTraditional",
    "AI_Cloud",
    0,
    "1.6",
    "1.6",
    now,
    now,
    now,
    42,
    "Registry fallback row for local smoke.",
    now
  );
}

async function requestJson(env, path) {
  const response = await worker.fetch(new Request(`https://rwmod.local${path}`), env, {
    waitUntil() {},
    passThroughOnException() {},
  });
  const bodyText = await response.text();
  let body = null;
  try {
    body = bodyText ? JSON.parse(bodyText) : null;
  } catch {
    body = bodyText;
  }
  assert.equal(response.status, 200, `${path} returned HTTP ${response.status}: ${bodyText}`);
  return body;
}

function inspectSchema(db) {
  const tableCount = db.prepare(`
    SELECT COUNT(*) AS Count
    FROM sqlite_master
    WHERE type = 'table' AND name LIKE 'RWMod%'
  `).get().Count;
  const indexCount = db.prepare(`
    SELECT COUNT(*) AS Count
    FROM sqlite_master
    WHERE type = 'index' AND name LIKE 'idx_rwmod%'
  `).get().Count;
  const workshopIndex = db.prepare(`
    SELECT name
    FROM sqlite_master
    WHERE type = 'index' AND name = 'idx_rwmod_mods_workshop'
  `).get();
  const workshopIndexColumns = db.prepare("PRAGMA index_info('idx_rwmod_mods_workshop')").all().map((row) => row.name);
  const workshopPlan = db.prepare(`
    EXPLAIN QUERY PLAN
    SELECT PackageId
    FROM RWModMods
    WHERE PrimaryWorkshopId = ?
  `).all("2009463077").map((row) => row.detail);

  assert.equal(tableCount, 9, "expected 9 RWMod tables");
  assert.equal(indexCount, 24, "expected 24 RWMod indexes");
  assert.ok(workshopIndex, "idx_rwmod_mods_workshop must exist");
  assert.deepEqual(workshopIndexColumns, ["PrimaryWorkshopId"], "workshop index should target PrimaryWorkshopId");
  assert.ok(
    workshopPlan.some((line) => line.includes("idx_rwmod_mods_workshop")),
    `workshop exact lookup should use idx_rwmod_mods_workshop: ${workshopPlan.join(" | ")}`
  );

  return {
    tableCount,
    indexCount,
    workshopIndex: workshopIndex.name,
    workshopIndexColumns,
    workshopExactLookupPlan: workshopPlan,
  };
}

async function main() {
  const db = createDatabase();
  const env = {
    DB: new LocalD1Database(db),
  };
  const schema = inspectSchema(db);

  const searchByWorkshop = await requestJson(env, "/api/v1/rwmod/mods?q=2009463077&limit=5");
  assert.equal(searchByWorkshop.Items[0]?.PackageId, "brrainz.harmony", "Workshop ID should return Harmony");

  const searchByName = await requestJson(env, "/api/v1/rwmod/mods?q=harmony&limit=5");
  assert.equal(searchByName.Items[0]?.PackageId, "brrainz.harmony", "Name search should return Harmony");

  const searchByVersion = await requestJson(env, "/api/v1/rwmod/mods?q=2023507013&gameVersion=1.6&limit=5");
  assert.equal(searchByVersion.Items[0]?.PackageId, "vanillaexpanded.vfe.core", "Workshop ID + version search should return VFE Core");

  const detail = await requestJson(env, "/api/v1/rwmod/mods/brrainz.harmony");
  assert.equal(detail.AboveFold.PackageId, "brrainz.harmony");
  assert.equal(detail.AboveFold.LocalizationStatus, "partial");
  assert.equal(detail.AboveFold.CompatibilityStatus, "warning");
  assert.equal(detail.AboveFold.PerformanceImpact, "light");
  assert.equal(detail.Sources[0]?.Url, "https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077");

  const rimthreadedPerformance = await requestJson(env, "/api/v1/rwmod/mods/rimthreaded.core/performance");
  assert.equal(rimthreadedPerformance.Impact, "heavy");
  assert.equal(rimthreadedPerformance.Items[0]?.Impact, "heavy");

  const optimizerCompatibility = await requestJson(env, "/api/v1/rwmod/mods/owlchemist.performanceoptimizer/compatibility");
  assert.equal(optimizerCompatibility.Status, "unknown");
  assert.equal(optimizerCompatibility.Count, 0);

  console.log(JSON.stringify({
    ok: true,
    schema,
    api: {
      searchByWorkshop: {
        query: "2009463077",
        count: searchByWorkshop.Count,
        first: searchByWorkshop.Items[0]?.PackageId,
      },
      searchByName: {
        query: "harmony",
        count: searchByName.Count,
        first: searchByName.Items[0]?.PackageId,
      },
      searchByVersion: {
        query: "2023507013",
        gameVersion: "1.6",
        count: searchByVersion.Count,
        first: searchByVersion.Items[0]?.PackageId,
      },
      detailAboveFold: detail.AboveFold,
      rimthreadedPerformance: {
        impact: rimthreadedPerformance.Impact,
        count: rimthreadedPerformance.Count,
      },
      optimizerCompatibility: {
        status: optimizerCompatibility.Status,
        count: optimizerCompatibility.Count,
      },
    },
  }, null, 2));
}

await main();
