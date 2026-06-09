// @ts-nocheck
// AI Translation Network Cloud Worker v2
// Paste-friendly version for Cloudflare Workers dashboard.
//
// Required bindings:
// - env.DB: D1 database
// - env.BUCKET: R2 bucket
//
// Required secrets:
// - MASTER_SECRET
// - TOKEN_HASH_PEPPER
//
// Optional migration secret:
// - LEGACY_OFFICIAL_SECRETS: comma-separated old official codes

const MAX_UPLOAD_SIZE = 30 * 1024 * 1024;
const ENABLE_UPLOAD_HISTORY_CLEANUP = true;
const ENABLE_SCHEDULED_PURGE = true;
const ENABLE_EVENT_LOGS = true;
const ENABLE_DOWNLOAD_COUNT = true;
const REGISTRY_CACHE_TTL_SECONDS = 300;
const MAX_ATTACHMENTS_PER_REQUEST = 5;
const MAX_ATTACHMENT_SIZE = 6 * 1024 * 1024;
const MAX_ATTACHMENT_TOTAL_SIZE = 12 * 1024 * 1024;

const FEEDBACK_CATEGORIES = [
  "bug",
  "optimization",
  "translation_wrong",
  "missing_mod",
  "compatibility",
  "upload_issue",
  "feature_request",
  "other",
];

const FEEDBACK_STATUSES = [
  "open",
  "triage",
  "investigating",
  "planned",
  "fixed",
  "duplicate",
  "closed",
];

const FEEDBACK_SEVERITIES = ["low", "normal", "high", "critical"];
const APPLICATION_STATUSES = ["open", "reviewing", "approved", "rejected", "issued", "closed"];
const RWMOD_LOCALIZATION_STATUSES = ["complete", "mostly_complete", "partial", "missing", "outdated", "unknown"];
const RWMOD_PERFORMANCE_IMPACTS = ["unknown", "light", "medium", "heavy"];
const RWMOD_CONFIDENCE_LEVELS = ["low", "medium", "high"];
const RWMOD_TRUST_LEVELS = [
  "official",
  "trusted_group",
  "rwmod_verified",
  "cloud_record",
  "player_report",
  "inferred",
  "unknown",
];
const RWMOD_REPORT_KINDS = ["compatibility", "performance", "localization", "source", "guide", "missing_mod", "other"];

const CORS_HEADERS = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Methods": "GET, POST, PATCH, DELETE, OPTIONS",
  "Access-Control-Allow-Headers": "Content-Type, Authorization, X-Admin-Token",
};

const ROLE_SCOPES = {
  master: [
    "upload:ai",
    "upload:manual",
    "upload:official",
    "record:verify",
    "record:delete:own",
    "record:delete:any",
    "record:nuke",
    "token:create",
    "token:revoke",
    "token:rotate",
    "audit:read",
    "feedback:moderate",
    "feedback:read_private",
    "application:read_private",
    "application:moderate",
    "application:issue",
  ],
  official_group: ["upload:official", "record:delete:own"],
  reviewer: [
    "record:verify",
    "audit:read",
    "feedback:moderate",
    "feedback:read_private",
    "application:read_private",
    "application:moderate",
  ],
  uploader: ["upload:manual"],
};

export default {
  async fetch(request, env, ctx) {
    if (request.method === "OPTIONS") {
      return createResponse(null, 204, CORS_HEADERS);
    }

    const url = new URL(request.url);
    const path = url.pathname;
    const method = request.method;

    try {
      if (path === "/api/v1/health" && method === "GET") {
        return json({ ok: true, now: new Date().toISOString() });
      }

      if (path === "/api/v1/registry" && method === "GET") {
        return await handleRegistry(request, env, url, ctx);
      }

      if (path === "/api/v1/rwmod/mods" && method === "GET") {
        return await handleRWModListMods(env, url);
      }

      const rwmodDetailMatch = path.match(/^\/api\/v1\/rwmod\/mods\/([^/]+)$/);
      if (rwmodDetailMatch && method === "GET") {
        return await handleRWModGetMod(env, rwmodDetailMatch[1]);
      }

      const rwmodLocalizationMatch = path.match(/^\/api\/v1\/rwmod\/mods\/([^/]+)\/localization$/);
      if (rwmodLocalizationMatch && method === "GET") {
        return await handleRWModLocalization(env, rwmodLocalizationMatch[1], url);
      }

      const rwmodCompatibilityMatch = path.match(/^\/api\/v1\/rwmod\/mods\/([^/]+)\/compatibility$/);
      if (rwmodCompatibilityMatch && method === "GET") {
        return await handleRWModCompatibility(env, rwmodCompatibilityMatch[1], url);
      }

      const rwmodPerformanceMatch = path.match(/^\/api\/v1\/rwmod\/mods\/([^/]+)\/performance$/);
      if (rwmodPerformanceMatch && method === "GET") {
        return await handleRWModPerformance(env, rwmodPerformanceMatch[1], url);
      }

      if (path === "/api/v1/rwmod/reports" && method === "POST") {
        return await handleRWModCreateReport(request, env);
      }

      if (path === "/api/v1/feedback" && method === "GET") {
        return await handleListPublicFeedback(env, url);
      }

      if (path === "/api/v1/feedback" && method === "POST") {
        return await handleCreateFeedback(request, env);
      }

      const publicFeedbackMatch = path.match(/^\/api\/v1\/feedback\/([^/]+)$/);
      if (publicFeedbackMatch && method === "GET") {
        return await handleGetPublicFeedback(env, publicFeedbackMatch[1]);
      }

      const feedbackVoteMatch = path.match(/^\/api\/v1\/feedback\/([^/]+)\/vote$/);
      if (feedbackVoteMatch && method === "POST") {
        return await handleVoteFeedback(request, env, feedbackVoteMatch[1]);
      }

      if (path === "/api/v1/group-key-applications" && method === "POST") {
        return await handleCreateGroupKeyApplication(request, env);
      }

      const publicApplicationMatch = path.match(/^\/api\/v1\/group-key-applications\/([^/]+)$/);
      if (publicApplicationMatch && method === "GET") {
        return await handleGetPublicGroupKeyApplication(env, publicApplicationMatch[1]);
      }

      if (path.startsWith("/api/v1/download/") && method === "GET") {
        return await handleDownload(request, env, url);
      }

      if (path === "/api/v1/upload" && method === "POST") {
        return await handleUpload(request, env);
      }

      if (path.startsWith("/api/v1/delete/") && method === "DELETE") {
        return await handleDelete(request, env, url);
      }

      if (path === "/api/v1/admin/privilege-codes" && method === "GET") {
        const auth = await authorizeRequest(request, env, "audit:read");
        return await handleListPrivilegeCodes(env, auth, url);
      }

      if (path === "/api/v1/admin/privilege-codes" && method === "POST") {
        const auth = await authorizeRequest(request, env, "token:create");
        return await handleCreatePrivilegeCode(request, env, auth);
      }

      if (path === "/api/v1/admin/privilege-code-usage" && method === "GET") {
        const auth = await authorizeRequest(request, env, "audit:read");
        return await handleRecentPrivilegeCodeUsage(env, auth, url);
      }

      if (path === "/api/v1/admin/feedback" && method === "GET") {
        const auth = await authorizeRequest(request, env, "feedback:read_private");
        return await handleListAdminFeedback(env, auth, url);
      }

      const adminFeedbackEventsMatch = path.match(/^\/api\/v1\/admin\/feedback\/([^/]+)\/events$/);
      if (adminFeedbackEventsMatch && method === "GET") {
        const auth = await authorizeRequest(request, env, "feedback:read_private");
        return await handleFeedbackEvents(env, auth, adminFeedbackEventsMatch[1], url);
      }

      const adminFeedbackMatch = path.match(/^\/api\/v1\/admin\/feedback\/([^/]+)$/);
      if (adminFeedbackMatch && method === "GET") {
        const auth = await authorizeRequest(request, env, "feedback:read_private");
        return await handleGetAdminFeedback(env, auth, adminFeedbackMatch[1]);
      }

      if (adminFeedbackMatch && method === "PATCH") {
        const auth = await authorizeRequest(request, env, "feedback:moderate");
        return await handleUpdateFeedback(request, env, auth, adminFeedbackMatch[1]);
      }

      if (path === "/api/v1/admin/group-key-applications" && method === "GET") {
        const auth = await authorizeRequest(request, env, "application:read_private");
        return await handleListAdminGroupKeyApplications(env, auth, url);
      }

      const adminApplicationIssueMatch = path.match(/^\/api\/v1\/admin\/group-key-applications\/([^/]+)\/issue-key$/);
      if (adminApplicationIssueMatch && method === "POST") {
        const auth = await authorizeRequest(request, env, "application:issue");
        return await handleIssueGroupKeyApplication(request, env, auth, adminApplicationIssueMatch[1]);
      }

      const adminApplicationEventsMatch = path.match(/^\/api\/v1\/admin\/group-key-applications\/([^/]+)\/events$/);
      if (adminApplicationEventsMatch && method === "GET") {
        const auth = await authorizeRequest(request, env, "application:read_private");
        return await handleGroupKeyApplicationEvents(env, auth, adminApplicationEventsMatch[1], url);
      }

      const adminApplicationMatch = path.match(/^\/api\/v1\/admin\/group-key-applications\/([^/]+)$/);
      if (adminApplicationMatch && method === "GET") {
        const auth = await authorizeRequest(request, env, "application:read_private");
        return await handleGetAdminGroupKeyApplication(env, auth, adminApplicationMatch[1]);
      }

      if (adminApplicationMatch && method === "PATCH") {
        const auth = await authorizeRequest(request, env, "application:moderate");
        return await handleUpdateGroupKeyApplication(request, env, auth, adminApplicationMatch[1]);
      }

      const adminAttachmentMatch = path.match(/^\/api\/v1\/admin\/attachments\/([^/]+)$/);
      if (adminAttachmentMatch && method === "GET") {
        const auth = await authorizeRequest(request, env, null, url);
        return await handleDownloadAttachment(env, auth, adminAttachmentMatch[1]);
      }

      const revokeMatch = path.match(/^\/api\/v1\/admin\/privilege-codes\/([^/]+)\/revoke$/);
      if (revokeMatch && method === "POST") {
        const auth = await authorizeRequest(request, env, "token:revoke");
        return await handleRevokePrivilegeCode(request, env, auth, revokeMatch[1]);
      }

      const pauseMatch = path.match(/^\/api\/v1\/admin\/privilege-codes\/([^/]+)\/pause$/);
      if (pauseMatch && method === "POST") {
        const auth = await authorizeRequest(request, env, "token:rotate");
        return await handlePausePrivilegeCode(request, env, auth, pauseMatch[1]);
      }

      const resumeMatch = path.match(/^\/api\/v1\/admin\/privilege-codes\/([^/]+)\/resume$/);
      if (resumeMatch && method === "POST") {
        const auth = await authorizeRequest(request, env, "token:rotate");
        return await handleResumePrivilegeCode(request, env, auth, resumeMatch[1]);
      }

      const eventsMatch = path.match(/^\/api\/v1\/admin\/privilege-codes\/([^/]+)\/events$/);
      if (eventsMatch && method === "GET") {
        const auth = await authorizeRequest(request, env, "audit:read");
        return await handlePrivilegeCodeEvents(env, auth, eventsMatch[1], url);
      }

      const codeMatch = path.match(/^\/api\/v1\/admin\/privilege-codes\/([^/]+)$/);
      if (codeMatch && method === "GET") {
        const auth = await authorizeRequest(request, env, "audit:read");
        return await handleGetPrivilegeCode(env, auth, codeMatch[1]);
      }

      if (codeMatch && method === "PATCH") {
        const auth = await authorizeRequest(request, env, "token:rotate");
        return await handleUpdatePrivilegeCode(request, env, auth, codeMatch[1]);
      }

      return text("Not Found", 404);
    } catch (err) {
      const normalized = normalizeError(err);
      const errorStatus = normalized.status;
      return json({ error: normalized.message }, errorStatus);
    }
  },

  async scheduled(event, env, ctx) {
    if (!ENABLE_SCHEDULED_PURGE) return;
    await purgeSoftDeletedRecords(env);
  },
};

async function handleRegistry(request, env, url, ctx) {
  const bypassCache = url.searchParams.get("fresh") === "1" || url.searchParams.get("cache") === "bypass";
  const cacheKey = new Request(`${url.origin}${url.pathname}?v=public-registry-v1`, request);
  const cache = caches.default;

  if (!bypassCache) {
    const cached = await cache.match(cacheKey);
    if (cached) {
      const headers = new Headers(cached.headers);
      headers.set("X-Registry-Cache", "HIT");
      return createResponse(cached.body, cached.status, headers);
    }
  }

  const { results } = await env.DB.prepare(
    "SELECT * FROM TranslationRegistry WHERE IsDeleted IS NOT 1 ORDER BY LastUpdated DESC"
  ).all();

  const formatted = (results || []).map((r) => ({
    ...r,
    IsVerified: r.IsVerified === 1,
    IsSmartMerged: r.IsSmartMerged === 1,
    ContributorName: publicContributorName(r),
    ContributorKind: publicContributorKind(r),
  }));

  const headers = {
    "Content-Type": "application/json; charset=utf-8",
    "Cache-Control": `public, max-age=${REGISTRY_CACHE_TTL_SECONDS}`,
    "CDN-Cache-Control": `max-age=${REGISTRY_CACHE_TTL_SECONDS}`,
    "X-Registry-Cache": bypassCache ? "BYPASS" : "MISS",
    ...CORS_HEADERS,
  };
  const response = createResponse(JSON.stringify(formatted), 200, headers);
  if (!bypassCache && REGISTRY_CACHE_TTL_SECONDS > 0) {
    ctx.waitUntil(cache.put(cacheKey, response.clone()));
  }
  return response;
}

