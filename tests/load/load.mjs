// Many teams at once against a running stack: every team uploads the sample documents at the same moment, then
// its members search and ask in parallel. Reports indexing throughput and latencies, and checks that no search
// or answer ever returns another team's passage.
// Usage: node tests/load/load.mjs [teams] [requests per team] [concurrency]
// Start the stack with the per-person limits out of the way:
//   AUTH_ATTEMPTS_PER_MINUTE=100000 QUESTIONS_PER_MINUTE=100000 docker compose -f docker-compose.yml -f docker-compose.light.yml up --build -d
import { readFileSync } from "node:fs";

const BASE = process.env.BASE_URL ?? "http://localhost:8080";
const [teams = 20, perTeam = 40, concurrency = 32] = process.argv.slice(2).map(Number);
const samples = new URL("../../samples/", import.meta.url).pathname;
const files = ["returns-and-refunds.md", "shipping.md", "brewing-guide.md", "support-handbook.md", "iade-politikasi.md", "equipment-care.pdf"];
const questions = [
  "How long do refunds take?", "Which carrier delivers the parcels?", "Is shipping free?", "How do I descale the kettle?",
  "What is the V60 recipe?", "Who handles double charges?", "KHV-004", "Para iadesi kaç günde yapılır?",
];
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function call(path, token, body, method = body ? "POST" : "GET") {
  const isForm = body instanceof FormData;
  const started = performance.now();
  const response = await fetch(BASE + path, {
    method,
    headers: { ...(isForm || !body ? {} : { "Content-Type": "application/json" }), ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    body: isForm ? body : body ? JSON.stringify(body) : undefined,
  });
  if (!response.ok) throw new Error(`${method} ${path}: ${response.status} ${await response.text()}`);
  return { data: response.status === 204 ? null : await response.json(), ms: performance.now() - started };
}

async function pool(tasks, limit) {
  const results = new Array(tasks.length);
  let next = 0;
  await Promise.all(Array.from({ length: Math.min(limit, tasks.length) }, async () => {
    while (next < tasks.length) {
      const i = next++;
      results[i] = await tasks[i]();
    }
  }));
  return results;
}

const pct = (values, p) => [...values].sort((a, b) => a - b)[Math.min(values.length - 1, Math.floor(p * values.length))];
const summary = (values) => `p50 ${pct(values, 0.5).toFixed(0)} ms, p95 ${pct(values, 0.95).toFixed(0)} ms`;
const content = Object.fromEntries(files.map((f) => [f, readFileSync(samples + f)]));
const run = Date.now().toString(36);

const team = await pool(Array.from({ length: teams }, (_, i) => async () => {
  const { data } = await call("/api/auth/register", null, { email: `load-${run}-${i}@example.com`, displayName: `Team ${i}`, password: "load-test-password" });
  const workspace = (await call("/api/me", data.accessToken)).data.workspaces[0].id;
  return { token: data.accessToken, workspace, documents: new Set() };
}), 16);

// Everyone uploads at once; each team's files carry its number, so every team has its own copies.
const uploadStarted = performance.now();
const uploads = await pool(team.flatMap((t, i) => files.map((f) => async () => {
  const form = new FormData();
  const bytes = f.endsWith(".pdf") ? content[f] : Buffer.concat([content[f], Buffer.from(`\n\nTeam ${i} keeps its own copy of this page.\n`)]);
  form.append("file", new Blob([bytes]), f);
  const { data, ms } = await call(`/api/workspaces/${t.workspace}/documents`, t.token, form);
  t.documents.add(data.id);
  return { ms, chunks: data.chunkCount };
})), concurrency);
// PDFs are identical across teams on purpose: the same file in two workspaces is allowed.

let indexedAt = 0;
for (;;) {
  const usage = await Promise.all(team.map((t) => call(`/api/workspaces/${t.workspace}/usage`, t.token).then((r) => r.data)));
  const ready = usage.reduce((n, u) => n + u.ready, 0);
  const failed = usage.reduce((n, u) => n + u.failed, 0);
  if (failed) throw new Error(`${failed} documents failed to index`);
  if (ready === teams * files.length) { indexedAt = performance.now(); break; }
  if (performance.now() - uploadStarted > 600_000) throw new Error("indexing took over 10 minutes");
  await sleep(250);
}
const chunks = uploads.reduce((n, u) => n + u.chunks, 0);
const indexSeconds = (indexedAt - uploadStarted) / 1000;
console.log(`${teams} teams uploaded ${uploads.length} documents (${chunks} passages) at once; upload ${summary(uploads.map((u) => u.ms))}`);
console.log(`all indexed ${indexSeconds.toFixed(1)} s after the first upload: ${(chunks / indexSeconds).toFixed(0)} passages per second`);

let leaks = 0;
const searches = await pool(team.flatMap((t) => Array.from({ length: perTeam }, (_, j) => async () => {
  const { data, ms } = await call(`/api/workspaces/${t.workspace}/search?q=${encodeURIComponent(questions[j % questions.length])}`, t.token);
  leaks += data.hits.filter((h) => !t.documents.has(h.documentId)).length;
  return ms;
})), concurrency);
console.log(`${searches.length} hybrid searches, ${concurrency} at a time: ${summary(searches)}`);

const conversations = await Promise.all(team.map((t) => call(`/api/workspaces/${t.workspace}/conversations`, t.token, {}).then((r) => r.data.id)));
const answers = await pool(team.flatMap((t, i) => Array.from({ length: Math.max(1, perTeam / 4) }, (_, j) => async () => {
  const { data, ms } = await call(`/api/workspaces/${t.workspace}/conversations/${conversations[i]}/messages`, t.token, { question: questions[j % questions.length] });
  leaks += data.answer.citations.filter((c) => !t.documents.has(c.documentId)).length;
  return ms;
})), concurrency);
console.log(`${answers.length} questions answered (built-in extractive model), ${concurrency} at a time: ${summary(answers)}`);

if (leaks) {
  console.log(`PROBLEM: ${leaks} hits or citations came from another team's workspace`);
  process.exit(1);
}
console.log("isolation held: every hit and every citation came from the asking team's own documents");
