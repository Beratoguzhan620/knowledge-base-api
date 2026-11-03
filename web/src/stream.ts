export interface SseEvent { event: string; data: string }

/**
 * Splits a server-sent event stream into events. Lines can arrive cut anywhere, so text is buffered until a blank
 * line ends an event; several "data:" lines are joined with newlines, as the format says.
 */
export function parseEvents(buffer: string): { events: SseEvent[]; rest: string } {
  const events: SseEvent[] = [];
  const normalized = buffer.replace(/\r\n?/g, "\n");
  const blocks = normalized.split("\n\n");
  const rest = blocks.pop() ?? "";
  for (const block of blocks) {
    let event = "message";
    const data: string[] = [];
    for (const line of block.split("\n")) {
      if (line.startsWith(":")) continue;
      const colon = line.indexOf(":");
      const field = colon < 0 ? line : line.slice(0, colon);
      const value = colon < 0 ? "" : line.slice(colon + 1).replace(/^ /, "");
      if (field === "event") event = value;
      else if (field === "data") data.push(value);
    }
    if (data.length > 0) events.push({ event, data: data.join("\n") });
  }
  return { events, rest };
}

export async function* readEvents(body: ReadableStream<Uint8Array>): AsyncGenerator<SseEvent> {
  const reader = body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";
  try {
    while (true) {
      const { value, done } = await reader.read();
      if (done) break;
      const { events, rest } = parseEvents(buffer + decoder.decode(value, { stream: true }));
      buffer = rest;
      yield* events;
    }
    const { events } = parseEvents(buffer + decoder.decode() + "\n\n");
    yield* events;
  } finally {
    reader.releaseLock();
  }
}