async function handleRWModListMods(env, url) {
  const query = cleanText(url.searchParams.get("q"), 120);
  const language = cleanText(url.searchParams.get("language"), 80);
  const gameVersion = cleanText(url.searchParams.get("gameVersion"), 40);
  const limit = clampInt(url.searchParams.get("limit"), 1, 100, 50);
  const items = [];
  const seen = new Set();

  const catalogRows = await queryRWModCatalogRows(env, { query, language, gameVersion, limit });
  for (const row of catalogRows) {
    const item = formatRWModListRow(row, "rwmod_catalog");
    if (!item.PackageId || seen.has(item.PackageId)) continue;
    seen.add(item.PackageId);
    items.push(item);
  }

  if (items.length < limit) {
    const registryRows = await queryRWModRegistryFallbackRows(env, {
      query,
      language,
      gameVersion,
      limit: limit - items.length,
      excludePackageIds: [...seen],
    });

    for (const row of registryRows) {
      const item = formatRWModRegistryListRow(row);
      if (!item.PackageId || seen.has(item.PackageId)) continue;
      seen.add(item.PackageId);
      items.push(item);
    }
  }

  return json({
    Items: items,
    Count: items.length,
    Limit: limit,
    Query: query,
    Language: language,
    GameVersion: gameVersion,
    Defaults: rwmodUnknownDefaults(),
  });
}

async function queryRWModCatalogRows(env, filters) {
  const where = ["m.IsListed = 1"];
  const values = [];

  if (filters.query) {
    const like = `%${filters.query.slice(0, 120)}%`;
    where.push(`(
      m.PackageId LIKE ?
      OR m.DisplayPackageId LIKE ?
      OR m.PrimaryWorkshopId LIKE ?
      OR m.ModName LIKE ?
      OR m.Author LIKE ?
      OR EXISTS (
        SELECT 1 FROM RWModAliases a
        WHERE a.PackageId = m.PackageId AND a.AliasText LIKE ?
      )
    )`);
    values.push(like, like, like, like, like, like);
  }

  if (filters.language) {
    where.push(`EXISTS (
      SELECT 1 FROM RWModLocalizationStatus ls
      WHERE ls.PackageId = m.PackageId AND ls.IsVisible = 1 AND ls.Language = ?
    )`);
    values.push(filters.language);
  }

  if (filters.gameVersion) {
    where.push("(m.SupportedGameVersionsJson LIKE ? OR m.LatestKnownVersion LIKE ?)");
    const like = `%${filters.gameVersion}%`;
    values.push(like, like);
  }

  const { results } = await env.DB.prepare(`
    SELECT
      m.PackageId,
      m.DisplayPackageId,
      m.ModName,
      m.Summary,
      m.Author,
      m.PrimaryWorkshopId,
      m.SupportedGameVersionsJson,
      m.LatestKnownVersion,
      m.LastWorkshopUpdated,
      m.LastRegistryUpdated,
      m.LocalizationStatus,
      m.CompatibilityStatus,
      m.PerformanceImpact,
      m.TrustLevel,
      m.Confidence,
      m.UpdatedAt,
      (
        SELECT COUNT(*)
        FROM RWModLocalizationStatus ls
        WHERE ls.PackageId = m.PackageId AND ls.IsVisible = 1
      ) AS LocalizationCount,
      (
        SELECT COUNT(*)
        FROM RWModCompatibilityReports cr
        WHERE cr.PackageId = m.PackageId AND cr.IsPublic = 1
      ) AS CompatibilityReportCount,
      (
        SELECT COUNT(*)
        FROM RWModPerformanceReports pr
        WHERE pr.PackageId = m.PackageId AND pr.IsPublic = 1
      ) AS PerformanceReportCount
    FROM RWModMods m
    WHERE ${where.join(" AND ")}
    ORDER BY COALESCE(m.LastRegistryUpdated, m.UpdatedAt, m.CreatedAt) DESC, m.ModName ASC
    LIMIT ?
  `).bind(...values, filters.limit).all();

  return results || [];
}

async function queryRWModRegistryFallbackRows(env, filters) {
  const registryColumns = await getTranslationRegistryColumnSet(env);
  const downloadCountProjection = registryColumns.has("DownloadCount") ? "" : "0 AS DownloadCount,";
  const totalDownloadCountProjection = registryColumns.has("DownloadCount")
    ? "SUM(DownloadCount) OVER (PARTITION BY lower(PackageId)) AS TotalDownloadCount"
    : "0 AS TotalDownloadCount";
  const where = ["IsDeleted IS NOT 1"];
  const values = [];

  if (filters.query) {
    const like = `%${filters.query.slice(0, 120)}%`;
    where.push("(PackageId LIKE ? OR ModName LIKE ? OR Author LIKE ? OR UploaderID LIKE ?)");
    values.push(like, like, like, like);
  }

  if (filters.language) {
    where.push("Language = ?");
    values.push(filters.language);
  }

  if (filters.gameVersion) {
    const like = `%${filters.gameVersion}%`;
    where.push("(TargetModVersion LIKE ? OR LatestVersion LIKE ?)");
    values.push(like, like);
  }

  const excluded = (filters.excludePackageIds || [])
    .map((value) => normalizePackageId(value))
    .filter(Boolean);
  if (excluded.length) {
    where.push(`lower(PackageId) NOT IN (${excluded.map(() => "?").join(",")})`);
    values.push(...excluded);
  }

  const { results } = await env.DB.prepare(`
    WITH ranked AS (
      SELECT
        *,
        ${downloadCountProjection}
        lower(PackageId) AS PackageKey,
        ROW_NUMBER() OVER (
          PARTITION BY lower(PackageId)
          ORDER BY IsVerified DESC, LastUpdated DESC, TranslationDate DESC, RecordId DESC
        ) AS rn,
        MAX(LastUpdated) OVER (PARTITION BY lower(PackageId)) AS LastRegistryUpdated,
        COUNT(*) OVER (PARTITION BY lower(PackageId)) AS RegistryRecordCount,
        ${totalDownloadCountProjection}
      FROM TranslationRegistry
      WHERE ${where.join(" AND ")}
    )
    SELECT *
    FROM ranked
    WHERE rn = 1
    ORDER BY LastRegistryUpdated DESC, ModName ASC
    LIMIT ?
  `).bind(...values, filters.limit).all();

  return results || [];
}

async function handleRWModGetMod(env, rawPackageId) {
  const packageId = normalizePackageId(rawPackageId);
  if (!packageId) return text("Invalid package id", 400);

  const catalog = await getRWModCatalogRow(env, packageId);
  const registry = await getRWModLatestRegistryRow(env, packageId);
  if (!catalog && !registry) return text("RWMod mod not found", 404);

  const localization = await getRWModLocalizationRows(env, packageId, new URL("https://rwmod.local/"));
  const compatibility = await getRWModCompatibilityRows(env, packageId, new URL("https://rwmod.local/"));
  const performance = await getRWModPerformanceRows(env, packageId, new URL("https://rwmod.local/"));
  const sources = await getRWModSourceRows(env, packageId);
  const dependencies = await getRWModDependencyRows(env, packageId);
  const guides = await getRWModGuideRows(env, packageId);
  const aliases = await getRWModAliasRows(env, packageId);
  const base = catalog
    ? formatRWModDetailBase(catalog, registry, "rwmod_catalog")
    : formatRWModRegistryDetailBase(registry);

  return json({
    ...base,
    AboveFold: {
      PackageId: base.PackageId,
      DisplayPackageId: base.DisplayPackageId,
      ModName: base.ModName,
      SupportedGameVersions: base.SupportedGameVersions,
      LocalizationStatus: base.LocalizationStatus,
      CompatibilityStatus: base.CompatibilityStatus,
      PerformanceImpact: base.PerformanceImpact,
      TrustLevel: base.TrustLevel,
      Confidence: base.Confidence,
    },
    Localization: localization,
    Compatibility: compatibility,
    Performance: performance,
    Sources: sources,
    Dependencies: dependencies,
    GuideLinks: guides,
    Aliases: aliases,
    Defaults: rwmodUnknownDefaults(),
  });
}

async function handleRWModLocalization(env, rawPackageId, url) {
  const packageId = normalizePackageId(rawPackageId);
  if (!packageId) return text("Invalid package id", 400);
  if (!(await rwmodPackageExists(env, packageId))) return text("RWMod mod not found", 404);

  const rows = await getRWModLocalizationRows(env, packageId, url);
  return json({
    PackageId: packageId,
    Status: aggregateRWModLocalizationStatus(rows),
    Items: rows,
    Count: rows.length,
    Defaults: rwmodUnknownDefaults(),
  });
}

async function handleRWModCompatibility(env, rawPackageId, url) {
  const packageId = normalizePackageId(rawPackageId);
  if (!packageId) return text("Invalid package id", 400);
  if (!(await rwmodPackageExists(env, packageId))) return text("RWMod mod not found", 404);

  const catalog = await getRWModCatalogRow(env, packageId);
  const rows = await getRWModCompatibilityRows(env, packageId, url);
  return json({
    PackageId: packageId,
    Status: normalizeRWModStatus(catalog?.CompatibilityStatus, ["unknown", "ok", "warning", "conflict"], "unknown"),
    TrustLevel: normalizeChoice(catalog?.TrustLevel, RWMOD_TRUST_LEVELS, "unknown"),
    Confidence: normalizeChoice(catalog?.Confidence, RWMOD_CONFIDENCE_LEVELS, "low"),
    Items: rows,
    Count: rows.length,
    Defaults: rwmodUnknownDefaults(),
  });
}

async function handleRWModPerformance(env, rawPackageId, url) {
  const packageId = normalizePackageId(rawPackageId);
  if (!packageId) return text("Invalid package id", 400);
  if (!(await rwmodPackageExists(env, packageId))) return text("RWMod mod not found", 404);

  const catalog = await getRWModCatalogRow(env, packageId);
  const rows = await getRWModPerformanceRows(env, packageId, url);
  return json({
    PackageId: packageId,
    Impact: normalizeChoice(catalog?.PerformanceImpact, RWMOD_PERFORMANCE_IMPACTS, "unknown"),
    TrustLevel: normalizeChoice(catalog?.TrustLevel, RWMOD_TRUST_LEVELS, "unknown"),
    Confidence: normalizeChoice(catalog?.Confidence, RWMOD_CONFIDENCE_LEVELS, "low"),
    Items: rows,
    Count: rows.length,
    Defaults: rwmodUnknownDefaults(),
  });
}

async function handleRWModCreateReport(request, env) {
  const body = await request.json();
  await verifyTurnstile(request, env, body.turnstileToken || body.cfTurnstileResponse || body["cf-turnstile-response"]);

  const reportKind = normalizeChoice(body.reportKind || body.kind || body.type, RWMOD_REPORT_KINDS, "other");
  const packageId = normalizePackageId(body.packageId);
  const modName = cleanText(body.modName, 180);
  const detail = cleanMultilineText(body.body || body.detail || body.summary, 6000);
  const reporterName = cleanText(body.reporterName, 80) || "Anonymous";
  const contact = cleanText(body.contact, 180);
  const severity = normalizeChoice(body.severity, FEEDBACK_SEVERITIES, rwmodReportSeverity(reportKind, body));
  const category = rwmodReportFeedbackCategory(reportKind);
  const now = new Date().toISOString();

  if (!packageId && reportKind !== "missing_mod") return text("PackageId is required", 400);
  if (!detail || detail.length < 12) return text("Report detail is too short", 400);

  const title = cleanText(body.title, 140) || rwmodReportTitle(reportKind, packageId, modName);
  const summary = cleanText(body.summary, 220) || detail.slice(0, 220);
  const metadata = {
    source: "rwmod",
    reportKind,
    relatedPackageId: normalizePackageId(body.relatedPackageId),
    relatedModName: cleanText(body.relatedModName, 180),
    impact: normalizeChoice(body.impact, RWMOD_PERFORMANCE_IMPACTS, "unknown"),
    compatibilityType: cleanText(body.compatibilityType || body.reportType, 80),
    trustLevel: "player_report",
    confidence: "low",
  };

  const feedbackId = crypto.randomUUID();
  await env.DB.prepare(`
    INSERT INTO FeedbackReports
    (FeedbackId, Category, Status, Severity, Title, Summary, Body, PackageId, ModName, Language,
     GameVersion, ModVersion, ReporterName, ReporterContact, IpHash, UserAgent, IsPublic,
     CreatedAt, UpdatedAt, LastActivityAt)
    VALUES (?, ?, 'open', ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 1, ?, ?, ?)
  `).bind(
    feedbackId,
    category,
    severity,
    title,
    summary,
    `${detail}\n\nRWMod metadata:\n${JSON.stringify(metadata, null, 2)}`,
    packageId,
    modName,
    cleanText(body.language, 80),
    cleanText(body.gameVersion, 80),
    cleanText(body.modVersion, 120),
    reporterName,
    contact,
    await requestIpHash(request),
    request.headers.get("user-agent") || "",
    now,
    now,
    now
  ).run();

  await writeFeedbackEvent(env, request, feedbackId, null, "create", {
    category,
    severity,
    rwmod: metadata,
  }, "RWMod report submitted", true);

  return json({
    success: true,
    feedbackId,
    status: "open",
    trustLevel: "player_report",
    confidence: "low",
    publicWarning: "Reports require review before they become RWMod public conclusions.",
  }, 201);
}

