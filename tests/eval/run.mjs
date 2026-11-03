// Measures retrieval and answers on the sample documents against a running stack.
// For every question with a known source passage: is that passage the first hit (hit@1), among the first three
// (hit@3), and at what rank (MRR), for each search mode. Then the full answer path: is the expected passage cited,
// and are questions the documents do not cover left unanswered?
// Usage: node tests/eval/run.mjs [api-address]
import { readFileSync } from "node:fs";

const BASE = process.argv[2] ?? process.env.BASE_URL ?? "http://localhost:8080";
const here = new URL(".", import.meta.url).pathname;
const samples = new URL("../../samples/", import.meta.url).pathname;
const { answerable, outside } = JSON.parse(readFileSync(here + "questions.json", "utf8"));
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function call(path, token, body, method = body ? "POST" : "GET") {
  const isForm = body instanceof FormData;
  const response = await fetch(BASE + path, {
    method,
    headers: { ...(isForm || !body ? {} : { "Content-Type": "application/json" }), ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    body: isForm ? body : body ? JSON.stringify(body) : undefined,
  });
  if (!response.ok) throw new Error(`${method} ${path}: ${response.status} ${await response.text()}`);
  return response.status === 204 ? null : response.json();
}

const { accessToken: token } = await call("/api/auth/register", null, {
  email: `eval-${Date.now()}@example.com`, displayName: "Evaluation", password: "evaluation-password",
});
const workspace = (await call("/api/me", token)).workspaces[0].id;
const ws = `/api/workspaces/${workspace}`;
for (const file of ["returns-and-refunds.md", "shipping.md", "brewing-guide.md", "support-handbook.md", "iade-politikasi.md", "equipment-care.pdf"]) {
  const form = new FormData();
  form.append("file", new Blob([readFileSync(samples + file)]), file);
  await call(`${ws}/documents`, token, form);
}
const started = Date.now();
for (;;) {
  const usage = await call(`${ws}/usage`, token);
  if (usage.ready === usage.documents) {
    console.log(`indexed ${usage.documents} documents, ${usage.chunks} passages, in ${((Date.now() - started) / 1000).toFixed(1)} s`);
    break;
  }
  if (usage.failed > 0) throw new Error("a document failed to index");
  if (Date.now() - started > 600_000) throw new Error("indexing took over 10 minutes");
  await sleep(500);
}

const matches = (hit, expected) =>
  hit.documentTitle === expected.doc && ("page" in expected ? hit.page === expected.page : hit.heading === expected.heading);

let model = "";
const rows = [];
for (const mode of ["Hybrid", "Vector", "Keyword"]) {
  let top1 = 0, top3 = 0, rr = 0;
  const missed = [];
  for (const item of answerable) {
    const result = await call(`${ws}/search?q=${encodeURIComponent(item.q)}&mode=${mode}&limit=10`, token);
    model = result.embeddingModel;
    const rank = result.hits.findIndex((h) => matches(h, item)) + 1;
    if (rank === 1) top1++;
    if (rank >= 1 && rank <= 3) top3++;
    if (rank >= 1) rr += 1 / rank;
    if (rank !== 1) missed.push(`${item.q} → ${rank || "not found"}`);
  }
  const n = answerable.length;
  rows.push({ mode, top1: top1 / n, top3: top3 / n, mrr: rr / n });
  if (process.env.VERBOSE) console.log(`${mode} misses:\n  ${missed.join("\n  ")}`);
}

// How close the right passage is to its question, and how close anything is to a question the documents do not
// cover: the gap between the two is where the relevance threshold belongs.
const stats = (values) => {
  const sorted = [...values].sort((a, b) => a - b);
  return `min ${sorted[0].toFixed(2)}, median ${sorted[Math.floor(sorted.length / 2)].toFixed(2)}, max ${sorted[sorted.length - 1].toFixed(2)}`;
};
const expectedSimilarity = [], expectedShare = [], outsideSimilarity = [], outsideShare = [], expectedLead = [], outsideLead = [];
const mean = (values) => values.reduce((a, b) => a + b, 0) / Math.max(1, values.length);
for (const item of answerable) {
  const vector = await call(`${ws}/search?q=${encodeURIComponent(item.q)}&mode=Vector&limit=50`, token);
  const words = await call(`${ws}/search?q=${encodeURIComponent(item.q)}&mode=Keyword&limit=50`, token);
  const similarity = vector.hits.find((h) => matches(h, item))?.similarity ?? 0;
  expectedSimilarity.push(similarity);
  expectedLead.push(similarity - mean(vector.hits.map((h) => h.similarity)));
  expectedShare.push(words.hits.find((h) => matches(h, item))?.wordShare ?? 0);
  if (process.env.VERBOSE) console.log(`  ${similarity.toFixed(2)} lead ${(similarity - mean(vector.hits.map((h) => h.similarity))).toFixed(2)}  ${item.q}`);
}
for (const q of outside) {
  const vector = await call(`${ws}/search?q=${encodeURIComponent(q)}&mode=Vector&limit=50`, token);
  const words = await call(`${ws}/search?q=${encodeURIComponent(q)}&mode=Keyword&limit=50`, token);
  const top = vector.hits[0]?.similarity ?? 0;
  outsideSimilarity.push(top);
  outsideLead.push(top - mean(vector.hits.map((h) => h.similarity)));
  outsideShare.push(Math.max(0, ...words.hits.map((h) => h.wordShare)));
  if (process.env.VERBOSE) console.log(`  outside ${top.toFixed(2)} lead ${(top - mean(vector.hits.map((h) => h.similarity))).toFixed(2)}  ${q}`);
}
console.log(`\nsimilarity of the right passage: ${stats(expectedSimilarity)}`);
console.log(`best similarity for questions outside the documents: ${stats(outsideSimilarity)}`);
console.log(`lead of the right passage over the average candidate: ${stats(expectedLead)}`);
console.log(`lead of the best passage for questions outside the documents: ${stats(outsideLead)}`);
console.log(`word share of the right passage: ${stats(expectedShare)}`);
console.log(`best word share for questions outside the documents: ${stats(outsideShare)}`);

console.log(`\nsearch, ${answerable.length} questions, embedding model ${model}`);
console.log("| Mode | Hit@1 | Hit@3 | MRR |");
console.log("|---|---|---|---|");
for (const r of rows) console.log(`| ${r.mode} | ${(r.top1 * 100).toFixed(0)}% | ${(r.top3 * 100).toFixed(0)}% | ${r.mrr.toFixed(2)} |`);

let cited = 0, answered = 0, refused = 0, chatModel = "";
const times = [];
const wrong = [];
for (const item of answerable) {
  const conversation = await call(`${ws}/conversations`, token, {});
  const result = await call(`${ws}/conversations/${conversation.id}/messages`, token, { question: item.q });
  chatModel = result.answer.model ?? chatModel;
  times.push(result.answer.durationMs);
  if (result.answer.outcome === "Answered") answered++;
  if (result.answer.citations.some((c) => matches(c, item))) cited++;
  else wrong.push(`${item.q} → ${result.answer.outcome}: ${result.answer.citations.map((c) => c.heading ?? `page ${c.page}`).join(", ") || "no citation"}`);
}
for (const q of outside) {
  const conversation = await call(`${ws}/conversations`, token, {});
  const result = await call(`${ws}/conversations/${conversation.id}/messages`, token, { question: q });
  if (result.answer.outcome === "NotFound") refused++;
  else wrong.push(`outside: ${q} → ${result.answer.outcome}`);
}
times.sort((a, b) => a - b);
const pct = (p) => times[Math.min(times.length - 1, Math.floor(p * times.length))];

console.log(`\nanswers, chat model ${chatModel}`);
console.log(`answered ${answered}/${answerable.length}, cited the expected passage ${cited}/${answerable.length} (${(cited / answerable.length * 100).toFixed(0)}%)`);
console.log(`questions outside the documents left unanswered: ${refused}/${outside.length}`);
console.log(`answer time p50 ${(pct(0.5) / 1000).toFixed(1)} s, p95 ${(pct(0.95) / 1000).toFixed(1)} s`);
if (process.env.VERBOSE) console.log(`\nmisses:\n  ${wrong.join("\n  ")}`);
