import { describe, expect, it } from "vitest";
import { documentQueryString } from "./api";
import { bytes, isAccepted, place, relative, splitCitations } from "./format";
import { parseRoute } from "./router";
import { parseEvents } from "./stream";

describe("formatting", () => {
  it("shows sizes people read", () => {
    expect(bytes(512)).toBe("512 B");
    expect(bytes(1536)).toBe("1.5 KB");
    expect(bytes(10 * 1024 * 1024)).toBe("10 MB");
  });

  it("shows recent times relative to now", () => {
    const now = new Date("2026-11-03T12:00:00Z");
    expect(relative("2026-11-03T11:59:30Z", now)).toBe("just now");
    expect(relative("2026-11-03T11:40:00Z", now)).toBe("20 min ago");
    expect(relative("2026-11-02T10:00:00Z", now)).toBe("yesterday");
  });

  it("names where a passage comes from", () => {
    expect(place({ documentTitle: "Returns", heading: "Refunds", page: null })).toBe("Returns › Refunds");
    expect(place({ documentTitle: "Handbook", heading: null, page: 4 })).toBe("Handbook, page 4");
  });

  it("accepts the formats the server reads", () => {
    expect(isAccepted("notes.MD")).toBe(true);
    expect(isAccepted("guide.pdf")).toBe(true);
    expect(isAccepted("sheet.xlsx")).toBe(false);
  });
});

describe("answers", () => {
  it("splits citation markers out of the text", () => {
    expect(splitCitations("Refunds take 3 days [1]. Exchanges are free [2, 3].")).toEqual([
      { kind: "text", text: "Refunds take 3 days " },
      { kind: "cite", numbers: [1] },
      { kind: "text", text: ". Exchanges are free " },
      { kind: "cite", numbers: [2, 3] },
      { kind: "text", text: "." },
    ]);
    expect(splitCitations("No markers")).toEqual([{ kind: "text", text: "No markers" }]);
  });

  it("reads server-sent events even when they arrive cut in pieces", () => {
    const first = parseEvents("event: sources\ndata: [1]\n\nevent: delta\nda");
    expect(first.events).toEqual([{ event: "sources", data: "[1]" }]);
    const second = parseEvents(first.rest + "ta: {\"text\":\"Hi\"}\r\n\r\n: keep-alive\n\n");
    expect(second.events).toEqual([{ event: "delta", data: "{\"text\":\"Hi\"}" }]);
    expect(second.rest).toBe("");
  });
});

describe("routes", () => {
  const id = "0192f3c2-8a1b-7c4d-9e2f-3a4b5c6d7e8f";

  it("reads workspace pages from the address", () => {
    expect(parseRoute("/")).toEqual({ page: "home" });
    expect(parseRoute(`/w/${id}`)).toEqual({ page: "ask", workspaceId: id });
    expect(parseRoute(`/w/${id}/c/${id}`)).toEqual({ page: "ask", workspaceId: id, conversationId: id });
    expect(parseRoute(`/w/${id}/documents/${id}`)).toEqual({ page: "document", workspaceId: id, documentId: id });
    expect(parseRoute(`/w/${id}/search`, "?q=refund")).toEqual({ page: "search", workspaceId: id, q: "refund" });
    expect(parseRoute("/w/not-an-id/documents")).toEqual({ page: "not-found" });
  });

  it("builds document queries without empty parts", () => {
    expect(documentQueryString({})).toBe("");
    expect(documentQueryString({ q: " kargo ", status: "Failed", page: 2 })).toBe("?q=kargo&status=Failed&page=2");
  });
});