async function getRWModCatalogRow(env, packageId) {
  return env.DB.prepare("SELECT * FROM RWModMods WHERE PackageId = ? AND IsListed = 1").bind(packageId).first();
}

async function getRWModLatestRegistryRow(env, packageId) {
  const registryColumns = await getTranslationRegistryColumnSet(env);
  const downloadCountProjection = registryColumns.has("DownloadCount") ? "" : "0 AS DownloadCount,";
  const totalDownloadCountProjection = registryColumns.has("DownloadCount")
    ? "SUM(DownloadCount) OVER (PARTITION BY lower(PackageId)) AS TotalDownloadCount"
    : "0 AS TotalDownloadCount";
  return env.DB.prepare(`
    SELECT
      *,
      ${downloadCountProjection}
      lower(PackageId) AS PackageKey,
      COUNT(*) OVER (PARTITION BY lower(PackageId)) AS RegistryRecordCount,
      ${totalDownloadCountProjection},
      MAX(LastUpdated) OVER (PARTITION BY lower(PackageId)) AS LastRegistryUpdated
    FROM TranslationRegistry
    WHERE lower(PackageId) = ? AND IsDeleted IS NOT 1
    ORDER BY IsVerified DESC, LastUpdated DESC, TranslationDate DESC, RecordId DESC
    LIMIT 1
  `).bind(packageId).first();
}

async function getTranslationRegistryColumnSet(env) {
  const { results } = await env.DB.prepare("PRAGMA table_info(TranslationRegistry)").all();
  return new Set((results || []).map((row) => row.name).filter(Boolean));
}

async function rwmodPackageExists(env, packageId) {
  if (await getRWModCatalogRow(env, packageId)) return true;
  return Boolean(await getRWModLatestRegistryRow(env, packageId));
}

async function getRWModLocalizationRows(env, packageId, url) {
  const language = cleanText(url.searchParams.get("language"), 80);
  const values = [packageId];
  const filters = ["PackageId = ?", "IsVisible = 1"];
  if (language) {
    filters.push("Language = ?");
    values.push(language);
  }

  const existing = await env.DB.prepare(`
    SELECT *
    FROM RWModLocalizationStatus
    WHERE ${filters.join(" AND ")}
    ORDER BY Language ASC, IsVerified DESC, UpdatedAt DESC
    LIMIT 100
  `).bind(...values).all();
  const rows = (existing.results || []).map(formatRWModLocalizationRow);
  const seenRecordIds = new Set(rows.map((row) => row.RegistryRecordId).filter(Boolean));

  const registryFilters = ["lower(PackageId) = ?", "IsDeleted IS NOT 1"];
  const registryValues = [packageId];
  if (language) {
    registryFilters.push("Language = ?");
    registryValues.push(language);
  }

  const registry = await env.DB.prepare(`
    SELECT *
    FROM TranslationRegistry
    WHERE ${registryFilters.join(" AND ")}
    ORDER BY Language ASC, IsVerified DESC, LastUpdated DESC
    LIMIT 100
  `).bind(...registryValues).all();

  for (const row of registry.results || []) {
    if (seenRecordIds.has(row.RecordId)) continue;
    rows.push(formatRWModRegistryLocalizationRow(row));
  }

  return rows;
}

async function getRWModCompatibilityRows(env, packageId, url) {
  const limit = clampInt(url.searchParams.get("limit"), 1, 100, 50);
  const { results } = await env.DB.prepare(`
    SELECT *
    FROM RWModCompatibilityReports
    WHERE PackageId = ? AND IsPublic = 1
    ORDER BY
      IsVerified DESC,
      CASE Confidence WHEN 'high' THEN 3 WHEN 'medium' THEN 2 ELSE 1 END DESC,
      UpdatedAt DESC
    LIMIT ?
  `).bind(packageId, limit).all();

  return (results || []).map(formatRWModCompatibilityRow);
}

async function getRWModPerformanceRows(env, packageId, url) {
  const limit = clampInt(url.searchParams.get("limit"), 1, 100, 50);
  const { results } = await env.DB.prepare(`
    SELECT *
    FROM RWModPerformanceReports
    WHERE PackageId = ? AND IsPublic = 1
    ORDER BY
      IsVerified DESC,
      CASE Confidence WHEN 'high' THEN 3 WHEN 'medium' THEN 2 ELSE 1 END DESC,
      UpdatedAt DESC
    LIMIT ?
  `).bind(packageId, limit).all();

  return (results || []).map(formatRWModPerformanceRow);
}

async function getRWModSourceRows(env, packageId) {
  const { results } = await env.DB.prepare(`
    SELECT *
    FROM RWModModSources
    WHERE PackageId = ? AND IsVisible = 1
    ORDER BY IsPrimary DESC, SourceType ASC, UpdatedAt DESC
    LIMIT 50
  `).bind(packageId).all();
  return (results || []).map(formatRWModSourceRow);
}

async function getRWModDependencyRows(env, packageId) {
  const { results } = await env.DB.prepare(`
    SELECT *
    FROM RWModDependencies
    WHERE PackageId = ? AND IsVisible = 1
    ORDER BY DependencyType ASC, DependencyName ASC
    LIMIT 100
  `).bind(packageId).all();
  return (results || []).map(formatRWModDependencyRow);
}

async function getRWModGuideRows(env, packageId) {
  const { results } = await env.DB.prepare(`
    SELECT *
    FROM RWModGuideLinks
    WHERE PackageId = ? AND IsVisible = 1
    ORDER BY SortOrder ASC, UpdatedAt DESC
    LIMIT 50
  `).bind(packageId).all();
  return (results || []).map(formatRWModGuideRow);
}

async function getRWModAliasRows(env, packageId) {
  const { results } = await env.DB.prepare(`
    SELECT *
    FROM RWModAliases
    WHERE PackageId = ?
    ORDER BY SearchWeight DESC, AliasText ASC
    LIMIT 100
  `).bind(packageId).all();
  return (results || []).map(formatRWModAliasRow);
}

function formatRWModListRow(row, dataSource) {
  const packageId = normalizePackageId(row.PackageId);
  return {
    PackageId: packageId,
    DisplayPackageId: row.DisplayPackageId || row.PackageId || packageId,
    ModName: row.ModName || row.DisplayPackageId || row.PackageId || "Unknown mod",
    Summary: row.Summary || "",
    Author: row.Author || "",
    PrimaryWorkshopId: row.PrimaryWorkshopId || "",
    SupportedGameVersions: parseRWModGameVersions(row.SupportedGameVersionsJson),
    LatestKnownVersion: row.LatestKnownVersion || "Unknown",
    LastWorkshopUpdated: row.LastWorkshopUpdated || null,
    LastRegistryUpdated: row.LastRegistryUpdated || null,
    LocalizationStatus: normalizeChoice(row.LocalizationStatus, RWMOD_LOCALIZATION_STATUSES, "unknown"),
    CompatibilityStatus: normalizeRWModStatus(row.CompatibilityStatus, ["unknown", "ok", "warning", "conflict"], "unknown"),
    PerformanceImpact: normalizeChoice(row.PerformanceImpact, RWMOD_PERFORMANCE_IMPACTS, "unknown"),
    TrustLevel: normalizeChoice(row.TrustLevel, RWMOD_TRUST_LEVELS, "unknown"),
    Confidence: normalizeChoice(row.Confidence, RWMOD_CONFIDENCE_LEVELS, "low"),
    LocalizationCount: Number(row.LocalizationCount || 0),
    CompatibilityReportCount: Number(row.CompatibilityReportCount || 0),
    PerformanceReportCount: Number(row.PerformanceReportCount || 0),
    DataSource: dataSource || "rwmod_catalog",
  };
}

function formatRWModRegistryListRow(row) {
  const packageId = normalizePackageId(row.PackageId);
  return {
    PackageId: packageId,
    DisplayPackageId: row.PackageId || packageId,
    ModName: row.ModName || row.PackageId || "Unknown mod",
    Summary: "",
    Author: row.Author || "",
    PrimaryWorkshopId: "",
    SupportedGameVersions: uniqueArray([row.TargetModVersion, row.LatestVersion].filter(isUsefulVersionText)),
    LatestKnownVersion: row.LatestVersion || row.TargetModVersion || "Unknown",
    LastWorkshopUpdated: row.ModLastUpdated || null,
    LastRegistryUpdated: row.LastRegistryUpdated || row.LastUpdated || null,
    LocalizationStatus: registryLocalizationStatus(row),
    CompatibilityStatus: "unknown",
    PerformanceImpact: "unknown",
    TrustLevel: "cloud_record",
    Confidence: row.IsVerified === 1 ? "medium" : "low",
    LocalizationCount: Number(row.RegistryRecordCount || 1),
    CompatibilityReportCount: 0,
    PerformanceReportCount: 0,
    TotalDownloadCount: Number(row.TotalDownloadCount || row.DownloadCount || 0),
    DataSource: "translation_registry",
  };
}

function formatRWModDetailBase(catalog, registry, dataSource) {
  const base = formatRWModListRow(catalog, dataSource);
  return {
    ...base,
    RegistrySeed: registry ? formatRWModRegistrySeedRow(registry) : null,
    CreatedAt: catalog.CreatedAt || null,
    UpdatedAt: catalog.UpdatedAt || null,
  };
}

function formatRWModRegistryDetailBase(registry) {
  const base = formatRWModRegistryListRow(registry);
  return {
    ...base,
    RegistrySeed: formatRWModRegistrySeedRow(registry),
    CreatedAt: registry.CreatedAt || null,
    UpdatedAt: registry.UpdatedAt || null,
  };
}

function formatRWModRegistrySeedRow(row) {
  if (!row) return null;
  return {
    RecordId: row.RecordId,
    PackageId: normalizePackageId(row.PackageId),
    Language: row.Language || "",
    ModName: row.ModName || "",
    Author: row.Author || "",
    TranslationType: row.TranslationType || "",
    IsVerified: row.IsVerified === 1,
    TargetModVersion: row.TargetModVersion || "Unknown",
    LatestVersion: row.LatestVersion || "Unknown",
    LastUpdated: row.LastUpdated || null,
    ModLastUpdated: row.ModLastUpdated || null,
    DownloadCount: Number(row.DownloadCount || 0),
    ContributorName: publicContributorName(row),
    ContributorKind: publicContributorKind(row),
  };
}

function formatRWModLocalizationRow(row) {
  return {
    LocalizationId: row.LocalizationId,
    PackageId: normalizePackageId(row.PackageId),
    Language: row.Language || "",
    LocaleLabel: row.LocaleLabel || row.Language || "",
    Status: normalizeChoice(row.Status, RWMOD_LOCALIZATION_STATUSES, "unknown"),
    SourceType: row.SourceType || "unknown",
    TrustLevel: normalizeChoice(row.TrustLevel, RWMOD_TRUST_LEVELS, "unknown"),
    Confidence: normalizeChoice(row.Confidence, RWMOD_CONFIDENCE_LEVELS, "low"),
    RegistryRecordId: row.RegistryRecordId || null,
    TranslationType: row.TranslationType || "",
    IsVerified: row.IsVerified === 1,
    CoveragePercent: nullableNumber(row.CoveragePercent),
    TranslatedKeyCount: nullableNumber(row.TranslatedKeyCount),
    MissingKeyCount: nullableNumber(row.MissingKeyCount),
    OutdatedKeyCount: nullableNumber(row.OutdatedKeyCount),
    TargetModVersion: row.TargetModVersion || "Unknown",
    SourceUrl: row.SourceUrl || "",
    ContributorName: row.ContributorName || "",
    Notes: row.Notes || "",
    CreatedAt: row.CreatedAt || null,
    UpdatedAt: row.UpdatedAt || null,
    DataSource: "rwmod_catalog",
  };
}

function formatRWModRegistryLocalizationRow(row) {
  return {
    LocalizationId: `registry:${row.RecordId}`,
    PackageId: normalizePackageId(row.PackageId),
    Language: row.Language || "",
    LocaleLabel: row.Language || "",
    Status: registryLocalizationStatus(row),
    SourceType: "ai_translation_core",
    TrustLevel: "cloud_record",
    Confidence: row.IsVerified === 1 ? "medium" : "low",
    RegistryRecordId: row.RecordId,
    TranslationType: row.TranslationType || "",
    IsVerified: row.IsVerified === 1,
    CoveragePercent: null,
    TranslatedKeyCount: null,
    MissingKeyCount: null,
    OutdatedKeyCount: null,
    TargetModVersion: row.TargetModVersion || "Unknown",
    SourceUrl: "",
    ContributorName: publicContributorName(row),
    Notes: row.UpdateLog || "",
    CreatedAt: row.CreatedAt || null,
    UpdatedAt: row.UpdatedAt || row.LastUpdated || null,
    LastUpdated: row.LastUpdated || null,
    DownloadCount: Number(row.DownloadCount || 0),
    DataSource: "translation_registry",
  };
}

