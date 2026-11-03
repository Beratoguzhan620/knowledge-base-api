import type { DocumentFormat, DocumentStatus, Role } from "./types";

export function bytes(value: number): string {
  if (value < 1024) return `${value} B`;
  const units = ["KB", "MB", "GB", "TB"];
  let size = value / 1024;
  let unit = 0;
  while (size >= 1024 && unit < units.length - 1) {
    size /= 1024;
    unit++;
  }
  return `${size >= 10 || Number.isInteger(size) ? Math.round(size) : size.toFixed(1)} ${units[unit]}`;
}

/** "just now", "5 min ago", "3 h ago", "yesterday", then a date. */
export function relative(iso: string, now: Date = new Date()): string {
  const then = new Date(iso);
  const seconds = Math.round((now.getTime() - then.getTime()) / 1000);
  if (seconds < 45) return "just now";
  const minutes = Math.round(seconds / 60);
  if (minutes < 60) return `${minutes} min ago`;
  const hours = Math.round(minutes / 60);
  if (hours < 24) return `${hours} h ago`;
  const days = Math.floor(hours / 24);
  if (days === 1) return "yesterday";
  if (days < 7) return `${days} days ago`;
  return then.toLocaleDateString("en-GB", { day: "numeric", month: "short", year: then.getFullYear() === now.getFullYear() ? undefined : "numeric" });
}

export const canEdit = (role: Role) => role === "Editor" || role === "Owner";
export const isOwner = (role: Role) => role === "Owner";

export const roleHint: Record<Role, string> = {
  Reader: "Searches and asks questions",
  Editor: "Also adds and deletes documents",
  Owner: "Also manages members and the workspace",
};

export const statusLabel: Record<DocumentStatus, string> = {
  Queued: "Waiting",
  Indexing: "Indexing",
  Ready: "Ready",
  Failed: "Failed",
};

export const formatLabel: Record<DocumentFormat, string> = { Markdown: "Markdown", PlainText: "Text", Pdf: "PDF" };

export const accepted = ".md,.markdown,.txt,.text,.pdf";

/** Whether a file looks like one the knowledge base reads, by its name. */
export function isAccepted(name: string): boolean {
  return /\.(md|markdown|txt|text|pdf)$/i.test(name);
}

/** "Returns policy › Refunds, page 3". */
export function place(source: { documentTitle: string; heading: string | null; page: number | null }): string {
  const where = source.heading ? `${source.documentTitle} › ${source.heading}` : source.documentTitle;
  return source.page ? `${where}, page ${source.page}` : where;
}

export type AnswerPart = { kind: "text"; text: string } | { kind: "cite"; numbers: number[] };

/** Splits an answer into text and citation markers like [1] or [2, 3], so the markers can be shown as buttons. */
export function splitCitations(answer: string): AnswerPart[] {
  const parts: AnswerPart[] = [];
  const pattern = /\[(\d{1,2}(?:\s*,\s*\d{1,2})*)\]/g;
  let last = 0;
  for (const match of answer.matchAll(pattern)) {
    if (match.index! > last) parts.push({ kind: "text", text: answer.slice(last, match.index) });
    parts.push({ kind: "cite", numbers: match[1].split(",").map((n) => Number(n.trim())) });
    last = match.index! + match[0].length;
  }
  if (last < answer.length) parts.push({ kind: "text", text: answer.slice(last) });
  return parts;
}

export function initials(name: string): string {
  return name.split(/\s+/).filter(Boolean).slice(0, 2).map((part) => part[0]!.toLocaleUpperCase("tr")).join("");
}
