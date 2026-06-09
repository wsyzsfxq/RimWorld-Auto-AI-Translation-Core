import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { once } from "node:events";
import { createServer } from "node:net";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = dirname(fileURLToPath(import.meta.url));
const workerDir = resolve(__dirname, "..");
const repoRoot = resolve(workerDir, "..", "..");
const frontierDir = join(repoRoot, "web", "rwmod-frontier");
const previewServerPath = join(frontierDir, "preview-server.mjs");

async function findFreePort() {
  const server = createServer();
  server.listen(0, "127.0.0.1");
  await once(server, "listening");
  const address = server.address();
  const port = typeof address === "object" && address ? address.port : 0;
  server.close();
  await once(server, "close");
  return port;
}

function startPreviewServer(port) {
  const child = spawn(process.execPath, [previewServerPath, "--worker"], {
    cwd: frontierDir,
    env: {
      ...process.env,
      PORT: String(port),
      HOST: "127.0.0.1",
    },
    stdio: ["ignore", "pipe", "pipe"],
  });

  let output = "";
  child.stdout.on("data", (chunk) => {
    output += chunk.toString();
  });
  child.stderr.on("data", (chunk) => {
    output += chunk.toString();
  });

  return { child, getOutput: () => output };
}

async function waitForServer(port, child, getOutput) {
  const deadline = Date.now() + 8000;
  while (Date.now() < deadline) {
    if (child.exitCode !== null) {
      throw new Error(`Preview server exited early with ${child.exitCode}:\n${getOutput()}`);
    }
    try {
      const response = await fetch(`http://127.0.0.1:${port}/api/v1/health`);
      if (response.ok) return;
    } catch {
      // Server is still starting.
    }
    await new Promise((resolve) => setTimeout(resolve, 120));
  }
  throw new Error(`Preview server did not become ready:\n${getOutput()}`);
}

async function requestJson(port, path, options = {}) {
  const response = await fetch(`http://127.0.0.1:${port}${path}`, {
    headers: { Accept: "application/json", ...(options.headers || {}) },
    ...options,
  });
  const bodyText = await response.text();
  let body = null;
  try {
    body = bodyText ? JSON.parse(bodyText) : null;
  } catch {
    body = bodyText;
  }
  assert.ok(response.ok, `${path} returned HTTP ${response.status}: ${bodyText}`);
  return body;
}

async function main() {
  const port = await findFreePort();
  const { child, getOutput } = startPreviewServer(port);

  try {
    await waitForServer(port, child, getOutput);

    const workshop = await requestJson(port, "/api/v1/rwmod/mods?q=2009463077&limit=5");
    assert.equal(workshop.Items[0]?.PackageId, "brrainz.harmony");
    assert.equal(workshop.Items[0]?.DataSource, "rwmod_catalog");

    const detail = await requestJson(port, "/api/v1/rwmod/mods/brrainz.harmony");
    assert.equal(detail.AboveFold.PackageId, "brrainz.harmony");
    assert.equal(detail.AboveFold.LocalizationStatus, "partial");
    assert.equal(detail.AboveFold.CompatibilityStatus, "warning");
    assert.equal(detail.AboveFold.PerformanceImpact, "light");
    assert.equal(detail.Sources[0]?.Url, "https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077");

    const registryFallback = await requestJson(port, "/api/v1/rwmod/mods?q=rwmod.registry.seedonly&limit=5");
    assert.equal(registryFallback.Items[0]?.PackageId, "rwmod.registry.seedonly");
    assert.equal(registryFallback.Items[0]?.DataSource, "translation_registry");
    assert.equal(registryFallback.Items[0]?.CompatibilityStatus, "unknown");
    assert.equal(registryFallback.Items[0]?.PerformanceImpact, "unknown");

    const rimthreaded = await requestJson(port, "/api/v1/rwmod/mods/rimthreaded.core/performance");
    assert.equal(rimthreaded.Impact, "heavy");
    assert.equal(rimthreaded.Items[0]?.Impact, "heavy");

    const optimizer = await requestJson(port, "/api/v1/rwmod/mods/owlchemist.performanceoptimizer/compatibility");
    assert.equal(optimizer.Status, "unknown");
    assert.equal(optimizer.Count, 0);

    const missing = await requestJson(port, "/api/v1/rwmod/mods?q=definitely.notindexed.999&limit=5");
    assert.equal(missing.Count, 0);

    const report = await requestJson(port, "/api/v1/rwmod/reports", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        reportKind: "compatibility",
        packageId: "brrainz.harmony",
        modName: "Harmony",
        summary: "Phase 3 automated local preview report smoke",
        detail: "Phase 3 automated local preview report smoke through worker-v2 and in-memory D1.",
        turnstileToken: "preview-turnstile-token",
      }),
    });
    assert.equal(report.success, true);
    assert.equal(report.status, "open");
    assert.equal(report.trustLevel, "player_report");
    assert.equal(report.confidence, "low");

    console.log(JSON.stringify({
      ok: true,
      port,
      api: {
        workshopFirst: workshop.Items[0]?.PackageId,
        detailAboveFold: detail.AboveFold,
        registryFallback: registryFallback.Items[0],
        rimthreadedImpact: rimthreaded.Impact,
        optimizerCompatibility: optimizer.Status,
        missingCount: missing.Count,
        reportStatus: report.status,
      },
    }, null, 2));
  } finally {
    child.kill();
  }
}

await main();