function formatRWModCompatibilityRow(row) {
  return {
    CompatibilityId: row.CompatibilityId,
    PackageId: normalizePackageId(row.PackageId),
    RelatedPackageId: normalizePackageId(row.RelatedPackageId),
    RelatedModName: row.RelatedModName || "",
    ReportType: row.ReportType || "soft_conflict",
    Severity: row.Severity || "normal",
    PublicStatus: normalizeRWModStatus(row.PublicStatus, ["unknown", "ok", "warning", "conflict"], "unknown"),
    GameVersion: row.GameVersion || "",
    ModVersion: row.ModVersion || "",
    ErrorPattern: row.ErrorPattern || "",
    Summary: row.Summary || "",
    Detail: row.Detail || "",
    EvidenceUrl: row.EvidenceUrl || "",
    ReporterName: row.ReporterName || "Anonymous",
    SourceType: row.SourceType || "player_report",
    TrustLevel: normalizeChoice(row.TrustLevel, RWMOD_TRUST_LEVELS, "player_report"),
    Confidence: normalizeChoice(row.Confidence, RWMOD_CONFIDENCE_LEVELS, "low"),
    VoteCount: Number(row.VoteCount || 0),
    IsVerified: row.IsVerified === 1,
    CreatedAt: row.CreatedAt || null,
    UpdatedAt: row.UpdatedAt || null,
    ReviewedAt: row.ReviewedAt || null,
  };
}

function formatRWModPerformanceRow(row) {
  return {
    PerformanceId: row.PerformanceId,
    PackageId: normalizePackageId(row.PackageId),
    Impact: normalizeChoice(row.Impact, RWMOD_PERFORMANCE_IMPACTS, "unknown"),
    Severity: row.Severity || "normal",
    GameVersion: row.GameVersion || "",
    ModVersion: row.ModVersion || "",
    ModCount: nullableNumber(row.ModCount),
    PawnCount: nullableNumber(row.PawnCount),
    ColonyAgeDays: nullableNumber(row.ColonyAgeDays),
    CpuModel: row.CpuModel || "",
    Scenario: row.Scenario || "other",
    MetricType: row.MetricType || "player_report",
    TpsBefore: nullableNumber(row.TpsBefore),
    TpsAfter: nullableNumber(row.TpsAfter),
    LoadSeconds: nullableNumber(row.LoadSeconds),
    Summary: row.Summary || "",
    Detail: row.Detail || "",
    ReporterName: row.ReporterName || "Anonymous",
    SourceType: row.SourceType || "player_report",
    TrustLevel: normalizeChoice(row.TrustLevel, RWMOD_TRUST_LEVELS, "player_report"),
    Confidence: normalizeChoice(row.Confidence, RWMOD_CONFIDENCE_LEVELS, "low"),
    VoteCount: Number(row.VoteCount || 0),
    IsVerified: row.IsVerified === 1,
    CreatedAt: row.CreatedAt || null,
    UpdatedAt: row.UpdatedAt || null,
    ReviewedAt: row.ReviewedAt || null,
  };
}

function formatRWModSourceRow(row) {
  return {
    SourceId: row.SourceId,
    PackageId: normalizePackageId(row.PackageId),
    SourceType: row.SourceType || "other",
    Url: row.Url || "",
    Label: row.Label || "",
    SourceOwner: row.SourceOwner || "",
    Language: row.Language || "",
    IsPrimary: row.IsPrimary === 1,
    TrustLevel: normalizeChoice(row.TrustLevel, RWMOD_TRUST_LEVELS, "unknown"),
    CreatedAt: row.CreatedAt || null,
    UpdatedAt: row.UpdatedAt || null,
  };
}

function formatRWModDependencyRow(row) {
  return {
    DependencyId: row.DependencyId,
    PackageId: normalizePackageId(row.PackageId),
    DependencyPackageId: normalizePackageId(row.DependencyPackageId),
    DependencyName: row.DependencyName || "",
    DependencyWorkshopId: row.DependencyWorkshopId || "",
    DependencyType: row.DependencyType || "required",
    GameVersion: row.GameVersion || "",
    LoadOrderNote: row.LoadOrderNote || "",
    SourceType: row.SourceType || "unknown",
    TrustLevel: normalizeChoice(row.TrustLevel, RWMOD_TRUST_LEVELS, "unknown"),
    Confidence: normalizeChoice(row.Confidence, RWMOD_CONFIDENCE_LEVELS, "low"),
    CreatedAt: row.CreatedAt || null,
    UpdatedAt: row.UpdatedAt || null,
  };
}

function formatRWModGuideRow(row) {
  return {
    GuideId: row.GuideId,
    PackageId: normalizePackageId(row.PackageId),
    LinkType: row.LinkType || "other",
    Url: row.Url || "",
    Title: row.Title || "",
    AuthorName: row.AuthorName || "",
    Language: row.Language || "",
    Platform: row.Platform || "",
    IsOfficial: row.IsOfficial === 1,
    TrustLevel: normalizeChoice(row.TrustLevel, RWMOD_TRUST_LEVELS, "unknown"),
    SortOrder: Number(row.SortOrder || 0),
    CreatedAt: row.CreatedAt || null,
    UpdatedAt: row.UpdatedAt || null,
  };
}

function formatRWModAliasRow(row) {
  return {
    AliasId: row.AliasId,
    PackageId: normalizePackageId(row.PackageId),
    AliasText: row.AliasText || "",
    AliasType: row.AliasType || "other",
    Language: row.Language || "",
    SearchWeight: Number(row.SearchWeight || 10),
    SourceType: row.SourceType || "unknown",
    CreatedAt: row.CreatedAt || null,
  };
}

function aggregateRWModLocalizationStatus(rows) {
  if (!rows.length) return "unknown";
  const order = ["complete", "mostly_complete", "partial", "outdated", "missing", "unknown"];
  return rows
    .map((row) => normalizeChoice(row.Status, RWMOD_LOCALIZATION_STATUSES, "unknown"))
    .sort((a, b) => order.indexOf(a) - order.indexOf(b))[0] || "unknown";
}

function registryLocalizationStatus(row) {
  if (!row) return "unknown";
  return "partial";
}

function rwmodUnknownDefaults() {
  return {
    LocalizationStatus: "unknown",
    CompatibilityStatus: "unknown",
    PerformanceImpact: "unknown",
    TrustLevel: "unknown",
    Confidence: "low",
  };
}

function normalizePackageId(value) {
  return String(value || "")
    .trim()
    .toLowerCase()
    .replace(/\s+/g, "")
    .slice(0, 180);
}

function parseRWModGameVersions(value) {
  const parsed = safeJsonArray(value);
  if (parsed.length) return parsed.map((item) => cleanText(item, 40)).filter(Boolean);
  return [];
}

function isUsefulVersionText(value) {
  const textValue = String(value || "").trim();
  return textValue && !["unknown", "n/a", "-"].includes(textValue.toLowerCase());
}

function nullableNumber(value) {
  if (value === null || value === undefined || value === "") return null;
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : null;
}

function normalizeRWModStatus(value, allowed, fallback) {
  return normalizeChoice(String(value || "").toLowerCase(), allowed, fallback);
}

function rwmodReportFeedbackCategory(reportKind) {
  if (reportKind === "compatibility") return "compatibility";
  if (reportKind === "performance") return "optimization";
  if (reportKind === "localization") return "translation_wrong";
  if (reportKind === "missing_mod") return "missing_mod";
  return "other";
}

function rwmodReportSeverity(reportKind, body) {
  const impact = normalizeChoice(body?.impact, RWMOD_PERFORMANCE_IMPACTS, "unknown");
  if (reportKind === "compatibility") return "high";
  if (reportKind === "performance" && impact === "heavy") return "high";
  return "normal";
}

function rwmodReportTitle(reportKind, packageId, modName) {
  const target = modName || packageId || "unknown mod";
  if (reportKind === "compatibility") return `RWMod compatibility report: ${target}`;
  if (reportKind === "performance") return `RWMod performance report: ${target}`;
  if (reportKind === "localization") return `RWMod localization report: ${target}`;
  if (reportKind === "missing_mod") return "RWMod missing mod report";
  return `RWMod report: ${target}`;
}

async function verifyTurnstile(request, env, token) {
  if (env.RWMOD_LOCAL_PREVIEW === "1" && token === "preview-turnstile-token") {
    const host = new URL(request.url).hostname.toLowerCase();
    if (["rwmod.local", "127.0.0.1", "localhost", "::1"].includes(host)) {
      return { success: true, localPreview: true };
    }
  }

  const secret = env.TURNSTILE_SECRET_KEY || "";
  if (!secret) throw httpError(503, "Turnstile is not configured");
  if (!token) throw httpError(400, "Turnstile token is required");

  const form = new URLSearchParams();
  form.set("secret", secret);
  form.set("response", String(token));
  const remoteIp = request.headers.get("cf-connecting-ip");
  if (remoteIp) form.set("remoteip", remoteIp);

  const response = await fetch("https://challenges.cloudflare.com/turnstile/v0/siteverify", {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body: form.toString(),
  });
  const result = await response.json();
  if (!result || result.success !== true) throw httpError(403, "Turnstile verification failed");
  return result;
}

async function handleDownload(request, env, url) {
  const parts = url.pathname.split("/");
  if (parts.length < 6) return text("Invalid URL", 400);

  const packageId = parts[4].toLowerCase();
  const language = parts[5];
  const recordId = url.searchParams.get("recordId");

  let record = null;
  if (recordId) {
    record = await env.DB.prepare(
      "SELECT * FROM TranslationRegistry WHERE RecordId = ? AND IsDeleted IS NOT 1"
    ).bind(recordId).first();
  } else {
    record = await env.DB.prepare(
      "SELECT * FROM TranslationRegistry WHERE PackageId = ? AND Language = ? AND IsDeleted IS NOT 1 ORDER BY IsVerified DESC, LastUpdated DESC LIMIT 1"
    ).bind(packageId, language).first();
  }

  if (!record) return text("Record not found", 404);

  const object = await env.BUCKET.get(record.FileUrl || `${record.RecordId}.zip`);
  if (!object) return text("Not found in Bucket", 404);

  if (ENABLE_DOWNLOAD_COUNT) {
    await tryOptionalDbWrite(
      env.DB.prepare("UPDATE TranslationRegistry SET DownloadCount = DownloadCount + 1 WHERE RecordId = ?").bind(record.RecordId),
      "download count"
    );
  }

  if (ENABLE_EVENT_LOGS) {
    await writeDownloadEvent(env, request, record);
  }

  const headers = new Headers(CORS_HEADERS);
  headers.set("Content-Type", "application/zip");
  return createResponse(object.body, 200, headers);
}

async function handleListPublicFeedback(env, url) {
  const status = url.searchParams.get("status");
  const category = url.searchParams.get("category");
  const query = String(url.searchParams.get("q") || "").trim();
  const limit = clampInt(url.searchParams.get("limit"), 1, 100, 50);
  const sort = url.searchParams.get("sort") || "updated";

  const filters = ["IsPublic = 1"];
  const values = [];
  if (status && FEEDBACK_STATUSES.includes(status)) {
    filters.push("Status = ?");
    values.push(status);
  }
  if (category && FEEDBACK_CATEGORIES.includes(category)) {
    filters.push("Category = ?");
    values.push(category);
  }
  if (query) {
    filters.push("(Title LIKE ? OR PackageId LIKE ? OR ModName LIKE ? OR Summary LIKE ?)");
    const like = `%${query.slice(0, 80)}%`;
    values.push(like, like, like, like);
  }

  const orderBy = sort === "votes"
    ? "VoteCount DESC, LastActivityAt DESC"
    : sort === "created"
      ? "CreatedAt DESC"
      : "LastActivityAt DESC";

  const { results } = await env.DB.prepare(`
    SELECT FeedbackId, Category, Status, Severity, Title, Summary, PackageId, ModName, Language,
           GameVersion, ModVersion, ReporterName, VoteCount, CommentCount, IsPinned,
           CreatedAt, LastActivityAt, ResolvedAt
    FROM FeedbackReports
    WHERE ${filters.join(" AND ")}
    ORDER BY IsPinned DESC, ${orderBy}
    LIMIT ?
  `).bind(...values, limit).all();

  return json(await attachAttachmentCounts(env, "feedback", results || [], "FeedbackId", formatPublicFeedbackRow));
}

