import type {
  Answer, ChunkPage, Conversation, ConversationDetail, DocumentPage, DocumentStatus, KbDocument, Me, Member, Message, Role, SearchMode,
  SearchResult, Session, Source, Usage, Workspace,
} from "./types";
import { readEvents } from "./stream";

/** An API error with the stable code from the problem response, such as "duplicate_document". */
export class ApiError extends Error {
  constructor(
    message: string,
    public readonly status: number,
    public readonly code: string | null,
    public readonly fields: Record<string, string[]> = {},
  ) {
    super(message);
  }
}

let token: string | null = null;
let onUnauthorized: () => void = () => {};

export function setToken(value: string | null, unauthorized: () => void) {
  token = value;
  onUnauthorized = unauthorized;
}

async function send(path: string, init: RequestInit = {}): Promise<Response> {
  const headers = new Headers(init.headers);
  if (typeof init.body === "string") headers.set("Content-Type", "application/json");
  if (token && !headers.has("Authorization")) headers.set("Authorization", `Bearer ${token}`);

  let response: Response;
  try {
    response = await fetch(path, { ...init, headers });
  } catch (e) {
    if ((e as Error).name === "AbortError") throw e;
    throw new ApiError("The server cannot be reached. Check your connection.", 0, null);
  }

  if (response.status === 401 && token) onUnauthorized();
  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    const fields = (problem?.errors ?? {}) as Record<string, string[]>;
    const fallback = response.status === 429 ? "Too many requests. Wait a minute and try again."
      : response.status === 413 ? "The file is too large."
      : "Something went wrong. Try again.";
    const detail = problem?.detail ?? Object.values(fields).flat()[0] ?? fallback;
    throw new ApiError(detail, response.status, problem?.code ?? null, fields);
  }

  return response;
}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const response = await send(path, init);
  return response.status === 204 ? (undefined as T) : ((await response.json()) as T);
}

const json = (body: unknown) => JSON.stringify(body);
const post = (body: unknown): RequestInit => ({ method: "POST", body: json(body) });
const ws = (id: string) => `/api/workspaces/${id}`;

export interface DocumentQuery { q?: string; status?: DocumentStatus; page?: number; pageSize?: number }

export function documentQueryString(query: DocumentQuery): string {
  const params = new URLSearchParams();
  if (query.q?.trim()) params.set("q", query.q.trim());
  if (query.status) params.set("status", query.status);
  if (query.page && query.page > 1) params.set("page", String(query.page));
  if (query.pageSize) params.set("pageSize", String(query.pageSize));
  const text = params.toString();
  return text ? `?${text}` : "";
}

/** What the answer stream reports while an answer is being written. */
export interface AnswerHandlers {
  onSources: (sources: Source[]) => void;
  onDelta: (text: string) => void;
}

export const api = {
  register: (body: { email: string; displayName: string; password: string; workspaceName?: string }) =>
    request<Session>("/api/auth/register", post(body)),
  login: (email: string, password: string) => request<Session>("/api/auth/login", post({ email, password })),
  me: () => request<Me>("/api/me"),

  createWorkspace: (name: string) => request<Workspace>("/api/workspaces", post({ name })),
  renameWorkspace: (id: string, name: string) => request<Workspace>(ws(id), { method: "PATCH", body: json({ name }) }),
  deleteWorkspace: (id: string) => request<void>(ws(id), { method: "DELETE" }),
  usage: (id: string) => request<Usage>(`${ws(id)}/usage`),
  reindexWorkspace: (id: string, all = false) => request<{ queued: number }>(`${ws(id)}/reindex${all ? "?all=true" : ""}`, { method: "POST" }),

  members: (id: string) => request<Member[]>(`${ws(id)}/members`),
  addMember: (id: string, email: string, role: Role) => request<Member>(`${ws(id)}/members`, post({ email, role })),
  changeRole: (id: string, userId: string, role: Role) => request<Member>(`${ws(id)}/members/${userId}`, { method: "PATCH", body: json({ role }) }),
  removeMember: (id: string, userId: string) => request<void>(`${ws(id)}/members/${userId}`, { method: "DELETE" }),

  documents: (id: string, query: DocumentQuery) => request<DocumentPage>(`${ws(id)}/documents${documentQueryString(query)}`),
  document: (id: string, documentId: string) => request<KbDocument>(`${ws(id)}/documents/${documentId}`),
  chunks: (id: string, documentId: string, page: number) => request<ChunkPage>(`${ws(id)}/documents/${documentId}/chunks?page=${page}&pageSize=20`),
  upload: (id: string, file: File) => {
    const form = new FormData();
    form.append("file", file);
    return request<KbDocument>(`${ws(id)}/documents`, { method: "POST", body: form });
  },
  renameDocument: (id: string, documentId: string, title: string) =>
    request<KbDocument>(`${ws(id)}/documents/${documentId}`, { method: "PATCH", body: json({ title }) }),
  reindex: (id: string, documentId: string) => request<KbDocument>(`${ws(id)}/documents/${documentId}/reindex`, { method: "POST" }),
  deleteDocument: (id: string, documentId: string) => request<void>(`${ws(id)}/documents/${documentId}`, { method: "DELETE" }),
  download: async (id: string, document: KbDocument) => (await send(`${ws(id)}/documents/${document.id}/file`)).blob(),

  search: (id: string, q: string, mode: SearchMode) =>
    request<SearchResult>(`${ws(id)}/search?q=${encodeURIComponent(q)}&mode=${mode}&limit=10`),

  conversations: (id: string) => request<Conversation[]>(`${ws(id)}/conversations`),
  startConversation: (id: string) => request<Conversation>(`${ws(id)}/conversations`, post({})),
  conversation: (id: string, conversationId: string) => request<ConversationDetail>(`${ws(id)}/conversations/${conversationId}`),
  renameConversation: (id: string, conversationId: string, title: string) =>
    request<Conversation>(`${ws(id)}/conversations/${conversationId}`, { method: "PATCH", body: json({ title }) }),
  deleteConversation: (id: string, conversationId: string) => request<void>(`${ws(id)}/conversations/${conversationId}`, { method: "DELETE" }),
  feedback: (id: string, conversationId: string, messageId: string, helpful: boolean | null) =>
    request<Message>(`${ws(id)}/conversations/${conversationId}/messages/${messageId}/feedback`, { method: "PUT", body: json({ helpful }) }),

  /** Asks a question and streams the answer: sources first, then the text as it is written, then the stored answer. */
  ask: async (id: string, conversationId: string, question: string, handlers: AnswerHandlers, signal?: AbortSignal): Promise<Answer> => {
    const response = await send(`${ws(id)}/conversations/${conversationId}/messages`, {
      ...post({ question }),
      headers: { Accept: "text/event-stream" },
      signal,
    });
    let sources: Source[] = [];
    for await (const event of readEvents(response.body!)) {
      const data = JSON.parse(event.data);
      if (event.event === "sources") {
        sources = data as Source[];
        handlers.onSources(sources);
      } else if (event.event === "delta") {
        handlers.onDelta((data as { text: string }).text);
      } else if (event.event === "done") {
        const done = data as { question: Message; answer: Message };
        return { sources, question: done.question, answer: done.answer };
      } else if (event.event === "error") {
        throw new ApiError((data as { detail: string }).detail, 500, null);
      }
    }

    throw new ApiError("The answer stopped before it was finished. Try again.", 0, null);
  },
};
