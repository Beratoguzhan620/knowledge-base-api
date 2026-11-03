// An owner and a reader of one workspace, in separate browsers, against the whole Compose stack.
// Usage: AUTH_ATTEMPTS_PER_MINUTE=1000 docker compose -f docker-compose.yml -f docker-compose.light.yml up --build -d
//        cd tests/e2e && npm install && npx playwright install chromium && node kb.mjs
import { chromium } from "playwright";
import { mkdirSync, writeFileSync } from "node:fs";

const BASE = process.env.BASE_URL ?? "http://localhost:8080";
const OUT = new URL("./output/", import.meta.url).pathname;
const SAMPLES = new URL("../../samples/", import.meta.url).pathname;
mkdirSync(OUT, { recursive: true });

const run = Date.now().toString(36);
const owner = { name: "Elif Şahin", email: `elif.${run}@example.com`, password: "e2e-test-password" };
const reader = { name: "Can Öztürk", email: `can.${run}@example.com`, password: "e2e-test-password" };

const browser = await chromium.launch();
const errors = [];
let failures = 0;

async function step(name, action) {
  try {
    await action();
    console.log(`ok   ${name}`);
  } catch (error) {
    failures++;
    console.log(`FAIL ${name}\n     ${String(error.message ?? error).split("\n")[0]}`);
  }
}

async function open(viewport = { width: 1280, height: 860 }) {
  const context = await browser.newContext({ viewport, locale: "en-GB" });
  const page = await context.newPage();
  page.on("pageerror", (error) => errors.push(error.message));
  page.on("console", (message) => {
    if (message.type() === "error" && !/Failed to load resource/.test(message.text())) errors.push(message.text());
  });
  return page;
}

async function register(page, person, workspace) {
  await page.goto(BASE);
  await page.getByRole("heading", { name: "Sign in" }).waitFor();
  await page.getByRole("link", { name: "Create an account" }).click();
  await page.getByLabel("Your name").fill(person.name);
  await page.getByLabel("Email").fill(person.email);
  await page.getByLabel("Password").fill(person.password);
  if (workspace) await page.getByLabel("First workspace").fill(workspace);
  await page.getByRole("button", { name: "Create account" }).click();
}

const elif = await open();
const can = await open();
let workspaceUrl = "";

await step("a visitor is sent to sign in, creates an account and lands in a workspace with nothing to ask about yet", async () => {
  await register(elif, owner, "Demlik destek");
  await elif.getByRole("button", { name: "Workspace: Demlik destek" }).waitFor();
  await elif.getByText("No documents to answer from yet").waitFor();
  workspaceUrl = elif.url().replace(/\/$/, "");
});

await step("documents are uploaded, unreadable files are refused, and every document becomes ready", async () => {
  await elif.getByRole("link", { name: "Go to documents" }).click();
  writeFileSync(`${OUT}prices.xlsx`, "PK\u0003\u0004 not really a spreadsheet");
  await elif.getByLabel("Choose files").setInputFiles([
    `${SAMPLES}returns-and-refunds.md`, `${SAMPLES}shipping.md`, `${SAMPLES}brewing-guide.md`, `${SAMPLES}equipment-care.pdf`, `${OUT}prices.xlsx`,
  ]);
  await elif.locator(".upload.failed", { hasText: "prices.xlsx" }).getByText("Only Markdown, text and PDF files.").waitFor();
  for (const title of ["Returns and refunds", "Shipping", "Brewing guide", "Equipment care"]) {
    await elif.locator(`tr[data-document="${title}"]`).getByText("Ready").waitFor({ timeout: 60000 });
  }
  await elif.getByText("4 ready of 4").waitFor();
});

await step("the same file twice is refused with a reason", async () => {
  await elif.getByLabel("Choose files").setInputFiles(`${SAMPLES}shipping.md`);
  await elif.locator(".upload.failed").getByText("This file is already in the workspace.").waitFor();
});

await step("a document shows its passages with their headings, and a PDF its pages", async () => {
  await elif.locator('tr[data-document="Returns and refunds"]').click();
  await elif.getByRole("heading", { name: /Returns and refunds/ }).waitFor();
  await elif.locator(".passage-heading", { hasText: "Refunds" }).waitFor();
  await elif.getByText("local-hashing-v1").waitFor();
  await elif.goto(`${workspaceUrl}/documents`);
  await elif.locator('tr[data-document="Equipment care"]').click();
  await elif.locator(".passage .badge", { hasText: "page 3" }).waitFor();
});