async function handleGetPublicFeedback(env, feedbackId) {
  const row = await getFeedbackById(env, feedbackId);
  if (!row || row.IsPublic !== 1) return text("Feedback not found", 404);

  const { results } = await env.DB.prepare(`
    SELECT EventId, Action, ActorCodeId, PublicNote, CreatedAt
    FROM FeedbackEvents
    WHERE FeedbackId = ? AND IsPublic = 1
    ORDER BY CreatedAt ASC
    LIMIT 100
  `).bind(feedbackId).all();

  return json({
    ...formatPublicFeedbackRow(row),
    Body: row.Body || "",
    Events: (results || []).map(formatPublicFeedbackEventRow),
    AttachmentCount: await countRequestAttachments(env, "feedback", feedbackId),
  });
}

async function handleCreateFeedback(request, env) {
  const body = await request.json();
  const title = cleanText(body.title, 140);
  const detail = cleanMultilineText(body.body || body.detail, 6000);
  const category = normalizeChoice(body.category, FEEDBACK_CATEGORIES, "other");
  const severity = normalizeChoice(body.severity, FEEDBACK_SEVERITIES, "normal");
  const now = new Date().toISOString();

  if (!title || title.length < 4) return text("Title is too short", 400);
  if (!detail || detail.length < 12) return text("Feedback detail is too short", 400);
  validateRequestAttachments(body.attachments);

  const feedbackId = crypto.randomUUID();
  const reporterName = cleanText(body.reporterName, 80) || "Anonymous";
  const contact = cleanText(body.contact, 180);
  const summary = cleanText(body.summary, 220) || detail.slice(0, 220);

  await env.DB.prepare(`
    INSERT INTO FeedbackReports
    (FeedbackId, Category, Status, Severity, Title, Summary, Body, PackageId, ModName, Language,
     GameVersion, ModVersion, ReporterName, ReporterContact, IpHash, UserAgent, IsPublic,
     CreatedAt, UpdatedAt, LastActivityAt)
    VALUES (?, ?, 'open', ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 1, ?, ?, ?)
  `).bind(
    feedbackId,
    category,
    severity,
    title,
    summary,
    detail,
    cleanText(body.packageId, 160).toLowerCase(),
    cleanText(body.modName, 180),
    cleanText(body.language, 80),
    cleanText(body.gameVersion, 80),
    cleanText(body.modVersion, 120),
    reporterName,
    contact,
    await requestIpHash(request),
    request.headers.get("user-agent") || "",
    now,
    now,
    now
  ).run();

  await writeFeedbackEvent(env, request, feedbackId, null, "create", {
    category,
    severity,
  }, "Feedback submitted", true);

  const attachments = await saveRequestAttachments(env, "feedback", feedbackId, body.attachments);

  return json({ success: true, feedbackId, status: "open", attachments: attachments.length }, 201);
}

async function handleVoteFeedback(request, env, feedbackId) {
  const row = await getFeedbackById(env, feedbackId);
  if (!row || row.IsPublic !== 1) return text("Feedback not found", 404);

  await env.DB.prepare(`
    UPDATE FeedbackReports
    SET VoteCount = VoteCount + 1, LastActivityAt = ?, UpdatedAt = ?
    WHERE FeedbackId = ? AND IsPublic = 1
  `).bind(new Date().toISOString(), new Date().toISOString(), feedbackId).run();

  await writeFeedbackEvent(env, request, feedbackId, null, "vote", {}, "Another player has this issue", false);

  const updated = await getFeedbackById(env, feedbackId);
  return json({ success: true, voteCount: updated ? updated.VoteCount : row.VoteCount + 1 });
}

async function handleListAdminFeedback(env, auth, url) {
  const status = url.searchParams.get("status");
  const category = url.searchParams.get("category");
  const includeHidden = url.searchParams.get("includeHidden") === "true";
  const query = String(url.searchParams.get("q") || "").trim();
  const limit = clampInt(url.searchParams.get("limit"), 1, 200, 100);

  const filters = [];
  const values = [];
  if (!includeHidden) filters.push("IsPublic = 1");
  if (status && FEEDBACK_STATUSES.includes(status)) {
    filters.push("Status = ?");
    values.push(status);
  }
  if (category && FEEDBACK_CATEGORIES.includes(category)) {
    filters.push("Category = ?");
    values.push(category);
  }
  if (query) {
    filters.push("(FeedbackId LIKE ? OR Title LIKE ? OR PackageId LIKE ? OR ModName LIKE ? OR Summary LIKE ? OR ReporterName LIKE ?)");
    const like = `%${query.slice(0, 80)}%`;
    values.push(like, like, like, like, like, like);
  }

  const where = filters.length ? `WHERE ${filters.join(" AND ")}` : "";
  const { results } = await env.DB.prepare(`
    SELECT FeedbackId, Category, Status, Severity, Title, Summary, PackageId, ModName, Language,
           GameVersion, ModVersion, ReporterName, ReporterContact, VoteCount, CommentCount,
           IsPublic, IsPinned, AssignedTo, DuplicateOf, ResolutionNote, CreatedAt,
           UpdatedAt, LastActivityAt, ResolvedAt
    FROM FeedbackReports
    ${where}
    ORDER BY IsPinned DESC, LastActivityAt DESC
    LIMIT ?
  `).bind(...values, limit).all();

  return json(await attachAttachmentCounts(env, "feedback", results || [], "FeedbackId", formatAdminFeedbackRow));
}

async function handleGetAdminFeedback(env, auth, feedbackId) {
  const row = await getFeedbackById(env, feedbackId);
  if (!row) return text("Feedback not found", 404);
  const attachments = await listRequestAttachments(env, "feedback", feedbackId);
  return json({ ...formatAdminFeedbackRow(row), Attachments: attachments });
}

async function handleUpdateFeedback(request, env, auth, feedbackId) {
  const row = await getFeedbackById(env, feedbackId);
  if (!row) return text("Feedback not found", 404);

  const body = await request.json();
  const now = new Date().toISOString();
  const nextStatus = Object.prototype.hasOwnProperty.call(body, "status")
    ? normalizeChoice(body.status, FEEDBACK_STATUSES, row.Status)
    : row.Status;
  const nextCategory = Object.prototype.hasOwnProperty.call(body, "category")
    ? normalizeChoice(body.category, FEEDBACK_CATEGORIES, row.Category)
    : row.Category;
  const nextSeverity = Object.prototype.hasOwnProperty.call(body, "severity")
    ? normalizeChoice(body.severity, FEEDBACK_SEVERITIES, row.Severity)
    : row.Severity;
  const isResolved = ["fixed", "duplicate", "closed"].includes(nextStatus);

  await env.DB.prepare(`
    UPDATE FeedbackReports
    SET Category = ?, Status = ?, Severity = ?, IsPublic = ?, IsPinned = ?, AssignedTo = ?,
        DuplicateOf = ?, ResolutionNote = ?, UpdatedAt = ?, LastActivityAt = ?,
        ResolvedAt = CASE WHEN ? THEN COALESCE(ResolvedAt, ?) ELSE NULL END
    WHERE FeedbackId = ?
  `).bind(
    nextCategory,
    nextStatus,
    nextSeverity,
    boolToInt(body.isPublic, row.IsPublic),
    boolToInt(body.isPinned, row.IsPinned),
    cleanText(body.assignedTo, 120) || row.AssignedTo || "",
    cleanText(body.duplicateOf, 80) || "",
    cleanText(body.resolutionNote, 1000) || "",
    now,
    now,
    isResolved ? 1 : 0,
    now,
    feedbackId
  ).run();

  const publicNote = cleanText(body.publicNote, 1000);
  await writeFeedbackEvent(env, request, feedbackId, auth.codeId, "moderate", {
    status: nextStatus,
    category: nextCategory,
    severity: nextSeverity,
    isPublic: boolToInt(body.isPublic, row.IsPublic) === 1,
    isPinned: boolToInt(body.isPinned, row.IsPinned) === 1,
  }, publicNote, Boolean(publicNote));

  const updated = await getFeedbackById(env, feedbackId);
  return json({ success: true, feedback: formatAdminFeedbackRow(updated) });
}

async function handleFeedbackEvents(env, auth, feedbackId, url) {
  const limit = clampInt(url.searchParams.get("limit"), 1, 300, 100);
  const { results } = await env.DB.prepare(`
    SELECT EventId, FeedbackId, ActorCodeId, Action, MetadataJson, PublicNote, IsPublic,
           IpHash, UserAgent, CreatedAt
    FROM FeedbackEvents
    WHERE FeedbackId = ?
    ORDER BY CreatedAt DESC
    LIMIT ?
  `).bind(feedbackId, limit).all();
  return json(results || []);
}

async function handleCreateGroupKeyApplication(request, env) {
  const body = await request.json();
  const applicantName = cleanText(body.applicantName || body.name, 100);
  const contact = cleanText(body.contact, 180);
  const groupName = cleanText(body.groupName, 140);
  const gameVersion = cleanText(body.gameVersion, 80) || "1.6";
  const steamWorkshopUrl = cleanText(body.steamWorkshopUrl || body.workshopUrl, 320);
  const portfolioText = cleanMultilineText(body.portfolioText || body.showcase, 4000);
  const proofText = cleanMultilineText(body.proofText || body.proof, 4000);
  const now = new Date().toISOString();

  if (!applicantName || applicantName.length < 2) return text("Applicant name is too short", 400);
  if (!contact || contact.length < 3) return text("Contact is required", 400);
  if (!groupName || groupName.length < 2) return text("Group name is too short", 400);
  if (!isLikelySteamWorkshopUrl(steamWorkshopUrl)) return text("A valid Steam Workshop link is required", 400);
  if (!portfolioText || portfolioText.length < 12) return text("Portfolio/showcase is too short", 400);
  if (!proofText || proofText.length < 12) return text("Author proof is too short", 400);
  validateRequestAttachments(body.attachments);

  const applicationId = crypto.randomUUID();
  await env.DB.prepare(`
    INSERT INTO GroupKeyApplications
    (ApplicationId, Status, ApplicantName, Contact, GroupName, GameVersion, SteamWorkshopUrl,
     PortfolioText, ProofText, IpHash, UserAgent, CreatedAt, UpdatedAt)
    VALUES (?, 'open', ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
  `).bind(
    applicationId,
    applicantName,
    contact,
    groupName,
    gameVersion,
    steamWorkshopUrl,
    portfolioText,
    proofText,
    await requestIpHash(request),
    request.headers.get("user-agent") || "",
    now,
    now
  ).run();

  const attachments = await saveRequestAttachments(env, "application", applicationId, body.attachments);
  await writeGroupKeyApplicationEvent(env, request, applicationId, null, "create", {
    attachmentCount: attachments.length,
  }, "");

  return json({ success: true, applicationId, status: "open", attachments: attachments.length }, 201);
}

async function handleGetPublicGroupKeyApplication(env, applicationId) {
  const row = await getGroupKeyApplicationById(env, applicationId);
  if (!row) return text("Application not found", 404);
  return json(formatPublicGroupKeyApplicationRow(row));
}

async function handleListAdminGroupKeyApplications(env, auth, url) {
  const status = url.searchParams.get("status");
  const query = String(url.searchParams.get("q") || "").trim();
  const limit = clampInt(url.searchParams.get("limit"), 1, 200, 100);
  const filters = [];
  const values = [];

  if (status && APPLICATION_STATUSES.includes(status)) {
    filters.push("Status = ?");
    values.push(status);
  }

  if (query) {
    filters.push("(ApplicationId LIKE ? OR ApplicantName LIKE ? OR Contact LIKE ? OR GroupName LIKE ? OR SteamWorkshopUrl LIKE ?)");
    const like = `%${query.slice(0, 100)}%`;
    values.push(like, like, like, like, like);
  }

  const where = filters.length ? `WHERE ${filters.join(" AND ")}` : "";
  const { results } = await env.DB.prepare(`
    SELECT ApplicationId, Status, ApplicantName, Contact, GroupName, GameVersion, SteamWorkshopUrl,
           PortfolioText, ProofText, ReviewerNote, IssuedCodeId, CreatedAt, UpdatedAt, ReviewedAt, IssuedAt,
           (SELECT COUNT(*) FROM RequestAttachments ra WHERE ra.OwnerType = 'application' AND ra.OwnerId = GroupKeyApplications.ApplicationId) AS AttachmentCount
    FROM GroupKeyApplications
    ${where}
    ORDER BY CreatedAt DESC
    LIMIT ?
  `).bind(...values, limit).all();

  return json((results || []).map(formatAdminGroupKeyApplicationRow));
}

async function handleGetAdminGroupKeyApplication(env, auth, applicationId) {
  const row = await getGroupKeyApplicationById(env, applicationId);
  if (!row) return text("Application not found", 404);
  const attachments = await listRequestAttachments(env, "application", applicationId);
  return json({ ...formatAdminGroupKeyApplicationRow(row), Attachments: attachments });
}

