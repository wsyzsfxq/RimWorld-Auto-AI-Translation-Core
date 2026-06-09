import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { once } from "node:events";
import { createServer } from "node:net";
import { resolve } from "node:path";

const workerDir = resolve(import.meta.dirname, "..");
const wranglerBin = "node_modules/wrangler/bin/wrangler.js";

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

function spawnWrangler(args) {
  return spawn(process.execPath, [wranglerBin, ...args], {
    cwd: workerDir,
    stdio: ["ignore", "pipe", "pipe"],
  });
}

async function runWrangler(args) {
  const child = spawnWrangler(args);
  let output = "";
  child.stdout.on("data", (chunk) => {
    output += chunk.toString();
  });
  child.stderr.on("data", (chunk) => {
    output += chunk.toString();
  });
  const [code] = await once(child, "close");
  assert.equal(code, 0, `wrangler ${args.join(" ")} failed:\n${output}`);
  return output;
}

async function waitForReady(child, getOutput, port) {
  const deadline = Date.now() + 30000;
  while (Date.now() < deadline) {
    if (child.exitCode !== null) {
      throw new Error(`wrangler dev exited early with ${child.exitCode}:\n${getOutput()}`);
    }
    if (getOutput().includes(`Ready on http://127.0.0.1:${port}`) || getOutput().includes("Ready on")) return;
    await new Promise((resolveTimeout) => setTimeout(resolveTimeout, 250));
  }
  throw new Error(`wrangler dev did not become ready:\n${getOutput()}`);
}

async function requestText(url) {
  const response = await fetch(url);
  const body = await response.text();
  return {
    url,
    status: response.status,
    contentType: response.headers.get("content-type") || "",
    length: body.length,
    body,
  };
}

function summarize(check) {
  return {
    url: check.url,
    status: check.status,
    contentType: check.contentType,
    length: check.length,
    snippet: check.body.slice(0, 120).replace(/\s+/g, " "),
  };
}

async function main() {
  await runWrangler(["d1", "execute", "atc-database", "--local", "--file", "schema.sql"]);

  const port = await findFreePort();
  const child = spawnWrangler(["dev", "--local", "--ip", "127.0.0.1", "--port", String(port), "--log-level", "info"]);
  let output = "";
  child.stdout.on("data", (chunk) => {
    output += chunk.toString();
  });
  child.stderr.on("data", (chunk) => {
    output += chunk.toString();
  });

  try {
    await waitForReady(child, () => output, port);

    const checks = [];
    checks.push(await requestText(`http://127.0.0.1:${port}/`));
    checks.push(await requestText(`http://127.0.0.1:${port}/assets/rwmod.css`));
    checks.push(await requestText(`http://127.0.0.1:${port}/assets/rwmod.js`));
    checks.push(await requestText(`http://127.0.0.1:${port}/catalog/deep-link`));
    checks.push(await requestText(`http://127.0.0.1:${port}/?mod=owlchemist.cleanpathfinding`));
    checks.push(await requestText(`http://127.0.0.1:${port}/api/v1/health`));
    checks.push(await requestText(`http://127.0.0.1:${port}/api/v1/rwmod/mods?q=owlchemist.cleanpathfinding&limit=5`));

    const [home, css, js, spaPath, queryPath, health, mods] = checks;
    assert.equal(home.status, 200);
    assert.match(home.contentType, /text\/html/);
    assert.match(home.body, /<script src="\.\/assets\/rwmod\.js"><\/script>/);

    assert.equal(css.status, 200);
    assert.match(css.contentType, /text\/css/);
    assert.match(css.body, /:root/);

    assert.equal(js.status, 200);
    assert.match(js.contentType, /javascript/);
    assert.match(js.body, /const API_BASE/);

    assert.equal(spaPath.status, 200);
    assert.match(spaPath.contentType, /text\/html/);
    assert.match(spaPath.body, /RWMod/);

    assert.equal(queryPath.status, 200);
    assert.match(queryPath.contentType, /text\/html/);
    assert.match(queryPath.body, /RimWorld mod intelligence console/);

    assert.equal(health.status, 200);
    assert.match(health.contentType, /application\/json/);
    assert.equal(JSON.parse(health.body).ok, true);

    assert.equal(mods.status, 200);
    assert.match(mods.contentType, /application\/json/);
    const modsPayload = JSON.parse(mods.body);
    assert.equal(modsPayload.Query, "owlchemist.cleanpathfinding");
    assert.equal(modsPayload.Defaults.CompatibilityStatus, "unknown");
    assert.equal(modsPayload.Defaults.PerformanceImpact, "unknown");

    console.log(JSON.stringify({
      ok: true,
      port,
      checks: checks.map(summarize),
      wranglerLogTail: output.split(/\r?\n/).filter(Boolean).slice(-30),
    }, null, 2));
  } finally {
    child.kill();
  }
}

await main();
