const API_BASE = window.RWMOD_API_BASE || "";
const TURNSTILE_SITE_KEY = window.RWMOD_TURNSTILE_SITE_KEY || "";
const LOCAL_PREVIEW_HOSTS = new Set(["127.0.0.1", "localhost", "::1"]);
const REPORT_PREVIEW_MODE = window.RWMOD_REPORT_PREVIEW === true
  || (!API_BASE && LOCAL_PREVIEW_HOSTS.has(window.location.hostname));
const SEARCH_DEBOUNCE_MS = 180;

let fallbackMods = [];

const state = {
  allMods: [],
  indexMods: [],
  visibleMods: [],
  details: new Map(),
  query: "",
  filter: "all",
  selectedPackageId: "",
  detailPriority: false,
  dataMode: "loading",
  apiError: "",
  searchTimer: 0,
  restoringUrl: false,
  turnstileToken: "",
  turnstileWidgetId: null,
  turnstileLoading: false,
};

const els = {
  searchInput: document.getElementById("searchInput"),
  pasteButton: document.getElementById("pasteButton"),
  clearButton: document.getElementById("clearButton"),
  headerSearchButton: document.getElementById("headerSearchButton"),
  apiStatus: document.getElementById("apiStatus"),
  apiHint: document.getElementById("apiHint"),
  resultList: document.getElementById("resultList"),
  resultCount: document.getElementById("resultCount"),
  metricMods: document.getElementById("metricMods"),
  metricTranslated: document.getElementById("metricTranslated"),
  metricRisks: document.getElementById("metricRisks"),
  metricHeavy: document.getElementById("metricHeavy"),
  detailEmpty: document.getElementById("detailEmpty"),
  detailCard: document.getElementById("detailCard"),
  detailTitle: document.getElementById("detailTitle"),
  detailPackage: document.getElementById("detailPackage"),
  detailVersions: document.getElementById("detailVersions"),
  detailLocalization: document.getElementById("detailLocalization"),
  detailPerformance: document.getElementById("detailPerformance"),
  detailCompatibility: document.getElementById("detailCompatibility"),
  detailConfidence: document.getElementById("detailConfidence"),
  detailPrimaryStatus: document.getElementById("detailPrimaryStatus"),
  detailTrust: document.getElementById("detailTrust"),
  detailWarning: document.getElementById("detailWarning"),
  localizationList: document.getElementById("localizationList"),
  compatibilityList: document.getElementById("compatibilityList"),
  performanceList: document.getElementById("performanceList"),
  sourceList: document.getElementById("sourceList"),
  openPrimarySource: document.getElementById("openPrimarySource"),
  copyPackageButton: document.getElementById("copyPackageButton"),
  copyLinkButton: document.getElementById("copyLinkButton"),
  openReportButton: document.getElementById("openReportButton"),
  reportDialog: document.getElementById("reportDialog"),
  reportPackage: document.getElementById("reportPackage"),
  reportType: document.getElementById("reportType"),
  reportLanguage: document.getElementById("reportLanguage"),
  reportGameVersion: document.getElementById("reportGameVersion"),
  reportImpact: document.getElementById("reportImpact"),
  reportSummary: document.getElementById("reportSummary"),
  reportFormStatus: document.getElementById("reportFormStatus"),
  reportStatus: document.getElementById("reportStatus"),
  turnstileSlot: document.getElementById("turnstileSlot"),
  analyzeButton: document.getElementById("analyzeButton"),
  modlistInput: document.getElementById("modlistInput"),
  analyzerStatus: document.getElementById("analyzerStatus"),
  analyzerSummary: document.getElementById("analyzerSummary"),
  analyzerResults: document.getElementById("analyzerResults"),
};

function getField(row, pascal, camel, fallback = "") {
  if (!row) return fallback;
  if (Object.prototype.hasOwnProperty.call(row, pascal)) return row[pascal] ?? fallback;
  if (Object.prototype.hasOwnProperty.call(row, camel)) return row[camel] ?? fallback;
  return fallback;
}

function normalize(value) {
  return String(value || "").trim().toLowerCase();
}

function normalizePackageId(value) {
  return normalize(value).replace(/\s+/g, "");
}

function extractWorkshopId(value) {
  const raw = String(value || "").trim();
  if (!raw) return "";

  const queryMatch = raw.match(/\b[?&]?id=(\d{6,})\b/i);
  if (queryMatch) return queryMatch[1];

  if (/^\d{6,}$/.test(raw)) return raw;

  const urlText = /^https?:\/\//i.test(raw) ? raw : `https://${raw}`;
  try {
    const url = new URL(urlText);
    const isSteamWorkshop = url.hostname.toLowerCase().endsWith("steamcommunity.com")
      && /\/(?:sharedfiles|workshop)\/filedetails/i.test(url.pathname);
    const id = url.searchParams.get("id") || "";
    if (isSteamWorkshop && /^\d{6,}$/.test(id)) return id;
  } catch {
    // Keep paste handling forgiving; non-URL inputs fall through to package search.
  }

  return "";
}

function apiSearchQuery(value) {
  return extractWorkshopId(value) || String(value || "").trim();
}

function findModByIdentifier(value, collection = []) {
  const packageId = normalizePackageId(value);
  const workshopId = extractWorkshopId(value);
  return collection.find((mod) => {
    const modWorkshopId = normalize(mod.PrimaryWorkshopId);
    return mod.PackageId === packageId
      || (workshopId && modWorkshopId === workshopId)
      || (!workshopId && packageId && modWorkshopId === packageId);
  }) || null;
}