async function handleUpdateGroupKeyApplication(request, env, auth, applicationId) {
  const row = await getGroupKeyApplicationById(env, applicationId);
  if (!row) return text("Application not found", 404);

  const body = await request.json();
  const now = new Date().toISOString();
  const nextStatus = Object.prototype.hasOwnProperty.call(body, "status")
    ? normalizeChoice(body.status, APPLICATION_STATUSES, row.Status)
    : row.Status;
  const reviewerNote = Object.prototype.hasOwnProperty.call(body, "reviewerNote")
    ? cleanMultilineText(body.reviewerNote, 4000)
    : row.ReviewerNote || "";

  await env.DB.prepare(`
    UPDATE GroupKeyApplications
    SET Status = ?, ReviewerNote = ?, UpdatedAt = ?, ReviewedAt = COALESCE(ReviewedAt, ?)
    WHERE ApplicationId = ?
  `).bind(nextStatus, reviewerNote, now, now, applicationId).run();

  await writeGroupKeyApplicationEvent(env, request, applicationId, auth.codeId, "moderate", {
    status: nextStatus,
  }, cleanText(body.publicNote, 1000));

  const updated = await getGroupKeyApplicationById(env, applicationId);
  const attachments = await listRequestAttachments(env, "application", applicationId);
  return json({ success: true, application: { ...formatAdminGroupKeyApplicationRow(updated), Attachments: attachments } });
}

async function handleIssueGroupKeyApplication(request, env, auth, applicationId) {
  if (auth.role !== "master") throw httpError(403, "Only the master key can issue official group keys");

  const row = await getGroupKeyApplicationById(env, applicationId);
  if (!row) return text("Application not found", 404);
  if (row.IssuedCodeId) return text("A privilege code has already been issued for this application", 409);

  const body = await readOptionalJson(request);
  const notePrefix = `Issued from application ${applicationId}`;
  const created = await createPrivilegeCodeRecord(request, env, auth, {
    role: "official_group",
    scopes: ROLE_SCOPES.official_group,
    label: cleanText(body.label, 120) || `${row.GroupName || row.ApplicantName} official group`,
    ownerName: cleanText(body.ownerName, 120) || row.ApplicantName || "",
    groupName: cleanText(body.groupName, 140) || row.GroupName || "",
    expiresAt: body.expiresAt || null,
    notes: cleanMultilineText(body.notes, 1000) || `${notePrefix}\n${row.SteamWorkshopUrl || ""}`,
  });

  const now = new Date().toISOString();
  await env.DB.prepare(`
    UPDATE GroupKeyApplications
    SET Status = 'issued', IssuedCodeId = ?, ReviewerNote = ?, UpdatedAt = ?, ReviewedAt = COALESCE(ReviewedAt, ?), IssuedAt = ?
    WHERE ApplicationId = ?
  `).bind(
    created.codeId,
    cleanMultilineText(body.reviewerNote, 4000) || row.ReviewerNote || "",
    now,
    now,
    now,
    applicationId
  ).run();

  await writeGroupKeyApplicationEvent(env, request, applicationId, auth.codeId, "issue_key", {
    codeId: created.codeId,
    groupName: row.GroupName || "",
  }, "");

  return json({
    success: true,
    applicationId,
    codeId: created.codeId,
    code: created.code,
    warning: "This raw code is shown once. Store it safely.",
  });
}

async function handleGroupKeyApplicationEvents(env, auth, applicationId, url) {
  const limit = clampInt(url.searchParams.get("limit"), 1, 300, 100);
  const { results } = await env.DB.prepare(`
    SELECT EventId, ApplicationId, ActorCodeId, Action, MetadataJson, PublicNote, IpHash, UserAgent, CreatedAt
    FROM GroupKeyApplicationEvents
    WHERE ApplicationId = ?
    ORDER BY CreatedAt DESC
    LIMIT ?
  `).bind(applicationId, limit).all();
  return json(results || []);
}

async function handleUpload(request, env) {
  const contentLength = request.headers.get("content-length");
  if (contentLength && parseInt(contentLength, 10) > MAX_UPLOAD_SIZE) {
    return text("Payload Too Large", 413);
  }

  const body = await request.json();
  const {
    PackageId,
    Language,
    ModName,
    LatestVersion,
    ModLastUpdated,
    UploaderID,
    Author,
    TranslationType,
    FileBase64,
    AdminToken,
    TargetModVersion,
    TranslationDate,
    IsSmartMerged,
    MergedAiCount,
    UpdateLog,
  } = body;

  if (!PackageId || !Language || !FileBase64) {
    return text("Invalid Payload", 400);
  }

  const auth = await authorizeToken(AdminToken, env, null, { allowAnonymous: true, request, action: "upload_authorize" });

  const finalType = normalizeTranslationType(TranslationType);
  if (finalType === "Official_Group" && !hasScope(auth, "upload:official")) {
    return text("Missing scope: upload:official", 403);
  }

  const isVerified = finalType === "Official_Group" && hasScope(auth, "upload:official") ? 1 : 0;
  const packageId = PackageId.toLowerCase();
  const now = new Date().toISOString();
  const recordId = `${packageId}_${Language}_${Date.now()}`;
  const fileKey = `${recordId}.zip`;
  const fileBuffer = base64ToUint8Array(FileBase64);
  const contributor = uploadContributor(body, auth, finalType);

  await env.BUCKET.put(fileKey, fileBuffer);

  await env.DB.prepare(`
    INSERT INTO TranslationRegistry
    (RecordId, PackageId, Language, ModName, LatestVersion, LastUpdated, ModLastUpdated,
     UploaderID, Author, TranslationType, IsVerified, FileUrl, TargetModVersion,
     TranslationDate, IsSmartMerged, MergedAiCount, UpdateLog, IsDeleted)
    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 0)
  `).bind(
    recordId,
    packageId,
    Language,
    ModName || "",
    LatestVersion || "Unknown",
    now,
    ModLastUpdated || now,
    contributor.uploaderId,
    contributor.author,
    finalType,
    isVerified,
    fileKey,
    TargetModVersion || "Unknown",
    TranslationDate || now,
    IsSmartMerged ? 1 : 0,
    MergedAiCount || 0,
    UpdateLog || ""
  ).run();

  if (ENABLE_EVENT_LOGS) {
    await writeUploadEvent(env, request, recordId, packageId, Language, UploaderID, "success", "");
  }
  if (ENABLE_UPLOAD_HISTORY_CLEANUP) {
    await cleanupOldRecords(env, packageId, Language, finalType);
  }

  return json({ success: true, recordId });
}

async function handleDelete(request, env, url) {
  const auth = await authorizeRequest(request, env, "record:delete:any");
  const parts = url.pathname.split("/");
  if (parts.length < 6) return text("Invalid URL", 400);

  const packageId = parts[4].toLowerCase();
  const language = parts[5];
  const recordId = url.searchParams.get("recordId");

  if (recordId) {
    await env.DB.prepare(
      "UPDATE TranslationRegistry SET IsDeleted = 1 WHERE RecordId = ?"
    ).bind(recordId).run();
    await writeModerationAction(env, recordId, auth.codeId, "soft_delete", {});
    return json({ success: true, message: `Record ${recordId} soft-deleted successfully.` });
  }

  requireScope(auth, "record:nuke");

  const records = await env.DB.prepare(
    "SELECT RecordId, FileUrl FROM TranslationRegistry WHERE PackageId = ? AND Language = ?"
  ).bind(packageId, language).all();

  for (const record of records.results || []) {
    await env.BUCKET.delete(record.FileUrl || `${record.RecordId}.zip`);
  }

  await env.DB.prepare(
    "DELETE FROM TranslationRegistry WHERE PackageId = ? AND Language = ?"
  ).bind(packageId, language).run();

  await writeModerationAction(env, null, auth.codeId, "hard_delete_package_language", {
    packageId,
    language,
    deletedCount: (records.results || []).length,
  });

  return json({ success: true, deletedCount: (records.results || []).length });
}

async function handleListPrivilegeCodes(env, auth, url) {
  const includeInactive = url.searchParams.get("includeInactive") === "true";
  const role = url.searchParams.get("role");
  const groupName = url.searchParams.get("groupName");
  const limit = clampInt(url.searchParams.get("limit"), 1, 200, 100);

  const filters = [];
  const values = [];
  if (!includeInactive) filters.push("IsActive = 1");
  if (role) {
    filters.push("Role = ?");
    values.push(role);
  }
  if (groupName) {
    filters.push("GroupName = ?");
    values.push(groupName);
  }

  const where = filters.length ? `WHERE ${filters.join(" AND ")}` : "";
  const { results } = await env.DB.prepare(`
    SELECT CodeId, Label, OwnerName, GroupName, Role, ScopesJson, IsActive, ExpiresAt,
           CreatedBy, CreatedAt, RevokedBy, RevokedAt, LastUsedAt, UsageCount, Notes
    FROM PrivilegeCodes
    ${where}
    ORDER BY CreatedAt DESC
    LIMIT ?
  `).bind(...values, limit).all();

  return json((results || []).map(formatPrivilegeCodeRow));
}

async function handleGetPrivilegeCode(env, auth, codeId) {
  const row = await getPrivilegeCodeById(env, codeId);
  if (!row) return text("Privilege code not found", 404);
  return json(formatPrivilegeCodeRow(row));
}

async function handleCreatePrivilegeCode(request, env, auth) {
  const body = await request.json();
  const created = await createPrivilegeCodeRecord(request, env, auth, body);
  return json({
    success: true,
    codeId: created.codeId,
    code: created.code,
    warning: "This raw code is shown once. Store it safely.",
  });
}

async function createPrivilegeCodeRecord(request, env, auth, body) {
  const role = body.role || "uploader";
  const scopes = Array.isArray(body.scopes) ? body.scopes : ROLE_SCOPES[role] || [];
  const rawCode = generatePrivilegeCode(role);
  const codeId = crypto.randomUUID();
  const codeHash = await hashPrivilegeCode(rawCode, env);

  await env.DB.prepare(`
    INSERT INTO PrivilegeCodes
    (CodeId, CodeHash, Label, OwnerName, GroupName, Role, ScopesJson, IsActive, ExpiresAt, CreatedBy, Notes)
    VALUES (?, ?, ?, ?, ?, ?, ?, 1, ?, ?, ?)
  `).bind(
    codeId,
    codeHash,
    body.label || role,
    body.ownerName || "",
    body.groupName || "",
    role,
    JSON.stringify(scopes),
    body.expiresAt || null,
    auth.codeId || "master",
    body.notes || ""
  ).run();

  await writePrivilegeEvent(env, request, codeId, auth.codeId, "create", { role, scopes });

  return {
    codeId,
    code: rawCode,
  };
}

async function handleRevokePrivilegeCode(request, env, auth, codeId) {
  const body = await readOptionalJson(request);
  const row = await getPrivilegeCodeById(env, codeId);
  if (!row) return text("Privilege code not found", 404);

  await env.DB.prepare(
    "UPDATE PrivilegeCodes SET IsActive = 0, RevokedBy = ?, RevokedAt = ? WHERE CodeId = ?"
  ).bind(auth.codeId || "master", new Date().toISOString(), codeId).run();

  await writePrivilegeEvent(env, request, codeId, auth.codeId, "revoke", { reason: body.reason || "" });
  return json({ success: true });
}

async function handlePausePrivilegeCode(request, env, auth, codeId) {
  const body = await readOptionalJson(request);
  const row = await getPrivilegeCodeById(env, codeId);
  if (!row) return text("Privilege code not found", 404);
  if (row.RevokedAt) return text("Revoked privilege codes cannot be paused.", 409);

  await env.DB.prepare(
    "UPDATE PrivilegeCodes SET IsActive = 0 WHERE CodeId = ?"
  ).bind(codeId).run();

  await writePrivilegeEvent(env, request, codeId, auth.codeId, "pause", { reason: body.reason || "" });
  return json({ success: true });
}

async function handleResumePrivilegeCode(request, env, auth, codeId) {
  const body = await readOptionalJson(request);
  const row = await getPrivilegeCodeById(env, codeId);
  if (!row) return text("Privilege code not found", 404);
  if (row.RevokedAt) return text("Revoked privilege codes cannot be resumed.", 409);

  await env.DB.prepare(
    "UPDATE PrivilegeCodes SET IsActive = 1 WHERE CodeId = ?"
  ).bind(codeId).run();

  await writePrivilegeEvent(env, request, codeId, auth.codeId, "resume", { reason: body.reason || "" });
  return json({ success: true });
}

async function handleUpdatePrivilegeCode(request, env, auth, codeId) {
  const body = await request.json();
  const row = await getPrivilegeCodeById(env, codeId);
  if (!row) return text("Privilege code not found", 404);

  const nextRole = body.role || row.Role;
  const nextScopes = Array.isArray(body.scopes)
    ? body.scopes
    : (body.role ? ROLE_SCOPES[nextRole] || [] : safeJsonArray(row.ScopesJson));

  await env.DB.prepare(`
    UPDATE PrivilegeCodes
    SET Label = ?, OwnerName = ?, GroupName = ?, Role = ?, ScopesJson = ?, ExpiresAt = ?, Notes = ?
    WHERE CodeId = ?
  `).bind(
    body.label ?? row.Label,
    body.ownerName ?? row.OwnerName ?? "",
    body.groupName ?? row.GroupName ?? "",
    nextRole,
    JSON.stringify(nextScopes),
    Object.prototype.hasOwnProperty.call(body, "expiresAt") ? body.expiresAt : row.ExpiresAt,
    body.notes ?? row.Notes ?? "",
    codeId
  ).run();

  await writePrivilegeEvent(env, request, codeId, auth.codeId, "update", {
    fields: Object.keys(body).filter((key) => ["label", "ownerName", "groupName", "role", "scopes", "expiresAt", "notes"].includes(key)),
  });

  const updated = await getPrivilegeCodeById(env, codeId);
  return json({ success: true, code: formatPrivilegeCodeRow(updated) });
}

