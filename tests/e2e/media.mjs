// Screenshots and walkthrough frames for the README, from the running stack with the sample documents.
// Usage (real models): docker compose up --build -d && node media.mjs <output-dir>
import { chromium } from "playwright";
import { mkdirSync, readFileSync } from "node:fs";

const BASE = process.env.BASE_URL ?? "http://localhost:8080";
const OUT = (process.argv[2] ?? new URL("./output/media/", import.meta.url).pathname).replace(/\/?$/, "/");
const SAMPLES = new URL("../../samples/", import.meta.url).pathname;
mkdirSync(`${OUT}frames`, { recursive: true });
const run = Date.now().toString(36);
const password = "media-password-1";
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

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

const people = {};
for (const [key, name] of Object.entries({ elif: "Elif Şahin", mehmet: "Mehmet Demir", zeynep: "Zeynep Kaya" })) {
  const email = `${key}.${run}@example.com`;
  const session = await call("/api/auth/register", null, { email, displayName: name, password, workspaceName: key === "elif" ? "Demlik support" : undefined });
  people[key] = { name, email, token: session.accessToken };
}
const elifToken = people.elif.token;
const workspace = (await call("/api/me", elifToken)).workspaces[0];
const ws = `/api/workspaces/${workspace.id}`;
await call(`${ws}/members`, elifToken, { email: people.mehmet.email, role: "Editor" });
await call(`${ws}/members`, elifToken, { email: people.zeynep.email, role: "Reader" });

for (const file of ["returns-and-refunds.md", "shipping.md", "brewing-guide.md", "support-handbook.md", "iade-politikasi.md", "equipment-care.pdf"]) {
  const form = new FormData();
  form.append("file", new Blob([readFileSync(SAMPLES + file)]), file);
  await call(`${ws}/documents`, people[file === "support-handbook.md" ? "mehmet" : "elif"].token, form);
  await sleep(300);
}
for (let i = 0; i < 300; i++) {
  const usage = await call(`${ws}/usage`, elifToken);
  if (usage.ready === usage.documents) break;
  await sleep(1000);
}

// Earlier conversations, so the list is not empty.
for (const q of ["Can a customer exchange a kettle for another model?", "What does E2 on the kettle display mean?"]) {
  const conversation = await call(`${ws}/conversations`, elifToken, {});
  await call(`${ws}/conversations/${conversation.id}/messages`, elifToken, { question: q });
}

const browser = await chromium.launch();
async function signedIn(person, viewport = { width: 1280, height: 800 }) {
  const context = await browser.newContext({ viewport, locale: "en-GB", deviceScaleFactor: 1 });
  const page = await context.newPage();
  await page.goto(`${BASE}/signin`);
  await page.getByLabel("Email").fill(person.email);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
  await page.getByRole("button", { name: /^Workspace:/ }).waitFor();
  return page;
}

const shot = async (page, name) => {
  await page.waitForLoadState("networkidle");
  await sleep(400);
  await page.screenshot({ path: `${OUT}${name}.png` });
  console.log(`saved ${name}.png`);
};

async function ask(page, question) {
  await page.getByLabel("Question", { exact: true }).fill(question);
  await page.getByRole("button", { name: "Ask" }).click();
  await page.locator(".turn.answer:not(.streaming)").last().waitFor();
  await page.locator(".turn.answer.streaming").waitFor({ state: "detached", timeout: 120000 });
  await sleep(300);
}

// Sign-in page.
{
  const context = await browser.newContext({ viewport: { width: 1280, height: 800 } });
  const page = await context.newPage();
  await page.goto(`${BASE}/signin`);
  await page.getByRole("heading", { name: "Sign in" }).waitFor();
  await shot(page, "sign-in");
  await context.close();
}

const elif = await signedIn(people.elif);
await elif.goto(`${BASE}/w/${workspace.id}`);
await ask(elif, "How long does a refund take, and what happens to the shipping cost?");
await elif.locator(".turn.answer").last().locator(".cite").first().click();
await shot(elif, "answer");

await ask(elif, "Who painted the Mona Lisa?");
await shot(elif, "not-found");

await elif.getByRole("link", { name: "Ask" }).click();
await ask(elif, "Para iadesi ne kadar sürede karta yatar?");
await shot(elif, "answer-turkish");

await elif.getByRole("link", { name: "Documents" }).click();
await elif.locator('tr[data-document="Equipment care"]').waitFor();
await shot(elif, "documents");

await elif.locator('tr[data-document="Returns and refunds"]').click();
await elif.locator(".passage-heading", { hasText: "Refunds" }).waitFor();
await shot(elif, "document");

await elif.getByRole("link", { name: "Search" }).click();
await elif.getByLabel("Search", { exact: true }).fill("which carrier delivers parcels");
await elif.getByRole("button", { name: "Search", exact: true }).click();
await elif.locator(".hit").first().waitFor();
await shot(elif, "search");

await elif.getByRole("link", { name: "Members" }).click();
await elif.getByRole("row", { name: /Zeynep Kaya/ }).waitFor();
await shot(elif, "members");

// Phone.
{
  const phone = await signedIn(people.zeynep, { width: 390, height: 844 });
  await phone.goto(`${BASE}/w/${workspace.id}`);
  await ask(phone, "What is the escalation path for a lost parcel?");
  await shot(phone, "phone");
  await phone.context().close();
}

// API reference.
{
  const context = await browser.newContext({ viewport: { width: 1280, height: 800 } });
  const page = await context.newPage();
  await page.goto(`${BASE}/scalar`);
  await page.waitForLoadState("networkidle");
  await sleep(1500);
  await shot(page, "api-reference");
  await context.close();
}

// Walkthrough frames: a question typed, the passages found, the answer streaming in, a citation opened.
const film = await signedIn(people.mehmet, { width: 1100, height: 720 });
await film.goto(`${BASE}/w/${workspace.id}`);
await film.getByText(/Ask Demlik support/).waitFor();
let frame = 0;
const frames = async (count = 1, gap = 0) => {
  for (let i = 0; i < count; i++) {
    await film.screenshot({ path: `${OUT}frames/${String(frame++).padStart(3, "0")}.png` });
    if (gap) await sleep(gap);
  }
};
await frames(3);
await film.getByLabel("Question", { exact: true }).pressSequentially("How should the grinder burrs be cleaned?", { delay: 45 });
await frames(2);
await film.getByRole("button", { name: "Ask" }).click();
for (let i = 0; i < 200; i++) {
  await frames(1);
  if (!(await film.locator(".turn.answer.streaming").count()) && (await film.locator(".turn.answer").count())) break;
  await sleep(150);
}
await sleep(300);
await frames(3);
await film.locator(".turn.answer").last().locator(".cite").first().click();
await sleep(400);
await frames(6);
console.log(`saved ${frame} frames`);

await browser.close();