await step("search shows where each passage ranked, by meaning and by words", async () => {
  await elif.getByRole("link", { name: "Search" }).click();
  await elif.getByLabel("Search", { exact: true }).fill("KHV-002");
  await elif.getByRole("tab", { name: "Words" }).click();
  await elif.getByRole("button", { name: "Search", exact: true }).click();
  await elif.locator(".hit").first().getByText("Brewing guide › Product codes").waitFor();
  await elif.locator(".hit").first().getByText("Words #1").waitFor();
  await elif.getByRole("tab", { name: "Hybrid" }).click();
  await elif.locator(".hit").first().getByText(/Meaning #\d/).waitFor();
});

await step("a question is answered from the documents, citing the passage it comes from", async () => {
  await elif.getByRole("link", { name: "Ask" }).click();
  await elif.getByLabel("Question", { exact: true }).fill("How long does a refund take to reach the card?");
  await elif.getByRole("button", { name: "Ask" }).click();
  const answer = elif.locator(".turn.answer").last();
  await answer.locator(".answer-text", { hasText: "3 working days" }).waitFor({ timeout: 30000 });
  await answer.locator(".source-place", { hasText: "Returns and refunds › Refunds" }).waitFor();
  await answer.locator(".cite").first().click();
  await answer.locator(".source.active").waitFor();
  await elif.waitForURL(/\/c\/[0-9a-f-]{36}$/);
  await elif.locator(".conversation", { hasText: "How long does a refund take" }).waitFor();
});

await step("a question the documents do not cover is not answered", async () => {
  await elif.getByLabel("Question", { exact: true }).fill("Who painted the Mona Lisa?");
  await elif.getByLabel("Question", { exact: true }).press("Enter");
  const answer = elif.locator(".turn.answer").last();
  await answer.getByText("Not in the documents").waitFor({ timeout: 30000 });
  await answer.getByText(/could not find an answer/).waitFor();
});

await step("answers can be rated", async () => {
  const first = elif.locator(".turn.answer").first();
  await first.getByRole("button", { name: "Helpful", exact: true }).click();
  await first.locator('button[aria-label="Helpful"][aria-pressed="true"]').waitFor();
  await elif.reload();
  await elif.locator('.turn.answer button[aria-label="Helpful"][aria-pressed="true"]').waitFor();
});

await step("the owner adds a reader, who asks in their own private conversations and cannot add documents", async () => {
  await register(can, reader);
  await can.getByRole("button", { name: /^Workspace: My workspace/ }).waitFor();
  await elif.getByRole("link", { name: "Members" }).click();
  await elif.getByLabel("Email").fill(reader.email);
  await elif.getByRole("button", { name: "Add" }).click();
  await elif.getByRole("row", { name: /Can Öztürk/ }).waitFor();

  await can.goto(workspaceUrl);
  await can.getByRole("button", { name: "Workspace: Demlik destek" }).waitFor();
  await can.getByText("Your questions appear here. Only you can see them.").waitFor();
  await can.getByLabel("Question", { exact: true }).fill("Which carrier delivers the parcels?");
  await can.getByRole("button", { name: "Ask" }).click();
  await can.locator(".turn.answer").last().getByText(/Yurtiçi Kargo/).first().waitFor({ timeout: 30000 });

  await can.getByRole("link", { name: "Documents" }).click();
  await can.locator('tr[data-document="Shipping"]').waitFor();
  if (await can.getByLabel("Choose files").count()) throw new Error("a reader can upload");
  if (await can.getByRole("button", { name: /^Delete / }).count()) throw new Error("a reader can delete");
});

await step("a removed member loses the workspace at once", async () => {
  await elif.getByRole("button", { name: "Remove Can Öztürk" }).click();
  await elif.getByRole("row", { name: /Can Öztürk/ }).waitFor({ state: "detached" });
  await can.goto(workspaceUrl);
  await can.getByText("This page does not exist").waitFor();
});

await step("the app works on a phone-sized screen", async () => {
  await elif.setViewportSize({ width: 390, height: 844 });
  for (const path of ["", "/documents", "/search", "/members"]) {
    await elif.goto(workspaceUrl + path);
    await elif.waitForLoadState("networkidle");
    const overflow = await elif.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
    if (overflow > 1) throw new Error(`${path || "/"} scrolls sideways by ${overflow}px`);
  }
});

await step("no errors in the browser console", async () => {
  if (errors.length) throw new Error(errors.join(" | "));
});

await elif.screenshot({ path: `${OUT}last.png` });
await browser.close();
if (failures) {
  console.log(`\n${failures} step(s) failed`);
  process.exit(1);
}
console.log("\nall steps passed");