async function handlePrivilegeCodeEvents(env, auth, codeId, url) {
  const limit = clampInt(url.searchParams.get("limit"), 1, 500, 200);
  const { results } = await env.DB.prepare(
    "SELECT * FROM PrivilegeCodeEvents WHERE CodeId = ? ORDER BY CreatedAt DESC LIMIT ?"
  ).bind(codeId, limit).all();
  return json(results || []);
}

async function handleRecentPrivilegeCodeUsage(env, auth, url) {
  const limit = clampInt(url.searchParams.get("limit"), 1, 500, 200);
  const { results } = await env.DB.prepare(`
    SELECT e.EventId, e.CodeId, c.Label, c.OwnerName, c.GroupName, c.Role,
           e.Action, e.IpHash, e.UserAgent, e.MetadataJson, e.CreatedAt
    FROM PrivilegeCodeEvents e
    LEFT JOIN PrivilegeCodes c ON c.CodeId = e.CodeId
    WHERE e.Action IN ('use', 'upload_authorize', 'download_authorize')
    ORDER BY e.CreatedAt DESC
    LIMIT ?
  `).bind(limit).all();
  return json(results || []);
}

async function authorizeRequest(request, env, requiredScope, url = null) {
  const token =
    request.headers.get("X-Admin-Token") ||
    request.headers.get("Authorization")?.replace(/^Bearer\s+/i, "") ||
    (url ? url.searchParams.get("token") : "");
  return authorizeToken(token, env, requiredScope, { request, action: "use" });
}

async function authorizeToken(token, env, requiredScope, options = {}) {
  if (!token) {
    if (options.allowAnonymous) return anonymousAuth();
    throw httpError(401, "Missing admin token");
  }

  if (env.MASTER_SECRET && token === env.MASTER_SECRET) {
    const auth = {
      codeId: "master",
      role: "master",
      scopes: ROLE_SCOPES.master,
      groupName: "master",
    };
    if (requiredScope) requireScope(auth, requiredScope);
    return auth;
  }

  if (parseSecretList(env.LEGACY_OFFICIAL_SECRETS).includes(token)) {
    const auth = {
      codeId: "legacy_official",
      role: "official_group",
      scopes: ROLE_SCOPES.official_group,
      groupName: "legacy",
    };
    if (requiredScope) requireScope(auth, requiredScope);
    return auth;
  }

  const codeHash = await hashPrivilegeCode(token, env);
  let row = null;
  try {
    row = await env.DB.prepare(
      "SELECT * FROM PrivilegeCodes WHERE CodeHash = ?"
    ).bind(codeHash).first();
  } catch (err) {
    if (options.allowAnonymous) return anonymousAuth();
    throw err;
  }

  if (!row || row.IsActive !== 1 || row.RevokedAt) {
    if (options.allowAnonymous) return anonymousAuth();
    throw httpError(401, "Invalid or revoked token");
  }

  if (row.ExpiresAt && new Date(row.ExpiresAt).getTime() < Date.now()) {
    throw httpError(401, "Expired token");
  }

  const auth = {
    codeId: row.CodeId,
    role: row.Role,
    scopes: uniqueArray([...(ROLE_SCOPES[row.Role] || []), ...safeJsonArray(row.ScopesJson)]),
    groupName: row.GroupName || "",
  };

  if (requiredScope) requireScope(auth, requiredScope);

  await recordPrivilegeCodeUse(env, options.request, row, requiredScope, options.action || "use");

  return auth;
}

function anonymousAuth() {
  return { codeId: null, role: "anonymous", scopes: [], groupName: "" };
}

function hasScope(auth, scope) {
  return auth && (auth.role === "master" || (auth.scopes || []).includes(scope));
}

function normalizeTranslationType(value) {
  return ["AI_Auto", "Manual", "Official_Group"].includes(value) ? value : "AI_Auto";
}

function requireScope(auth, scope) {
  if (!hasScope(auth, scope)) throw httpError(403, `Missing scope: ${scope}`);
}

async function getPrivilegeCodeById(env, codeId) {
  return env.DB.prepare(
    "SELECT CodeId, Label, OwnerName, GroupName, Role, ScopesJson, IsActive, ExpiresAt, CreatedBy, CreatedAt, RevokedBy, RevokedAt, LastUsedAt, UsageCount, Notes FROM PrivilegeCodes WHERE CodeId = ?"
  ).bind(codeId).first();
}

function formatPrivilegeCodeRow(row) {
  if (!row) return null;
  const scopes = uniqueArray([...(ROLE_SCOPES[row.Role] || []), ...safeJsonArray(row.ScopesJson)]);
  return {
    CodeId: row.CodeId,
    Label: row.Label,
    OwnerName: row.OwnerName,
    GroupName: row.GroupName,
    Role: row.Role,
    IsActive: row.IsActive === 1,
    ExpiresAt: row.ExpiresAt,
    CreatedBy: row.CreatedBy,
    CreatedAt: row.CreatedAt,
    RevokedBy: row.RevokedBy,
    RevokedAt: row.RevokedAt,
    LastUsedAt: row.LastUsedAt,
    UsageCount: row.UsageCount,
    Notes: row.Notes,
    Scopes: scopes,
    Status: row.RevokedAt ? "revoked" : (row.IsActive === 1 ? "active" : "paused"),
  };
}

async function getFeedbackById(env, feedbackId) {
  return env.DB.prepare("SELECT * FROM FeedbackReports WHERE FeedbackId = ?").bind(feedbackId).first();
}

function formatPublicFeedbackRow(row) {
  return {
    FeedbackId: row.FeedbackId,
    Category: row.Category,
    Status: row.Status,
    Severity: row.Severity,
    Title: row.Title,
    Summary: row.Summary,
    PackageId: row.PackageId,
    ModName: row.ModName,
    Language: row.Language,
    GameVersion: row.GameVersion,
    ModVersion: row.ModVersion,
    ReporterName: row.ReporterName,
    VoteCount: row.VoteCount || 0,
    CommentCount: row.CommentCount || 0,
    IsPinned: row.IsPinned === 1,
    CreatedAt: row.CreatedAt,
    LastActivityAt: row.LastActivityAt,
    ResolvedAt: row.ResolvedAt,
    AttachmentCount: row.AttachmentCount || 0,
  };
}

function formatAdminFeedbackRow(row) {
  return {
    ...formatPublicFeedbackRow(row),
    Body: row.Body || "",
    ReporterContact: row.ReporterContact || "",
    IsPublic: row.IsPublic === 1,
    AssignedTo: row.AssignedTo || "",
    DuplicateOf: row.DuplicateOf || "",
    ResolutionNote: row.ResolutionNote || "",
    UpdatedAt: row.UpdatedAt,
    AttachmentCount: row.AttachmentCount || 0,
  };
}

function formatPublicFeedbackEventRow(row) {
  return {
    EventId: row.EventId,
    Action: row.Action,
    ActorCodeId: row.ActorCodeId,
    PublicNote: row.PublicNote || "",
    CreatedAt: row.CreatedAt,
  };
}

async function getGroupKeyApplicationById(env, applicationId) {
  return env.DB.prepare("SELECT * FROM GroupKeyApplications WHERE ApplicationId = ?").bind(applicationId).first();
}

function formatPublicGroupKeyApplicationRow(row) {
  return {
    ApplicationId: row.ApplicationId,
    Status: row.Status,
    ApplicantName: row.ApplicantName,
    GroupName: row.GroupName,
    GameVersion: row.GameVersion,
    SteamWorkshopUrl: row.SteamWorkshopUrl,
    IssuedCodeId: row.IssuedCodeId || null,
    CreatedAt: row.CreatedAt,
    UpdatedAt: row.UpdatedAt,
    ReviewedAt: row.ReviewedAt,
    IssuedAt: row.IssuedAt,
    AttachmentCount: row.AttachmentCount || 0,
  };
}

function formatAdminGroupKeyApplicationRow(row) {
  return {
    ...formatPublicGroupKeyApplicationRow(row),
    Contact: row.Contact || "",
    PortfolioText: row.PortfolioText || "",
    ProofText: row.ProofText || "",
    ReviewerNote: row.ReviewerNote || "",
  };
}

async function saveRequestAttachments(env, ownerType, ownerId, attachments) {
  if (!attachments) return [];
  validateRequestAttachments(attachments);

  const saved = [];
  for (const attachment of attachments) {
    const fileName = sanitizeFileName(attachment.fileName || attachment.name || "attachment.bin");
    const contentType = cleanText(attachment.contentType || attachment.type || "application/octet-stream", 120) || "application/octet-stream";
    const base64 = String(attachment.base64 || attachment.data || "").replace(/^data:[^,]+,/, "");
    if (!base64) continue;

    const bytes = base64ToUint8Array(base64);
    if (!bytes.length) continue;

    const attachmentId = crypto.randomUUID();
    const r2Key = `attachments/${ownerType}/${ownerId}/${attachmentId}-${fileName}`;
    await env.BUCKET.put(r2Key, bytes, {
      httpMetadata: {
        contentType,
      },
    });

    await env.DB.prepare(`
      INSERT INTO RequestAttachments
      (AttachmentId, OwnerType, OwnerId, FileName, ContentType, SizeBytes, R2Key, IsPublic)
      VALUES (?, ?, ?, ?, ?, ?, ?, 0)
    `).bind(
      attachmentId,
      ownerType,
      ownerId,
      fileName,
      contentType,
      bytes.length,
      r2Key
    ).run();

    saved.push({
      AttachmentId: attachmentId,
      FileName: fileName,
      ContentType: contentType,
      SizeBytes: bytes.length,
    });
  }

  return saved;
}

function validateRequestAttachments(attachments) {
  if (!attachments) return;
  if (!Array.isArray(attachments)) throw httpError(400, "Attachments must be an array");
  if (attachments.length > MAX_ATTACHMENTS_PER_REQUEST) {
    throw httpError(413, `Too many attachments. Maximum is ${MAX_ATTACHMENTS_PER_REQUEST}.`);
  }

  let totalBytes = 0;
  for (const attachment of attachments) {
    const fileName = sanitizeFileName(attachment.fileName || attachment.name || "attachment.bin");
    const base64 = String(attachment.base64 || attachment.data || "").replace(/^data:[^,]+,/, "");
    if (!base64) continue;
    const estimatedBytes = estimateBase64Bytes(base64);
    if (estimatedBytes > MAX_ATTACHMENT_SIZE) throw httpError(413, `${fileName} is too large`);
    totalBytes += estimatedBytes;
    if (totalBytes > MAX_ATTACHMENT_TOTAL_SIZE) throw httpError(413, "Attachment total size is too large");
  }
}

function estimateBase64Bytes(base64) {
  const cleaned = String(base64 || "").replace(/\s+/g, "");
  if (!cleaned) return 0;
  const padding = cleaned.endsWith("==") ? 2 : cleaned.endsWith("=") ? 1 : 0;
  return Math.max(0, Math.floor((cleaned.length * 3) / 4) - padding);
}

async function listRequestAttachments(env, ownerType, ownerId) {
  const { results } = await env.DB.prepare(`
    SELECT AttachmentId, OwnerType, OwnerId, FileName, ContentType, SizeBytes, CreatedAt
    FROM RequestAttachments
    WHERE OwnerType = ? AND OwnerId = ?
    ORDER BY CreatedAt ASC
  `).bind(ownerType, ownerId).all();
  return (results || []).map(formatAttachmentRow);
}

async function countRequestAttachments(env, ownerType, ownerId) {
  try {
    const row = await env.DB.prepare(
      "SELECT COUNT(*) AS Count FROM RequestAttachments WHERE OwnerType = ? AND OwnerId = ?"
    ).bind(ownerType, ownerId).first();
    return row ? Number(row.Count || 0) : 0;
  } catch {
    return 0;
  }
}

async function attachAttachmentCounts(env, ownerType, rows, idField, formatter) {
  const formatted = rows.map(formatter);
  if (!rows.length) return formatted;
  try {
    const ids = rows.map((row) => row[idField]).filter(Boolean);
    if (!ids.length) return formatted;
    const placeholders = ids.map(() => "?").join(",");
    const { results } = await env.DB.prepare(`
      SELECT OwnerId, COUNT(*) AS Count
      FROM RequestAttachments
      WHERE OwnerType = ? AND OwnerId IN (${placeholders})
      GROUP BY OwnerId
    `).bind(ownerType, ...ids).all();
    const counts = new Map((results || []).map((row) => [row.OwnerId, Number(row.Count || 0)]));
    return formatted.map((item, index) => ({
      ...item,
      AttachmentCount: counts.get(rows[index][idField]) || 0,
    }));
  } catch {
    return formatted;
  }
}

function formatAttachmentRow(row) {
  return {
    AttachmentId: row.AttachmentId,
    OwnerType: row.OwnerType,
    OwnerId: row.OwnerId,
    FileName: row.FileName,
    ContentType: row.ContentType,
    SizeBytes: row.SizeBytes || 0,
    CreatedAt: row.CreatedAt,
    DownloadUrl: `/api/v1/admin/attachments/${encodeURIComponent(row.AttachmentId)}`,
  };
}

