export type Role = "Reader" | "Editor" | "Owner";
export type DocumentStatus = "Queued" | "Indexing" | "Ready" | "Failed";
export type DocumentFormat = "Markdown" | "PlainText" | "Pdf";
export type SearchMode = "Hybrid" | "Vector" | "Keyword";
export type Outcome = "Answered" | "NotFound" | "Failed";

export interface User { id: string; email: string; displayName: string }
export interface Session { accessToken: string; expiresAt: string; user: User }

export interface Workspace { id: string; name: string; role: Role; members: number; documents: number; createdAt: string }
export interface Me { user: User; workspaces: Workspace[] }

export interface Member { userId: string; displayName: string; email: string; role: Role; joinedAt: string }

export interface KbDocument {
  id: string; title: string; fileName: string; format: DocumentFormat; sizeBytes: number; language: string; status: DocumentStatus;
  error: string | null; chunkCount: number; characterCount: number; embeddingModel: string | null; uploadedBy: string;
  createdAt: string; indexedAt: string | null;
}
export interface DocumentPage { items: KbDocument[]; total: number; page: number; pageSize: number }
export interface Chunk { id: string; ordinal: number; heading: string | null; page: number | null; text: string }
export interface ChunkPage { items: Chunk[]; total: number; page: number; pageSize: number }
export interface Usage {
  storageBytes: number; storageLimitBytes: number; maxFileBytes: number; documents: number; chunks: number;
  queued: number; indexing: number; ready: number; failed: number;
}

export interface SearchHit {
  chunkId: string; documentId: string; documentTitle: string; ordinal: number; heading: string | null; page: number | null; text: string;
  score: number; vectorRank: number | null; keywordRank: number | null; similarity: number | null;
}
export interface SearchResult { query: string; mode: SearchMode; embeddingModel: string; hits: SearchHit[] }

export interface Source { number: number; chunkId: string; documentId: string; documentTitle: string; heading: string | null; page: number | null; quote: string }
export interface Message {
  id: string; role: "User" | "Assistant"; content: string; outcome: Outcome | null; model: string | null; citations: Source[];
  helpful: boolean | null; durationMs: number | null; createdAt: string;
}
export interface Conversation { id: string; title: string | null; createdAt: string; updatedAt: string }
export interface ConversationDetail extends Conversation { messages: Message[] }
export interface Answer { sources: Source[]; question: Message; answer: Message }
