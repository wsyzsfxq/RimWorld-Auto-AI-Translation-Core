import { createServer } from "node:http";
import { readFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(__dirname, "..", "..");
const workerDir = path.join(repoRoot, "cloudflare", "worker");
const runtimeEnv = typeof process === "undefined" ? {} : process.env;
const runtimeArgs = typeof process === "undefined" ? [] : process.argv.slice(2);
const port = Number(runtimeEnv.PORT || 8794);
const host = runtimeEnv.HOST || "127.0.0.1";
const backend = runtimeArgs.includes("--worker")
  ? "worker"
  : String(runtimeEnv.RWMOD_PREVIEW_BACKEND || "mock").toLowerCase();
const useWorkerBackend = backend === "worker";
const contentTypes = new Map([
  [".html", "text/html; charset=utf-8"],
  [".css", "text/css; charset=utf-8"],
  [".js", "text/javascript; charset=utf-8"],
  [".json", "application/json; charset=utf-8"],
]);

const fixture = JSON.parse(await readFile(path.join(__dirname, "mock-api-data.json"), "utf8"));
const mods = fixture.Items || [];
let workerRuntime = null;

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

function normalize(value) {
  return String(value || "").trim().toLowerCase();
}

function normalizePackageId(value) {
  return normalize(value).replace(/\s+/g, "");
}

function cleanIdPart(value) {
  return normalizePackageId(value).replace(/[^a-z0-9_.-]+/g, "-") || "unknown";
}

function safeText(value, fallback = "") {
  const text = String(value ?? "").trim();
  return text || fallback;
}

function trustLevel(value, fallback = "unknown") {
  const normalized = normalize(value);
  return [
    "official",
    "trusted_group",
    "rwmod_verified",
    "cloud_record",
    "player_report",
    "inferred",
    "unknown",
  ].includes(normalized) ? normalized : fallback;
}

function confidence(value, fallback = "low") {
  const normalized = normalize(value);
  return ["low", "medium", "high"].includes(normalized) ? normalized : fallback;
}

function extractWorkshopId(value) {
  const raw = String(value || "").trim();
  if (!raw) return "";

  const queryMatch = raw.match(/\b[?&]?id=(\d{6,})\b/i);
  if (queryMatch) return queryMatch[1];

  if (/^\d{6,}$/.test(raw)) return raw;

  const urlText = /^https?:\/\//i.test(raw) ? raw : `https://${raw}`;
  try {
    const parsed = new URL(urlText);
    const isSteamWorkshop = parsed.hostname.toLowerCase().endsWith("steamcommunity.com")
      && /\/(?:sharedfiles|workshop)\/filedetails/i.test(parsed.pathname);
    const id = parsed.searchParams.get("id") || "";
    if (isSteamWorkshop && /^\d{6,}$/.test(id)) return id;
  } catch {
    // Preview search should stay forgiving for non-URL catalog queries.
  }

  return "";
}

function json(res, value, status = 200) {
  res.writeHead(status, {
    "Content-Type": "application/json; charset=utf-8",
    "Access-Control-Allow-Origin": "*",
  });
  res.end(JSON.stringify(value));
}

function notFound(res) {
  res.writeHead(404, { "Content-Type": "text/plain; charset=utf-8" });
  res.end("Not found");
}

function badRequest(res, message) {
  json(res, { success: false, error: message }, 400);
}

async function readJsonBody(req) {
  const chunks = [];
  for await (const chunk of req) chunks.push(chunk);
  const text = Buffer.concat(chunks).toString("utf8");
  return text ? JSON.parse(text) : {};
}

async function readRequestBody(req) {
  const chunks = [];
  for await (const chunk of req) chunks.push(chunk);
  return chunks.length ? Buffer.concat(chunks) : null;
}

function findMod(packageId) {
  const key = normalizePackageId(packageId);
  return mods.find((mod) => normalizePackageId(mod.PackageId) === key);
}

function listMods(url) {
  const q = normalize(url.searchParams.get("q"));
  const packageQuery = normalizePackageId(url.searchParams.get("q"));
  const workshopId = extractWorkshopId(url.searchParams.get("q"));
  const language = normalize(url.searchParams.get("language"));
  const gameVersion = normalize(url.searchParams.get("gameVersion"));
  const limit = Math.min(100, Math.max(1, Number(url.searchParams.get("limit") || 50)));

  const filtered = mods.filter((mod) => {
    const haystack = [
      mod.PackageId,
      mod.DisplayPackageId,
      mod.PrimaryWorkshopId,
      mod.ModName,
      mod.Author,
      mod.LocalizationStatus,
      mod.CompatibilityStatus,
      mod.PerformanceImpact,
    ].map(normalize).join(" ");
    const identifierMatch = !q
      || normalizePackageId(mod.PackageId) === packageQuery
      || normalize(mod.PrimaryWorkshopId) === packageQuery
      || (workshopId && normalize(mod.PrimaryWorkshopId) === workshopId);
    const languageMatch = !language || (mod.Localization || []).some((row) => normalize(row.Language) === language);
    const versionMatch = !gameVersion || (mod.SupportedGameVersions || []).some((version) => normalize(version).includes(gameVersion));
    return (identifierMatch || haystack.includes(q) || (workshopId && haystack.includes(workshopId))) && languageMatch && versionMatch;
  }).slice(0, limit);

  return {
    Items: filtered,
    Count: filtered.length,
    Limit: limit,
    Query: q,
    Language: language,
    GameVersion: gameVersion,
    Defaults: unknownDefaults(),
  };
}

async function createWorkerRuntime() {
  const [{ DatabaseSync }, workerModule] = await Promise.all([
    import("node:sqlite"),
    import(pathToFileURL(path.join(workerDir, "worker-v2.js")).href),
  ]);
  const schemaSql = await readFile(path.join(workerDir, "schema.sql"), "utf8");
  const db = new DatabaseSync(":memory:");
  db.exec(schemaSql);
  seedWorkerPreviewDatabase(db);
  return {
    worker: workerModule.default,
    env: {
      DB: new LocalD1Database(db),
      BUCKET: createLocalBucket(),
      RWMOD_LOCAL_PREVIEW: "1",
      TURNSTILE_SECRET_KEY: "local-preview-only",
      TOKEN_HASH_PEPPER: "local-preview-only",
      MASTER_SECRET: "local-preview-only",
    },
  };
}

function createLocalBucket() {
  const objects = new Map();
  return {
    async get(key) {
      const object = objects.get(key);
      if (!object) return null;
      return {
        body: object.body,
        httpMetadata: object.httpMetadata || {},
      };
    },
    async put(key, body, options = {}) {
      objects.set(key, {
        body,
        httpMetadata: options.httpMetadata || {},
      });
      return null;
    },
    async delete(key) {
      objects.delete(key);
    },
  };
}

function seedWorkerPreviewDatabase(db) {
  const now = "2026-06-09T00:00:00.000Z";
  seedFixtureMods(db, now);
  seedRegistryOnlyRows(db, now);
}

function seedFixtureMods(db, now) {
  const insertMod = db.prepare(`
    INSERT INTO RWModMods
    (PackageId, DisplayPackageId, ModName, Summary, Author, PrimaryWorkshopId,
     SupportedGameVersionsJson, LatestKnownVersion, LastWorkshopUpdated,
     LastRegistryUpdated, LocalizationStatus, CompatibilityStatus,
     PerformanceImpact, TrustLevel, Confidence, IsListed, CreatedAt, UpdatedAt)
    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 1, ?, ?)
  `);
  const insertSource = db.prepare(`
    INSERT INTO RWModModSources
    (SourceId, PackageId, SourceType, Url, Label, SourceOwner, Language,
     IsPrimary, TrustLevel, IsVisible, CreatedAt, UpdatedAt)
    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, 1, ?, ?)
  `);
  const insertLocalization = db.prepare(`
    INSERT INTO RWModLocalizationStatus
    (LocalizationId, PackageId, Language, LocaleLabel, Status, SourceType,
     TrustLevel, Confidence, RegistryRecordId, TranslationType, IsVerified,
     CoveragePercent, TargetModVersion, SourceUrl, ContributorName, Notes,
     IsVisible, CreatedAt, UpdatedAt)
    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 1, ?, ?)
  `);
  const insertCompatibility = db.prepare(`
    INSERT INTO RWModCompatibilityReports
    (CompatibilityId, PackageId, RelatedPackageId, RelatedModName, ReportType,
     Severity, PublicStatus, GameVersion, ModVersion, ErrorPattern, Summary,
     Detail, EvidenceUrl, ReporterName, SourceType, TrustLevel, Confidence,
     VoteCount, IsVerified, IsPublic, CreatedAt, UpdatedAt, ReviewedAt)
    VALUES (?, ?, '', '', ?, ?, ?, ?, '', '', ?, ?, '', ?, ?, ?, ?, 0, ?, 1, ?, ?, ?)
  `);
  const insertPerformance = db.prepare(`
    INSERT INTO RWModPerformanceReports
    (PerformanceId, PackageId, Impact, Severity, GameVersion, ModVersion,
     ModCount, PawnCount, Scenario, MetricType, Summary, Detail, ReporterName,
     SourceType, TrustLevel, Confidence, VoteCount, IsVerified, IsPublic,
     CreatedAt, UpdatedAt, ReviewedAt)
    VALUES (?, ?, ?, ?, ?, '', NULL, NULL, 'preview', 'player_report', ?, ?, ?, ?, ?, ?, 0, ?, 1, ?, ?, ?)
  `);
  const insertAlias = db.prepare(`
    INSERT INTO RWModAliases
    (AliasId, PackageId, AliasText, AliasType, Language, SearchWeight, SourceType, CreatedAt)
    VALUES (?, ?, ?, ?, ?, ?, 'rwmod_preview_seed', ?)
  `);
  const insertRegistry = db.prepare(`
    INSERT INTO TranslationRegistry
    (RecordId, PackageId, Language, ModName, LatestVersion, LastUpdated,
     ModLastUpdated, UploaderID, Author, TranslationType, IsVerified, FileUrl,
     TargetModVersion, TranslationDate, IsSmartMerged, MergedAiCount, UpdateLog,
     IsDeleted, DownloadCount, CreatedAt, UpdatedAt)
    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 0, 0, ?, 0, ?, ?, ?)
  `);

  for (const mod of mods) {
    const packageId = normalizePackageId(mod.PackageId);
    const versions = Array.isArray(mod.SupportedGameVersions) ? mod.SupportedGameVersions : [];
    insertMod.run(
      packageId,
      safeText(mod.DisplayPackageId, packageId),
      safeText(mod.ModName, packageId),
      safeText(mod.Summary, `${safeText(mod.ModName, packageId)} local Worker-backed preview row.`),
      safeText(mod.Author),
      safeText(mod.PrimaryWorkshopId),
      JSON.stringify(versions),
      safeText(mod.LatestKnownVersion, versions.at(-1) || "Unknown"),
      now,
      now,
      normalize(mod.LocalizationStatus) || "unknown",
      normalize(mod.CompatibilityStatus) || "unknown",
      normalize(mod.PerformanceImpact) || "unknown",
      trustLevel(mod.TrustLevel),
      confidence(mod.Confidence),
      now,
      now
    );

    (mod.Sources || []).forEach((source, index) => {
      insertSource.run(
        `fixture:${packageId}:source:${index + 1}`,
        packageId,
        safeText(source.SourceType, "other"),
        safeText(source.Url),
        safeText(source.Label, "Source"),
        safeText(source.SourceOwner),
        safeText(source.Language),
        index === 0 ? 1 : 0,
        trustLevel(source.TrustLevel),
        now,
        now
      );
    });

    (mod.Localization || []).forEach((row, index) => {
      const language = safeText(row.Language, "Unknown");
      const registryRecordId = `fixture:${packageId}:registry:${cleanIdPart(language)}:${index + 1}`;
      insertLocalization.run(
        `fixture:${packageId}:loc:${cleanIdPart(language)}:${index + 1}`,
        packageId,
        language,
        safeText(row.LocaleLabel, language),
        normalize(row.Status) || "unknown",
        safeText(row.SourceType, "unknown"),
        trustLevel(row.TrustLevel),
        confidence(row.Confidence),
        registryRecordId,
        safeText(row.TranslationType, row.SourceType === "official" ? "Official_Group" : "AI_Cloud"),
        trustLevel(row.TrustLevel) === "official" ? 1 : 0,
        row.CoveragePercent ?? null,
        safeText(row.TargetModVersion, mod.LatestKnownVersion || "Unknown"),
        safeText(row.SourceUrl),
        safeText(row.ContributorName, row.SourceType === "official" ? "Official localization" : "RWMod preview"),
        safeText(row.Notes, "Local Worker-backed preview localization row."),
        now,
        now
      );
      insertRegistry.run(
        registryRecordId,
        packageId,
        language,
        safeText(mod.ModName, packageId),
        safeText(mod.LatestKnownVersion, "Unknown"),
        now,
        now,
        safeText(row.ContributorName, "RWModPreview"),
        safeText(mod.Author, "RWMod Preview"),
        safeText(row.TranslationType, row.SourceType === "official" ? "Official_Group" : "AI_Cloud"),
        trustLevel(row.TrustLevel) === "official" ? 1 : 0,
        `${registryRecordId}.zip`,
        safeText(row.TargetModVersion, mod.LatestKnownVersion || "Unknown"),
        now,
        safeText(row.Notes, "Local Worker-backed preview registry row."),
        Number(mod.TotalDownloadCount || 0),
        now,
        now
      );
    });

    (mod.Compatibility || []).forEach((row, index) => {
      const publicStatus = normalize(row.PublicStatus) || "unknown";
      insertCompatibility.run(
        `fixture:${packageId}:compat:${index + 1}`,
        packageId,
        safeText(row.ReportType, "soft_conflict"),
        publicStatus === "conflict" ? "high" : "normal",
        publicStatus,
        safeText(row.GameVersion, mod.LatestKnownVersion || ""),
        safeText(row.Summary, "Local Worker-backed preview compatibility row."),
        safeText(row.Detail, row.Summary || ""),
        safeText(row.ReporterName, "RWMod preview"),
        safeText(row.SourceType, "rwmod_preview_seed"),
        trustLevel(row.TrustLevel, "player_report"),
        confidence(row.Confidence),
        trustLevel(row.TrustLevel) === "rwmod_verified" ? 1 : 0,
        now,
        now,
        now
      );
    });

    (mod.Performance || []).forEach((row, index) => {
      const impact = normalize(row.Impact) || "unknown";
      insertPerformance.run(
        `fixture:${packageId}:perf:${index + 1}`,
        packageId,
        impact,
        impact === "heavy" ? "high" : "normal",
        safeText(row.GameVersion, mod.LatestKnownVersion || ""),
        safeText(row.Summary, "Local Worker-backed preview performance row."),
        safeText(row.Detail, row.Summary || ""),
        safeText(row.ReporterName, "RWMod preview"),
        safeText(row.SourceType, "rwmod_preview_seed"),
        trustLevel(row.TrustLevel, "player_report"),
        confidence(row.Confidence),
        trustLevel(row.TrustLevel) === "rwmod_verified" || trustLevel(row.TrustLevel) === "official" ? 1 : 0,
        now,
        now,
        now
      );
    });

    [
      [mod.ModName, "name", "English", 90],
      [mod.DisplayPackageId || mod.PackageId, "package_id", "", 100],
      [mod.PrimaryWorkshopId, "workshop_id", "", 70],
    ].forEach(([alias, aliasType, language, weight], index) => {
      if (!alias) return;
      insertAlias.run(
        `fixture:${packageId}:alias:${index + 1}`,
        packageId,
        String(alias),
        aliasType,
        language,
        weight,
        now
      );
    });
  }
}

function seedRegistryOnlyRows(db, now) {
  db.prepare(`
    INSERT INTO TranslationRegistry
    (RecordId, PackageId, Language, ModName, LatestVersion, LastUpdated,
     ModLastUpdated, UploaderID, Author, TranslationType, IsVerified, FileUrl,
     TargetModVersion, TranslationDate, IsSmartMerged, MergedAiCount, UpdateLog,
     IsDeleted, DownloadCount, CreatedAt, UpdatedAt)
    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 0, 0, ?, 0, ?, ?, ?)
  `).run(
    "fixture:registry-only:zh-hant",
    "rwmod.registry.seedonly",
    "ChineseTraditional",
    "Registry Seed Only",
    "1.6",
    now,
    now,
    "RWModPreview",
    "RWMod Preview",
    "AI_Cloud",
    0,
    "fixture-registry-only.zip",
    "1.6",
    now,
    "Registry-only row proves TranslationRegistry fallback before full catalog seeding.",
    7,
    now,
    now
  );
}

async function proxyWorkerRequest(req, res, url) {
  const body = await readRequestBody(req);
  const headers = new Headers();
  for (const [key, value] of Object.entries(req.headers)) {
    if (Array.isArray(value)) {
      value.forEach((item) => headers.append(key, item));
    } else if (value !== undefined) {
      headers.set(key, value);
    }
  }

  const init = {
    method: req.method,
    headers,
  };
  if (body && !["GET", "HEAD"].includes(req.method || "")) {
    init.body = body;
  }

  const workerUrl = `https://rwmod.local${url.pathname}${url.search}`;
  const response = await workerRuntime.worker.fetch(new Request(workerUrl, init), workerRuntime.env, {
    waitUntil() {},
    passThroughOnException() {},
  });

  const responseHeaders = Object.fromEntries(response.headers.entries());
  res.writeHead(response.status, responseHeaders);
  const arrayBuffer = await response.arrayBuffer();
  res.end(Buffer.from(arrayBuffer));
}

function unknownDefaults() {
  return {
    LocalizationStatus: "unknown",
    CompatibilityStatus: "unknown",
    PerformanceImpact: "unknown",
    TrustLevel: "unknown",
    Confidence: "low",
  };
}

async function serveStatic(req, res, url) {
  let pathname = decodeURIComponent(url.pathname);
  if (pathname === "/") pathname = "/index.html";
  const filePath = path.normalize(path.join(__dirname, pathname));
  if (!filePath.startsWith(path.normalize(__dirname))) {
    res.writeHead(403, { "Content-Type": "text/plain; charset=utf-8" });
    res.end("Forbidden");
    return;
  }

  try {
    const data = await readFile(filePath);
    res.writeHead(200, { "Content-Type": contentTypes.get(path.extname(filePath)) || "application/octet-stream" });
    res.end(data);
  } catch {
    notFound(res);
  }
}

workerRuntime = useWorkerBackend ? await createWorkerRuntime() : null;

const server = createServer(async (req, res) => {
  const url = new URL(req.url || "/", `http://${host}:${port}`);

  try {
    if (req.method === "OPTIONS") {
      res.writeHead(204, {
        "Access-Control-Allow-Origin": "*",
        "Access-Control-Allow-Methods": "GET, POST, OPTIONS",
        "Access-Control-Allow-Headers": "Content-Type",
      });
      res.end();
      return;
    }

    if (useWorkerBackend && url.pathname.startsWith("/api/")) {
      await proxyWorkerRequest(req, res, url);
      return;
    }

    if (req.method === "GET" && url.pathname === "/api/v1/rwmod/mods") {
      json(res, listMods(url));
      return;
    }

    const detailMatch = url.pathname.match(/^\/api\/v1\/rwmod\/mods\/([^/]+)$/);
    if (req.method === "GET" && detailMatch) {
      const mod = findMod(decodeURIComponent(detailMatch[1]));
      if (!mod) {
        notFound(res);
        return;
      }
      json(res, {
        ...mod,
        AboveFold: {
          PackageId: mod.PackageId,
          DisplayPackageId: mod.DisplayPackageId,
          ModName: mod.ModName,
          SupportedGameVersions: mod.SupportedGameVersions || [],
          LocalizationStatus: mod.LocalizationStatus,
          CompatibilityStatus: mod.CompatibilityStatus,
          PerformanceImpact: mod.PerformanceImpact,
          TrustLevel: mod.TrustLevel,
          Confidence: mod.Confidence,
        },
        Defaults: unknownDefaults(),
      });
      return;
    }

    const childMatch = url.pathname.match(/^\/api\/v1\/rwmod\/mods\/([^/]+)\/(localization|compatibility|performance)$/);
    if (req.method === "GET" && childMatch) {
      const mod = findMod(decodeURIComponent(childMatch[1]));
      if (!mod) {
        notFound(res);
        return;
      }
      const key = childMatch[2] === "localization"
        ? "Localization"
        : childMatch[2] === "compatibility"
          ? "Compatibility"
          : "Performance";
      json(res, {
        PackageId: mod.PackageId,
        Items: mod[key] || [],
        Count: (mod[key] || []).length,
      });
      return;
    }

    if (req.method === "POST" && url.pathname === "/api/v1/rwmod/reports") {
      let body;
      try {
        body = await readJsonBody(req);
      } catch {
        badRequest(res, "Invalid JSON body");
        return;
      }
      const reportKind = normalize(body.reportKind || body.kind || body.type || "other");
      const packageId = normalizePackageId(body.packageId);
      const detail = String(body.detail || body.body || body.summary || "").trim();
      if (!packageId && reportKind !== "missing_mod") {
        badRequest(res, "PackageId is required");
        return;
      }
      if (detail.length < 12) {
        badRequest(res, "Report detail is too short");
        return;
      }
      json(res, {
        success: true,
        feedbackId: `preview-${Date.now()}`,
        status: "open",
        reportKind,
        trustLevel: "player_report",
        confidence: "low",
        publicWarning: "Reports require review before they become RWMod public conclusions.",
      }, 201);
      return;
    }

    await serveStatic(req, res, url);
  } catch (err) {
    json(res, {
      success: false,
      error: err && err.message ? err.message : String(err),
    }, 500);
  }
});

server.listen(port, host, () => {
  const mode = useWorkerBackend ? "Worker-backed D1 preview" : "mock preview";
  console.log(`RWMod ${mode} server: http://${host}:${port}`);
});