async function handleDownloadAttachment(env, auth, attachmentId) {
  const row = await env.DB.prepare(
    "SELECT * FROM RequestAttachments WHERE AttachmentId = ?"
  ).bind(attachmentId).first();
  if (!row) return text("Attachment not found", 404);

  if (row.OwnerType === "feedback") requireScope(auth, "feedback:read_private");
  if (row.OwnerType === "application") requireScope(auth, "application:read_private");

  const object = await env.BUCKET.get(row.R2Key);
  if (!object) return text("Attachment object not found", 404);

  const headers = new Headers(CORS_HEADERS);
  headers.set("Content-Type", row.ContentType || "application/octet-stream");
  headers.set("Content-Disposition", `attachment; filename="${contentDispositionFileName(row.FileName || "attachment.bin")}"`);
  headers.set("Cache-Control", "private, no-store");
  return createResponse(object.body, 200, headers);
}

function sanitizeFileName(value) {
  const cleaned = String(value || "attachment.bin")
    .replace(/[\\/:*?"<>|]+/g, "_")
    .replace(/\s+/g, " ")
    .trim()
    .slice(0, 120);
  return cleaned || "attachment.bin";
}

function contentDispositionFileName(value) {
  return sanitizeFileName(value).replace(/"/g, "");
}

function isLikelySteamWorkshopUrl(value) {
  try {
    const url = new URL(String(value || ""));
    const host = url.hostname.toLowerCase();
    return (host === "steamcommunity.com" || host.endsWith(".steamcommunity.com")) && url.pathname.includes("/sharedfiles/filedetails/");
  } catch {
    return false;
  }
}

function publicContributorKind(row) {
  return row && (row.IsVerified === 1 || row.IsVerified === true || row.TranslationType === "Official_Group") ? "group" : "player";
}

function publicContributorName(row) {
  const group = publicContributorKind(row) === "group";
  const candidates = group
    ? [row.Author, row.UploaderID, row.GroupName, row.OwnerName]
    : [row.UploaderID, row.Author, row.OwnerName];
  const name = candidates.map((value) => String(value || "").trim()).find((value) => value && !isAnonymousPublicName(value));
  if (looksLikeOpaquePublicId(name)) {
    return `${group ? "Translation group" : "Anonymous player"} #${name.slice(0, 6)}`;
  }
  return name || (group ? "Official translation group" : "Unsigned player");
}

function uploadContributor(body, auth, finalType) {
  const group = finalType === "Official_Group" && hasScope(auth, "upload:official");
  const groupName = cleanText(body.GroupName || auth.groupName, 120);
  const ownerName = cleanText(body.OwnerName || body.TranslatorName || body.UploaderName, 120);
  const uploader = cleanText(body.UploaderID || body.UploaderName || body.PlayerName, 120);
  const author = cleanText(body.Author, 160);

  if (group) {
    return {
      uploaderId: ownerName || uploader || auth.codeId || "Official_Group",
      author: groupName || author || ownerName || "Official translation group",
    };
  }

  return {
    uploaderId: uploader || ownerName || author || "Anonymous",
    author: author || uploader || ownerName || "AI Translation Network",
  };
}

function isAnonymousPublicName(value) {
  return ["anonymous", "autotranslation core", "auto translation core", "unknown", "-"].includes(String(value || "").trim().toLowerCase());
}

function looksLikeOpaquePublicId(value) {
  return /^[a-f0-9]{24,}$/i.test(String(value || "").trim());
}

async function readOptionalJson(request) {
  try {
    const textBody = await request.text();
    return textBody ? JSON.parse(textBody) : {};
  } catch {
    return {};
  }
}

function clampInt(value, min, max, fallback) {
  const parsed = parseInt(value, 10);
  if (!Number.isFinite(parsed)) return fallback;
  return Math.min(max, Math.max(min, parsed));
}

function normalizeChoice(value, allowed, fallback) {
  const normalized = String(value || "").trim();
  return allowed.includes(normalized) ? normalized : fallback;
}

function cleanText(value, maxLength) {
  return String(value || "")
    .replace(/\s+/g, " ")
    .trim()
    .slice(0, maxLength);
}

function cleanMultilineText(value, maxLength) {
  return String(value || "")
    .replace(/\r\n/g, "\n")
    .replace(/\r/g, "\n")
    .replace(/[ \t]+\n/g, "\n")
    .replace(/\n{4,}/g, "\n\n\n")
    .trim()
    .slice(0, maxLength);
}

function boolToInt(value, fallback) {
  if (typeof value === "boolean") return value ? 1 : 0;
  if (value === 0 || value === 1) return value;
  return fallback === 1 || fallback === true ? 1 : 0;
}

async function recordPrivilegeCodeUse(env, request, row, requiredScope, action) {
  const now = new Date().toISOString();
  await tryOptionalDbWrite(
    env.DB.prepare("UPDATE PrivilegeCodes SET LastUsedAt = ?, UsageCount = UsageCount + 1 WHERE CodeId = ?").bind(now, row.CodeId),
    "privilege token usage"
  );

  if (request) {
    await writePrivilegeEvent(env, request, row.CodeId, row.CodeId, action || "use", {
      requiredScope: requiredScope || "",
      role: row.Role,
      groupName: row.GroupName || "",
    });
  }
}

async function hashPrivilegeCode(code, env) {
  const normalized = String(code || "").trim();
  const pepper = env.TOKEN_HASH_PEPPER || "";
  return sha256Hex(new TextEncoder().encode(`${pepper}:${normalized}`));
}

async function sha256Hex(bytes) {
  const digest = await crypto.subtle.digest("SHA-256", bytes);
  return [...new Uint8Array(digest)].map((b) => b.toString(16).padStart(2, "0")).join("");
}

function generatePrivilegeCode(role) {
  const bytes = new Uint8Array(24);
  crypto.getRandomValues(bytes);
  const suffix = btoa(String.fromCharCode(...bytes)).replace(/[^a-zA-Z0-9]/g, "").slice(0, 28);
  return `ATC_${role.toUpperCase()}_${suffix}`;
}

function base64ToUint8Array(base64) {
  return Uint8Array.from(atob(base64), (c) => c.charCodeAt(0));
}

function safeJsonArray(value) {
  try {
    const parsed = JSON.parse(value || "[]");
    return Array.isArray(parsed) ? parsed : [];
  } catch {
    return [];
  }
}

function parseSecretList(value) {
  return String(value || "")
    .split(",")
    .map((item) => item.trim())
    .filter(Boolean);
}

function uniqueArray(values) {
  return [...new Set((values || []).filter(Boolean))];
}

async function cleanupOldRecords(env, packageId, language, finalType) {
  const limits = { Official_Group: 3, Manual: 2, AI_Auto: 1 };
  const keepCount = limits[finalType] || 1;
  const history = await env.DB.prepare(
    "SELECT RecordId, FileUrl FROM TranslationRegistry WHERE PackageId = ? AND Language = ? AND TranslationType = ? AND IsDeleted IS NOT 1 ORDER BY LastUpdated DESC"
  ).bind(packageId, language, finalType).all();

  const records = history.results || [];
  if (records.length <= keepCount) return;

  for (const rec of records.slice(keepCount)) {
    await env.DB.prepare("UPDATE TranslationRegistry SET IsDeleted = 1 WHERE RecordId = ?").bind(rec.RecordId).run();
  }
}

async function purgeSoftDeletedRecords(env) {
  const cutoff = new Date(Date.now() - 7 * 24 * 60 * 60 * 1000).toISOString();
  const trash = await env.DB.prepare(
    "SELECT RecordId, FileUrl FROM TranslationRegistry WHERE IsDeleted = 1 AND LastUpdated < ?"
  ).bind(cutoff).all();

  for (const item of trash.results || []) {
    await env.BUCKET.delete(item.FileUrl || `${item.RecordId}.zip`);
    await env.DB.prepare("DELETE FROM TranslationRegistry WHERE RecordId = ?").bind(item.RecordId).run();
  }
}

async function writeUploadEvent(env, request, recordId, packageId, language, uploaderId, status, errorMessage) {
  await tryOptionalDbWrite(env.DB.prepare(`
    INSERT INTO UploadEvents
    (EventId, RecordId, PackageId, Language, UploaderIdHash, IpHash, UserAgent, Status, ErrorMessage)
    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
  `).bind(
    crypto.randomUUID(),
    recordId,
    packageId,
    language,
    uploaderId ? await sha256Hex(new TextEncoder().encode(String(uploaderId))) : null,
    await requestIpHash(request),
    request.headers.get("user-agent") || "",
    status,
    errorMessage || ""
  ), "upload event");
}

async function writeDownloadEvent(env, request, record) {
  await tryOptionalDbWrite(env.DB.prepare(`
    INSERT INTO DownloadEvents
    (EventId, RecordId, PackageId, Language, IpHash)
    VALUES (?, ?, ?, ?, ?)
  `).bind(
    crypto.randomUUID(),
    record.RecordId,
    record.PackageId,
    record.Language,
    await requestIpHash(request)
  ), "download event");
}

async function writeModerationAction(env, recordId, adminId, action, metadata) {
  await tryOptionalDbWrite(env.DB.prepare(`
    INSERT INTO ModerationActions
    (ActionId, RecordId, AdminId, Action, MetadataJson)
    VALUES (?, ?, ?, ?, ?)
  `).bind(
    crypto.randomUUID(),
    recordId,
    adminId || "",
    action,
    JSON.stringify(metadata || {})
  ), "moderation action");
}

async function writeFeedbackEvent(env, request, feedbackId, actorCodeId, action, metadata, publicNote, isPublic) {
  await tryOptionalDbWrite(env.DB.prepare(`
    INSERT INTO FeedbackEvents
    (EventId, FeedbackId, ActorCodeId, Action, MetadataJson, PublicNote, IsPublic, IpHash, UserAgent)
    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
  `).bind(
    crypto.randomUUID(),
    feedbackId,
    actorCodeId || null,
    action,
    JSON.stringify(metadata || {}),
    publicNote || "",
    isPublic ? 1 : 0,
    await requestIpHash(request),
    request.headers.get("user-agent") || ""
  ), "feedback event");
}

async function writeGroupKeyApplicationEvent(env, request, applicationId, actorCodeId, action, metadata, publicNote) {
  await tryOptionalDbWrite(env.DB.prepare(`
    INSERT INTO GroupKeyApplicationEvents
    (EventId, ApplicationId, ActorCodeId, Action, MetadataJson, PublicNote, IpHash, UserAgent)
    VALUES (?, ?, ?, ?, ?, ?, ?, ?)
  `).bind(
    crypto.randomUUID(),
    applicationId,
    actorCodeId || null,
    action,
    JSON.stringify(metadata || {}),
    publicNote || "",
    await requestIpHash(request),
    request.headers.get("user-agent") || ""
  ), "group key application event");
}

async function writePrivilegeEvent(env, request, codeId, actorCodeId, action, metadata) {
  await tryOptionalDbWrite(env.DB.prepare(`
    INSERT INTO PrivilegeCodeEvents
    (EventId, CodeId, ActorCodeId, Action, IpHash, UserAgent, MetadataJson)
    VALUES (?, ?, ?, ?, ?, ?, ?)
  `).bind(
    crypto.randomUUID(),
    codeId || null,
    actorCodeId || null,
    action,
    await requestIpHash(request),
    request.headers.get("user-agent") || "",
    JSON.stringify(metadata || {})
  ), "privilege event");
}

async function tryOptionalDbWrite(statement, label) {
  try {
    return await statement.run();
  } catch (err) {
    console.warn(`Optional DB write skipped (${label}):`, err && err.message ? err.message : err);
    return null;
  }
}

async function requestIpHash(request) {
  const ip = request.headers.get("cf-connecting-ip") || "";
  return ip ? sha256Hex(new TextEncoder().encode(ip)) : null;
}

function json(value, status = 200) {
  return createResponse(JSON.stringify(value), status, { "Content-Type": "application/json; charset=utf-8", ...CORS_HEADERS });
}

function text(value, status = 200) {
  return createResponse(value, status, CORS_HEADERS);
}

function createResponse(body, status = 200, headers = CORS_HEADERS) {
  /** @type {ResponseInit} */
  const init = {
    status,
    headers,
  };
  return Reflect.construct(Response, [body, init]);
}

class HttpStatusError extends Error {
  constructor(status, message) {
    super(message);
    this.name = "HttpStatusError";
    Object.defineProperty(this, "httpStatus", {
      value: status,
      enumerable: true,
      configurable: true,
    });
  }
}

function normalizeError(err) {
  if (err instanceof HttpStatusError) {
    const status = Reflect.get(err, "httpStatus");
    return { status: Number.isInteger(status) ? status : 500, message: err.message };
  }

  if (err && typeof err === "object") {
    const maybeStatus = Reflect.get(err, "httpStatus");
    const status = Number.isInteger(maybeStatus) ? maybeStatus : 500;
    const maybeMessage = Reflect.get(err, "message");
    const message = typeof maybeMessage === "string" ? maybeMessage : String(err);
    return { status, message };
  }

  return { status: 500, message: String(err) };
}

function httpError(status, message) {
  return new HttpStatusError(status, message);
}