function decodeCommonEntities(value) {
  return String(value || "")
    .replace(/&lt;/gi, "<")
    .replace(/&gt;/gi, ">")
    .replace(/&amp;/gi, "&")
    .replace(/&quot;/gi, "\"")
    .replace(/&#39;/gi, "'");
}

function isPackageIdCandidate(value) {
  const candidate = normalizePackageId(value);
  return /^[a-z0-9][a-z0-9_.-]*\.[a-z0-9][a-z0-9_.-]*$/i.test(candidate)
    && /[a-z]/i.test(candidate)
    && !/^\d+(?:\.\d+)+$/.test(candidate);
}

function normalizeModlistLine(line) {
  let clean = decodeCommonEntities(line)
    .replace(/<!--[\s\S]*?-->/g, " ")
    .replace(/<[^>]+>/g, " ")
    .replace(/\s+(?:#|\/\/).*/, "")
    .trim();

  for (let i = 0; i < 4; i += 1) {
    clean = clean
      .replace(/^\s*(?:[-*•]+|[✓✔☑]+|\[[xX ]?\])\s*/, "")
      .replace(/^\s*(?:\(?\d+\)?|\[\d+\])\s*(?:[.:)\]-]+|\s+-\s+|\s*:\s*)\s*/, "")
      .replace(/^\s*(?:load\s*order|sort\s*order|order|package\s*id|packageid|mod\s*id|id)\s*[:=]\s*/i, "")
      .trim();
  }

  return clean;
}

function dedupeIdentifiers(items) {
  const seen = new Set();
  const result = [];
  items.forEach((item) => {
    const clean = String(item || "").trim();
    if (!clean || seen.has(clean)) return;
    seen.add(clean);
    result.push(clean);
  });
  return result;
}

function extractIdentifierFromLine(line) {
  const clean = normalizeModlistLine(line);
  if (!clean) return [];

  const workshopId = extractWorkshopId(clean);
  if (workshopId) return [workshopId];

  const tokens = clean.match(/(?:https?:\/\/)?steamcommunity\.com\/sharedfiles\/filedetails\/\?\S+|\b\d{6,}\b|\b[a-z0-9][a-z0-9_.-]*\.[a-z0-9][a-z0-9_.-]*\b/gi) || [];
  return dedupeIdentifiers(tokens.map((token) => {
    const id = extractWorkshopId(token);
    if (id) return id;
    const packageId = normalizePackageId(token.replace(/[,"'()[\]{}<>]+$/g, ""));
    return isPackageIdCandidate(packageId) ? packageId : "";
  }).filter(Boolean));
}

function extractXmlPackageIds(rawText) {
  const text = decodeCommonEntities(rawText);
  const ids = [];
  const liPattern = /<li\b[^>]*>([\s\S]*?)<\/li>/gi;
  let match;
  while ((match = liPattern.exec(text)) !== null) {
    ids.push(...extractIdentifierFromLine(match[1]));
  }
  return dedupeIdentifiers(ids);
}

function parseModlistInput(rawText) {
  const text = String(rawText || "");
  const inputLines = text.split(/\r?\n/).map((line) => line.trim()).filter(Boolean);
  const xmlIds = extractXmlPackageIds(text);
  const identifiers = xmlIds.length
    ? xmlIds
    : dedupeIdentifiers(inputLines.flatMap(extractIdentifierFromLine));

  return {
    identifiers,
    inputLineCount: inputLines.length,
    mode: xmlIds.length ? "ModsConfig.xml" : "mixed text",
  };
}

function packageIdForReport(rawValue, reportKind) {
  const normalized = normalizePackageId(rawValue);
  if (reportKind !== "missing_mod") return normalized;
  const workshopId = extractWorkshopId(rawValue);
  if (workshopId && (!normalized || normalized === workshopId || /steamcommunity\.com/i.test(String(rawValue)))) return "";
  return normalized;
}

function catalogIndex() {
  const seen = new Set();
  return [
    ...state.allMods,
    ...state.indexMods,
    ...state.details.values(),
  ].filter((mod) => {
    if (!mod?.PackageId || seen.has(mod.PackageId)) return false;
    seen.add(mod.PackageId);
    return true;
  });
}

function readUrlState() {
  const params = new URLSearchParams(window.location.search);
  return {
    query: params.get("q") || "",
    filter: params.get("filter") || "all",
    mod: normalizePackageId(params.get("mod")),
    hasModParam: params.has("mod"),
  };
}

function writeUrlState({ replace = false } = {}) {
  if (state.restoringUrl) return;
  const params = new URLSearchParams();
  if (state.query.trim()) params.set("q", state.query.trim());
  if (state.filter && state.filter !== "all") params.set("filter", state.filter);
  if (state.detailPriority && state.selectedPackageId) params.set("mod", state.selectedPackageId);
  const nextUrl = `${window.location.pathname}${params.toString() ? `?${params.toString()}` : ""}${window.location.hash || ""}`;
  const currentUrl = `${window.location.pathname}${window.location.search}${window.location.hash}`;
  if (nextUrl === currentUrl) return;
  window.history[replace ? "replaceState" : "pushState"](
    { q: state.query, filter: state.filter, mod: state.selectedPackageId },
    "",
    nextUrl
  );
}

function syncDetailPriority() {
  document.body.classList.toggle("has-selected-mod", state.detailPriority && Boolean(state.selectedPackageId));
}

function applyFilterButtonState() {
  document.querySelectorAll(".filter-chip").forEach((item) => {
    item.classList.toggle("is-active", item.dataset.filter === state.filter);
  });
}

function applyUrlState({ replace = true } = {}) {
  const next = readUrlState();
  state.restoringUrl = true;
  state.query = next.query;
  state.filter = next.filter || "all";
  state.selectedPackageId = next.mod;
  state.detailPriority = next.hasModParam && Boolean(next.mod);
  els.searchInput.value = state.query;
  applyFilterButtonState();
  syncDetailPriority();
  state.restoringUrl = false;
  writeUrlState({ replace });
}

function modDetailUrl(packageId = state.selectedPackageId) {
  const url = new URL(window.location.href);
  url.searchParams.delete("q");
  url.searchParams.delete("filter");
  if (packageId) url.searchParams.set("mod", packageId);
  return url.toString();
}

function titleCase(value) {
  return String(value || "unknown")
    .replace(/_/g, " ")
    .replace(/\s+/g, " ")
    .trim()
    .replace(/\b\w/g, (char) => char.toUpperCase());
}

function statusClass(value) {
  const normalized = normalize(value).replace(/\s+/g, "_");
  if (["complete", "mostly_complete", "ok", "no_known_conflict", "light", "official", "rwmod_verified", "trusted_group", "high"].includes(normalized)) return "safe";
  if (["partial", "warning", "load_order_note", "needs_dependency", "medium", "cloud_record"].includes(normalized)) return "warn";
  if (["missing", "conflict", "hard_conflict", "heavy", "critical"].includes(normalized)) return "danger";
  if (["soft_conflict", "version_mismatch", "outdated"].includes(normalized)) return "warn";
  return "unknown";
}

function publicCompatibilityLabel(value) {
  const normalized = normalize(value).replace(/\s+/g, "_");
  if (normalized === "ok" || normalized === "no_known_conflict") return "No known conflict";
  if (normalized === "warning") return "Needs review";
  if (normalized === "conflict" || normalized === "hard_conflict") return "Conflict risk";
  if (normalized === "unknown") return "Unknown";
  return titleCase(value);
}

function mapMod(row) {
  const packageId = normalizePackageId(getField(row, "PackageId", "packageId"));
  const versions = getField(row, "SupportedGameVersions", "versions", []);
  const safeVersions = Array.isArray(versions) ? versions : [];
  return {
    PackageId: packageId,
    DisplayPackageId: getField(row, "DisplayPackageId", "displayPackageId", packageId),
    PrimaryWorkshopId: getField(row, "PrimaryWorkshopId", "workshopId"),
    ModName: getField(row, "ModName", "name", packageId || "Unknown mod"),
    Author: getField(row, "Author", "author"),
    Summary: getField(row, "Summary", "summary"),
    SupportedGameVersions: safeVersions,
    LatestKnownVersion: getField(row, "LatestKnownVersion", "latestKnownVersion", "Unknown"),
    LocalizationStatus: normalize(getField(row, "LocalizationStatus", "localizationStatus", "unknown")),
    CompatibilityStatus: normalize(getField(row, "CompatibilityStatus", "compatibilityStatus", "unknown")),
    PerformanceImpact: normalize(getField(row, "PerformanceImpact", "performanceImpact", "unknown")),
    TrustLevel: normalize(getField(row, "TrustLevel", "trustLevel", "unknown")),
    Confidence: normalize(getField(row, "Confidence", "confidence", "low")),
    LocalizationCount: Number(getField(row, "LocalizationCount", "localizationCount", 0) || 0),
    CompatibilityReportCount: Number(getField(row, "CompatibilityReportCount", "compatibilityReportCount", 0) || 0),
    PerformanceReportCount: Number(getField(row, "PerformanceReportCount", "performanceReportCount", 0) || 0),
    TotalDownloadCount: Number(getField(row, "TotalDownloadCount", "totalDownloadCount", 0) || 0),
    DataSource: getField(row, "DataSource", "dataSource", "rwmod_catalog"),
    Sources: getField(row, "Sources", "sources", []),
    Localization: getField(row, "Localization", "localization", []),
    Compatibility: getField(row, "Compatibility", "compatibility", []),
    Performance: getField(row, "Performance", "performance", []),
  };
}

function buildApiUrl(path, params = {}) {
  const url = new URL(`${API_BASE}${path}`, window.location.origin);
  Object.entries(params).forEach(([key, value]) => {
    if (value !== undefined && value !== null && String(value).trim()) {
      url.searchParams.set(key, value);
    }
  });
  return url;
}

async function fetchJson(path, params) {
  const response = await fetch(buildApiUrl(path, params), {
    headers: { Accept: "application/json" },
  });
  if (!response.ok) throw new Error(`HTTP ${response.status}`);
  return response.json();
}

async function postJson(path, payload) {
  const response = await fetch(buildApiUrl(path), {
    method: "POST",
    headers: {
      Accept: "application/json",
      "Content-Type": "application/json",
    },
    body: JSON.stringify(payload),
  });
  const text = await response.text();
  let body = null;
  try {
    body = text ? JSON.parse(text) : null;
  } catch {
    body = { error: text };
  }
  if (!response.ok) {
    const message = body?.error || body?.message || text || `HTTP ${response.status}`;
    throw new Error(message);
  }
  return body;
}

async function loadFallbackFixture() {
  if (fallbackMods.length) return fallbackMods;
  const response = await fetch("./mock-api-data.json", {
    headers: { Accept: "application/json" },
  });
  if (!response.ok) throw new Error(`Fallback fixture HTTP ${response.status}`);
  const payload = await response.json();
  fallbackMods = Array.isArray(payload) ? payload : payload.Items || payload.items || [];
  return fallbackMods;
}

async function loadModsFromApi(query = state.query) {
  const payload = await fetchJson("/api/v1/rwmod/mods", {
    q: apiSearchQuery(query),
    limit: 80,
  });
  const rows = Array.isArray(payload) ? payload : payload.Items || payload.items || [];
  return rows.map(mapMod).filter((mod) => mod.PackageId);
}

async function loadModsFromFallback(query = state.query) {
  const fixture = await loadFallbackFixture();
  const q = normalize(query);
  const packageQuery = normalizePackageId(query);
  const workshopId = extractWorkshopId(query);
  return fixture
    .map(mapMod)
    .filter((mod) => {
      if (findModByIdentifier(query, [mod])) return true;
      const haystack = [
        mod.ModName,
        mod.PackageId,
        mod.DisplayPackageId,
        mod.PrimaryWorkshopId,
        mod.Author,
        mod.LocalizationStatus,
        mod.CompatibilityStatus,
        mod.PerformanceImpact,
      ].map(normalize).join(" ");
      return !q || haystack.includes(q) || (packageQuery && haystack.includes(packageQuery)) || (workshopId && haystack.includes(workshopId));
    });
}

async function refreshCatalog() {
  setApiState("loading", "Data link scanning", "Searching RWMod API.");
  try {
    const mods = await loadModsFromApi();
    state.allMods = mods;
    if (!state.query) state.indexMods = mods;
    if (state.query && !state.indexMods.length) {
      state.indexMods = await loadModsFromApi("");
    }
    state.dataMode = "live";
    state.apiError = "";
    setApiState("live", "Live API", `${mods.length} mod rows from Worker.`);
  } catch (err) {
    state.indexMods = await loadModsFromFallback("");
    state.allMods = await loadModsFromFallback();
    state.dataMode = "fallback";
    state.apiError = err.message || "API unavailable";
    setApiState("fallback", "Offline preview", "Worker API unavailable. Using local preview data.");
  }

  if (!state.selectedPackageId || !state.allMods.some((mod) => mod.PackageId === state.selectedPackageId)) {
    state.selectedPackageId = state.allMods[0]?.PackageId || "";
  }
  syncDetailPriority();
  writeUrlState({ replace: true });
  renderAll();
  if (state.selectedPackageId) loadDetail(state.selectedPackageId);
}

async function loadDetail(packageId) {
  if (!packageId) return;
  const current = state.details.get(packageId) || state.allMods.find((mod) => mod.PackageId === packageId);

  if (state.dataMode !== "live") {
    state.details.set(packageId, current);
    renderDetail(current);
    return;
  }

  renderDetail(current, true);
  try {
    const payload = await fetchJson(`/api/v1/rwmod/mods/${encodeURIComponent(packageId)}`);
    const detail = { ...current, ...mapMod(payload) };
    detail.Sources = payload.Sources || payload.sources || detail.Sources || [];
    detail.Localization = payload.Localization || payload.localization || detail.Localization || [];
    detail.Compatibility = payload.Compatibility || payload.compatibility || detail.Compatibility || [];
    detail.Performance = payload.Performance || payload.performance || detail.Performance || [];
    state.details.set(packageId, detail);
    renderDetail(detail);
  } catch {
    state.details.set(packageId, current);
    renderDetail(current);
  }
}

function setApiState(mode, label, hint) {
  els.apiStatus.textContent = label;
  els.apiStatus.className = `status-dot ${mode === "live" ? "safe" : mode === "loading" ? "info" : "unknown"}`;
  els.apiHint.textContent = hint;
  syncReportModeStatus();
}

function passesFilter(mod) {
  if (state.filter === "translated") return ["complete", "mostly_complete"].includes(mod.LocalizationStatus);
  if (state.filter === "safe") return ["ok", "no_known_conflict"].includes(mod.CompatibilityStatus);
  if (state.filter === "heavy") return mod.PerformanceImpact === "heavy";
  if (state.filter === "needsChinese") return ["missing", "partial", "unknown", "outdated"].includes(mod.LocalizationStatus);
  return true;
}

function getVisibleMods() {
  return state.allMods.filter(passesFilter);
}

function renderMetrics() {
  const mods = state.allMods;
  els.metricMods.textContent = String(mods.length);
  els.metricTranslated.textContent = String(mods.filter((m) => ["complete", "mostly_complete"].includes(m.LocalizationStatus)).length);
  els.metricRisks.textContent = String(mods.filter((m) => !["ok", "no_known_conflict", "unknown"].includes(m.CompatibilityStatus)).length);
  els.metricHeavy.textContent = String(mods.filter((m) => m.PerformanceImpact === "heavy").length);
}

function renderResults() {
  const mods = getVisibleMods();
  state.visibleMods = mods;
  els.resultCount.textContent = `${mods.length} result${mods.length === 1 ? "" : "s"}`;
  els.resultList.innerHTML = "";

  if (!mods.length) {
    const empty = document.createElement("article");
    empty.className = "mini-row mini-row-action";
    const filterOnly = state.allMods.length > 0;
    empty.innerHTML = filterOnly
      ? `
        <strong>No result in this filter</strong>
        <span>The current search has indexed rows, but none match this filter.</span>
        <button class="secondary-button" type="button">Clear filter</button>
      `
      : `
        <strong>No indexed mod found</strong>
        <span>Report a missing PackageId or Workshop URL so moderators can add it.</span>
        <button class="secondary-button" type="button">Report missing mod</button>
      `;
    empty.querySelector("button").addEventListener("click", () => {
      if (filterOnly) {
        state.filter = "all";
        applyFilterButtonState();
        writeUrlState();
        renderAll();
        return;
      }
      openMissingModReport(state.query);
    });
    els.resultList.append(empty);
    return;
  }

  for (const mod of mods) {
    const button = document.createElement("button");
    button.type = "button";
    button.className = `data-card${mod.PackageId === state.selectedPackageId ? " is-active" : ""}`;
    button.innerHTML = `
      <div class="card-top">
        <div>
          <h3>${escapeHtml(mod.ModName)}</h3>
          <div class="package-line mono">${escapeHtml(mod.DisplayPackageId || mod.PackageId)}</div>
        </div>
        <span class="status-chip ${statusClass(mod.LocalizationStatus)}">${titleCase(mod.LocalizationStatus)}</span>
      </div>
      <div class="chip-row">
        <span class="status-chip ${statusClass(mod.CompatibilityStatus)}">${publicCompatibilityLabel(mod.CompatibilityStatus)}</span>
        <span class="status-chip ${statusClass(mod.PerformanceImpact)}">${titleCase(mod.PerformanceImpact)}</span>
        <span class="status-chip ${statusClass(mod.TrustLevel)}">${titleCase(mod.TrustLevel)}</span>
      </div>
    `;
    button.addEventListener("click", () => {
      state.selectedPackageId = mod.PackageId;
      state.detailPriority = true;
      syncDetailPriority();
      writeUrlState();
      renderResults();
      loadDetail(mod.PackageId);
      document.getElementById("detailPanel").scrollIntoView({ behavior: "smooth", block: "start" });
    });
    els.resultList.append(button);
  }
}

function renderMiniRows(target, rows, emptyText, formatter) {
  target.innerHTML = "";
  if (!rows || !rows.length) {
    const empty = document.createElement("article");
    empty.className = "mini-row";
    empty.innerHTML = `<strong>Unknown</strong><span>${escapeHtml(emptyText)}</span>`;
    target.append(empty);
    return;
  }

  rows.forEach((row) => {
    const formatted = formatter(row);
    const item = document.createElement("article");
    item.className = "mini-row";
    item.innerHTML = `
      <strong>${escapeHtml(formatted.title)}</strong>
      <span>${escapeHtml(formatted.detail)}</span>
      ${formatted.meta ? `<span>${escapeHtml(formatted.meta)}</span>` : ""}
    `;
    target.append(item);
  });
}

function createMiniRow({ title, detail, meta = "", className = "" }) {
  const row = document.createElement("article");
  row.className = `mini-row${className ? ` ${className}` : ""}`;
  row.innerHTML = `
    <strong>${escapeHtml(title)}</strong>
    <span>${escapeHtml(detail)}</span>
    ${meta ? `<span>${escapeHtml(meta)}</span>` : ""}
  `;
  return row;
}

function renderDetail(mod, loading = false) {
  if (!mod) {
    els.detailEmpty.classList.remove("is-hidden");
    els.detailCard.classList.add("is-hidden");
    return;
  }

  els.detailEmpty.classList.add("is-hidden");
  els.detailCard.classList.remove("is-hidden");
  els.detailTitle.textContent = loading ? `${mod.ModName} / loading` : mod.ModName;
  els.detailPackage.textContent = mod.DisplayPackageId || mod.PackageId;
  els.detailVersions.textContent = mod.SupportedGameVersions.length ? mod.SupportedGameVersions.join(", ") : mod.LatestKnownVersion || "Unknown";
  els.detailLocalization.textContent = titleCase(mod.LocalizationStatus);
  els.detailPerformance.textContent = titleCase(mod.PerformanceImpact);
  els.detailCompatibility.textContent = publicCompatibilityLabel(mod.CompatibilityStatus);
  els.detailConfidence.textContent = titleCase(mod.Confidence);
  els.detailPrimaryStatus.textContent = publicCompatibilityLabel(mod.CompatibilityStatus);
  setChipClass(els.detailPrimaryStatus, mod.CompatibilityStatus);
  els.detailTrust.textContent = `${titleCase(mod.TrustLevel)} / ${titleCase(mod.Confidence)}`;
  setChipClass(els.detailTrust, mod.TrustLevel);

  renderWarning(mod);
  renderMiniRows(els.localizationList, mod.Localization, "No localization record. Unknown does not mean missing forever.", formatLocalization);
  renderMiniRows(els.compatibilityList, mod.Compatibility, "No reviewed compatibility report. Unknown does not mean safe.", formatCompatibility);
  renderMiniRows(els.performanceList, mod.Performance, "No verified performance summary yet.", formatPerformance);
  renderSources(mod);
  renderShareState(mod);

  els.reportPackage.value = mod.PackageId;
}

function renderShareState(mod) {
  if (!els.copyLinkButton) return;
  els.copyLinkButton.disabled = !mod?.PackageId;
  els.copyLinkButton.title = mod?.PackageId ? `Copy link to ${mod.PackageId}` : "No mod selected";
}

function selectedMod() {
  return state.details.get(state.selectedPackageId)
    || state.allMods.find((item) => item.PackageId === state.selectedPackageId)
    || null;
}

function renderWarning(mod) {
  const status = mod.CompatibilityStatus;
  const cls = statusClass(status);
  els.detailWarning.className = `warning-block ${cls}`;
  const title = publicCompatibilityLabel(status);
  const copy = status === "unknown"
    ? "No reviewed conflict data. Unknown does not mean safe."
    : ["ok", "no_known_conflict"].includes(status)
      ? "No reviewed conflict is currently recorded."
      : `Review this before adding the mod. Trust: ${titleCase(mod.TrustLevel)}.`;
  els.detailWarning.innerHTML = `<strong>${escapeHtml(title)}</strong> <p>${escapeHtml(copy)}</p>`;
}

function renderSources(mod) {
  els.sourceList.innerHTML = "";
  const sources = mod.Sources || [];
  if (!sources.length) {
    const empty = document.createElement("article");
    empty.className = "mini-row";
    empty.innerHTML = "<strong>No source link</strong><span>No external source is indexed yet.</span>";
    els.sourceList.append(empty);
    els.openPrimarySource.classList.add("is-disabled");
    els.openPrimarySource.removeAttribute("href");
    return;
  }

  sources.forEach((source) => {
    const url = getField(source, "Url", "url");
    const label = getField(source, "Label", "label") || titleCase(getField(source, "SourceType", "sourceType", "source"));
    const link = document.createElement("a");
    link.className = "source-link";
    link.href = url;
    link.target = "_blank";
    link.rel = "noreferrer";
    link.innerHTML = `<strong>${escapeHtml(label)}</strong><span>${escapeHtml(url)}</span>`;
    els.sourceList.append(link);
  });

  els.openPrimarySource.classList.remove("is-disabled");
  els.openPrimarySource.href = getField(sources[0], "Url", "url");
}

function formatLocalization(row) {
  const language = getField(row, "Language", "language", "Unknown");
  const status = getField(row, "Status", "status", "unknown");
  const trust = getField(row, "TrustLevel", "trustLevel", "unknown");
  const confidence = getField(row, "Confidence", "confidence", "low");
  const notes = getField(row, "Notes", "notes") || getField(row, "SourceType", "sourceType", "No note");
  return {
    title: `${language} / ${titleCase(status)}`,
    detail: notes,
    meta: `${titleCase(trust)} / ${titleCase(confidence)}`,
  };
}

function formatCompatibility(row) {
  const reportType = getField(row, "ReportType", "reportType", "unknown");
  const status = getField(row, "PublicStatus", "publicStatus", getField(row, "CompatibilityStatus", "compatibilityStatus", "unknown"));
  const summary = getField(row, "Summary", "summary", "No summary");
  const trust = getField(row, "TrustLevel", "trustLevel", "unknown");
  const confidence = getField(row, "Confidence", "confidence", "low");
  return {
    title: `${publicCompatibilityLabel(status)} / ${titleCase(reportType)}`,
    detail: summary,
    meta: `${titleCase(trust)} / ${titleCase(confidence)}`,
  };
}

function formatPerformance(row) {
  const impact = getField(row, "Impact", "impact", getField(row, "PerformanceImpact", "performanceImpact", "unknown"));
  const summary = getField(row, "Summary", "summary", "No summary");
  const trust = getField(row, "TrustLevel", "trustLevel", "unknown");
  const confidence = getField(row, "Confidence", "confidence", "low");
  return {
    title: titleCase(impact),
    detail: summary,
    meta: `${titleCase(trust)} / ${titleCase(confidence)}`,
  };
}

function setChipClass(element, value) {
  element.className = `status-chip ${statusClass(value)}`;
}

function analyzeModlist() {
  const parsed = parseModlistInput(els.modlistInput.value);
  const index = state.indexMods.length ? state.indexMods : state.allMods;
  const matched = [];
  const missing = [];
  parsed.identifiers.forEach((identifier) => {
    const mod = findModByIdentifier(identifier, index);
    if (mod) {
      matched.push({ identifier, mod });
      return;
    }
    missing.push(identifier);
  });

  const found = matched.map((item) => item.mod);
  const uniqueFound = found.filter((mod, indexInList, list) => {
    return list.findIndex((item) => item.PackageId === mod.PackageId) === indexInList;
  });
  const missingCount = missing.length;
  els.analyzerStatus.textContent = missingCount ? "Review needed" : uniqueFound.length ? "Catalog matched" : "No match";
  els.analyzerStatus.className = `status-dot ${missingCount ? "unknown" : uniqueFound.length ? "safe" : "unknown"}`;
  els.analyzerSummary.textContent = `${uniqueFound.length} matched / ${parsed.identifiers.length} parsed IDs from ${parsed.inputLineCount} input line${parsed.inputLineCount === 1 ? "" : "s"} (${parsed.mode}). ${missingCount} not indexed.`;
  els.analyzerResults.innerHTML = "";

  if (!parsed.identifiers.length) {
    els.analyzerResults.append(createMiniRow({
      title: "No identifiers parsed",
      detail: "Paste ModsConfig.xml, RimSort/RimPy output, PackageIds, Workshop IDs, or Steam Workshop URLs.",
    }));
    return;
  }

  if (!uniqueFound.length) {
    els.analyzerResults.append(createMiniRow({
      title: "No indexed package matched",
      detail: "Parsed IDs were valid, but none are currently present in the preview catalog.",
      meta: missing.slice(0, 6).join(", "),
    }));
    return;
  }

  uniqueFound
    .sort((a, b) => analyzerRank(b) - analyzerRank(a))
    .forEach((mod) => {
      const severity = analyzerSeverity(mod);
      els.analyzerResults.append(createMiniRow({
        title: `${severity} / ${mod.ModName}`,
        detail: `${publicCompatibilityLabel(mod.CompatibilityStatus)} / ${titleCase(mod.PerformanceImpact)} / ${titleCase(mod.LocalizationStatus)}`,
        meta: mod.PackageId,
        className: `severity-${statusClass(analyzerStatusValue(mod))}`,
      }));
    });

  if (missing.length) {
    els.analyzerResults.append(createMiniRow({
      title: "Not indexed",
      detail: "These parsed IDs are ready for a missing-mod report or future catalog seed.",
      meta: missing.slice(0, 12).join(", "),
      className: "severity-unknown",
    }));
  }
}

function syncReportModeStatus() {
  if (!els.reportStatus) return;
  if (REPORT_PREVIEW_MODE) {
    els.reportStatus.textContent = "Preview intake";
    els.reportStatus.className = "status-dot info";
    return;
  }
  if (TURNSTILE_SITE_KEY) {
    els.reportStatus.textContent = state.dataMode === "live" ? "Live intake" : "API required";
    els.reportStatus.className = `status-dot ${state.dataMode === "live" ? "safe" : "unknown"}`;
    return;
  }
  els.reportStatus.textContent = "Turnstile required";
  els.reportStatus.className = "status-dot unknown";
}

function setReportStatus(message, mode = "unknown") {
  els.reportFormStatus.textContent = message;
  els.reportFormStatus.className = `report-note ${mode}`;
}

function loadTurnstileScript() {
  if (window.turnstile) return Promise.resolve();
  if (state.turnstileLoading) {
    return new Promise((resolve, reject) => {
      const startedAt = Date.now();
      const timer = window.setInterval(() => {
        if (window.turnstile) {
          window.clearInterval(timer);
          resolve();
        } else if (Date.now() - startedAt > 8000) {
          window.clearInterval(timer);
          reject(new Error("Turnstile script did not load"));
        }
      }, 120);
    });
  }

  state.turnstileLoading = true;
  return new Promise((resolve, reject) => {
    const script = document.createElement("script");
    script.src = "https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit";
    script.async = true;
    script.defer = true;
    script.onload = () => resolve();
    script.onerror = () => reject(new Error("Turnstile script did not load"));
    document.head.append(script);
  });
}

async function ensureTurnstileReady() {
  if (REPORT_PREVIEW_MODE) {
    state.turnstileToken = "preview-turnstile-token";
    els.turnstileSlot.textContent = "Preview verification active.";
    return true;
  }

  if (!TURNSTILE_SITE_KEY) {
    els.turnstileSlot.textContent = "Turnstile site key is not configured.";
    return false;
  }

  try {
    await loadTurnstileScript();
    if (state.turnstileWidgetId === null && window.turnstile) {
      state.turnstileWidgetId = window.turnstile.render(els.turnstileSlot, {
        sitekey: TURNSTILE_SITE_KEY,
        theme: "dark",
        callback: (token) => {
          state.turnstileToken = token;
          setReportStatus("Verification ready. Report can be queued.", "safe");
        },
        "expired-callback": () => {
          state.turnstileToken = "";
          setReportStatus("Verification expired. Run the check again.", "warn");
        },
        "error-callback": () => {
          state.turnstileToken = "";
          setReportStatus("Verification failed. Retry before submitting.", "danger");
        },
      });
    }
    return true;
  } catch (err) {
    els.turnstileSlot.textContent = err.message || "Turnstile unavailable.";
    return false;
  }
}

function resetTurnstile() {
  state.turnstileToken = "";
  if (state.turnstileWidgetId !== null && window.turnstile) {
    window.turnstile.reset(state.turnstileWidgetId);
  }
}

function openReportDialog() {
  if (typeof els.reportDialog.showModal !== "function") return;
  setReportStatus("Reports stay in review before public conclusions change.", "unknown");
  ensureTurnstileReady();
  els.reportDialog.showModal();
}

function openMissingModReport(seedValue = "") {
  const cleanSeed = String(seedValue || "").trim();
  const workshopId = extractWorkshopId(cleanSeed);
  els.reportType.value = "missing_mod";
  els.reportPackage.value = cleanSeed;
  els.reportLanguage.value = "";
  els.reportGameVersion.value = "";
  els.reportImpact.value = "unknown";
  els.reportSummary.value = cleanSeed
    ? workshopId
      ? `Please index this RimWorld mod. Workshop ID: ${workshopId}`
      : `Please index this RimWorld mod: ${cleanSeed}`
    : "Please index this RimWorld mod. I can provide the PackageId or Workshop URL.";
  openReportDialog();
}

async function submitReport(event) {
  event.preventDefault();
  const reportKind = els.reportType.value;
  const rawPackageInput = els.reportPackage.value.trim();
  const workshopId = extractWorkshopId(rawPackageInput);
  const packageId = packageIdForReport(rawPackageInput, reportKind);
  const summary = els.reportSummary.value.trim();
  const mod = selectedMod();

  if (!packageId && reportKind !== "missing_mod") {
    setReportStatus("PackageId is required.", "danger");
    els.reportPackage.focus();
    return;
  }
  if (summary.length < 12) {
    setReportStatus("Report detail is too short.", "danger");
    els.reportSummary.focus();
    return;
  }
  if (state.dataMode !== "live" && !REPORT_PREVIEW_MODE) {
    setReportStatus("RWMod API is unavailable. Report is not queued.", "danger");
    return;
  }

  const ready = await ensureTurnstileReady();
  if (!ready || !state.turnstileToken) {
    setReportStatus("Verification is required before submission.", "warn");
    return;
  }

  setReportStatus("Queueing report for review.", "unknown");
  const detail = reportKind === "missing_mod" && rawPackageInput
    ? `${summary}\n\nSubmitted identifier or URL: ${rawPackageInput}${workshopId ? `\nParsed Workshop ID: ${workshopId}` : ""}`
    : summary;
  const payload = {
    reportKind,
    packageId,
    modName: mod?.ModName || "",
    summary: summary.slice(0, 220),
    detail,
    workshopId,
    language: els.reportLanguage.value,
    gameVersion: els.reportGameVersion.value,
    impact: els.reportImpact.value,
    turnstileToken: state.turnstileToken,
  };

  try {
    const result = await postJson("/api/v1/rwmod/reports", payload);
    const feedbackId = result?.feedbackId || "queued";
    setReportStatus(`Queued for review: ${feedbackId}`, "safe");
    els.reportSummary.value = "";
    resetTurnstile();
    window.setTimeout(() => {
      if (els.reportDialog.open) els.reportDialog.close();
      setReportStatus("Reports stay in review before public conclusions change.", "unknown");
    }, 1100);
  } catch (err) {
    setReportStatus(err.message || "Report submission failed.", "danger");
    resetTurnstile();
  }
}

function analyzerRank(mod) {
  let score = 0;
  if (mod.CompatibilityStatus === "conflict") score += 100;
  if (mod.PerformanceImpact === "heavy") score += 60;
  if (mod.PerformanceImpact === "medium") score += 30;
  if (["missing", "unknown", "partial"].includes(mod.LocalizationStatus)) score += 10;
  return score;
}

function analyzerStatusValue(mod) {
  if (mod.CompatibilityStatus === "conflict") return "conflict";
  if (mod.PerformanceImpact === "heavy") return "heavy";
  if (["missing", "unknown", "partial"].includes(mod.LocalizationStatus)) return "warning";
  return mod.CompatibilityStatus;
}

function analyzerSeverity(mod) {
  if (mod.CompatibilityStatus === "conflict") return "Conflict";
  if (mod.PerformanceImpact === "heavy") return "Heavy impact";
  if (mod.PerformanceImpact === "medium") return "Medium impact";
  if (["missing", "unknown", "partial"].includes(mod.LocalizationStatus)) return "Localization gap";
  if (mod.CompatibilityStatus === "unknown") return "Unknown";
  return "Clear";
}

function renderAll() {
  renderMetrics();
  renderResults();
  const mod = state.details.get(state.selectedPackageId) || state.allMods.find((item) => item.PackageId === state.selectedPackageId);
  renderDetail(mod);
}

function scheduleSearchRefresh() {
  window.clearTimeout(state.searchTimer);
  state.searchTimer = window.setTimeout(refreshCatalog, SEARCH_DEBOUNCE_MS);
}

function escapeHtml(value) {
  return String(value || "")
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#39;");
}

els.searchInput.addEventListener("input", (event) => {
  state.query = event.target.value;
  writeUrlState({ replace: true });
  scheduleSearchRefresh();
});

els.clearButton.addEventListener("click", () => {
  state.query = "";
  els.searchInput.value = "";
  els.searchInput.focus();
  writeUrlState({ replace: true });
  refreshCatalog();
});

els.pasteButton.addEventListener("click", async () => {
  try {
    const text = await navigator.clipboard.readText();
    els.searchInput.value = text.trim();
    state.query = els.searchInput.value;
    writeUrlState({ replace: true });
    refreshCatalog();
  } catch {
    els.searchInput.focus();
  }
});

els.headerSearchButton.addEventListener("click", () => els.searchInput.focus());

document.querySelectorAll(".mode-chip").forEach((button) => {
  button.addEventListener("click", () => {
    document.querySelectorAll(".mode-chip").forEach((item) => item.classList.remove("is-active"));
    button.classList.add("is-active");
    if (button.dataset.mode === "modlist") document.getElementById("analyzer").scrollIntoView({ behavior: "smooth" });
    if (button.dataset.mode === "reports") document.getElementById("reports").scrollIntoView({ behavior: "smooth" });
  });
});

document.querySelectorAll(".filter-chip").forEach((button) => {
  button.addEventListener("click", () => {
    state.filter = button.dataset.filter;
    applyFilterButtonState();
    writeUrlState();
    renderAll();
  });
});

document.querySelectorAll("[data-jump]").forEach((button) => {
  button.addEventListener("click", () => document.querySelector(button.dataset.jump).scrollIntoView({ behavior: "smooth" }));
});

els.copyPackageButton.addEventListener("click", async () => {
  const text = els.detailPackage.textContent;
  try {
    await navigator.clipboard.writeText(text);
    els.copyPackageButton.textContent = "Copied";
    window.setTimeout(() => {
      els.copyPackageButton.textContent = "Copy PackageId";
    }, 1200);
  } catch {
    els.copyPackageButton.textContent = text;
  }
});

els.copyLinkButton.addEventListener("click", async () => {
  const link = modDetailUrl();
  try {
    await navigator.clipboard.writeText(link);
    els.copyLinkButton.textContent = "Copied link";
  } catch {
    els.copyLinkButton.textContent = "Link ready";
  }
  window.setTimeout(() => {
    els.copyLinkButton.textContent = "Copy link";
  }, 1200);
});

window.addEventListener("popstate", () => {
  applyUrlState({ replace: true });
  refreshCatalog();
});

els.openReportButton.addEventListener("click", () => {
  openReportDialog();
});

els.analyzeButton.addEventListener("click", analyzeModlist);

document.getElementById("reportForm").addEventListener("submit", submitReport);

syncReportModeStatus();
applyUrlState({ replace: true });
refreshCatalog();
