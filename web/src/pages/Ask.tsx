import { ArrowUp, FileUp, Loader2, MessageSquarePlus, MessagesSquare, Trash2 } from "lucide-react";
import { useEffect, useRef, useState, type FormEvent, type KeyboardEvent } from "react";
import { api } from "../api";
import { AnswerMessage, CitedText, SourceList } from "../components/Answer";
import { Empty, Failed, Loading } from "../components/States";
import { useWorkspace } from "../context";
import { canEdit, relative } from "../format";
import { useLoad } from "../hooks";
import { linkProps, navigate } from "../router";
import type { ConversationDetail, Message, Source } from "../types";

interface Pending { question: string; sources: Source[] | null; text: string }

function Conversations({ current, reloadKey }: { current?: string; reloadKey: number }) {
  const workspace = useWorkspace();
  const [list, reload] = useLoad(() => api.conversations(workspace.id), [workspace.id, reloadKey]);

  async function remove(id: string) {
    await api.deleteConversation(workspace.id, id);
    if (id === current) navigate(`/w/${workspace.id}`, true);
    reload();
  }

  return (
    <aside className="conversations" aria-label="Conversations">
      <a className="button wide" {...linkProps(`/w/${workspace.id}`)}><MessageSquarePlus size={16} />New question</a>
      <div className="conversation-list">
        {list.state === "failed" && <p className="small form-error">{list.error}</p>}
        {list.data?.length === 0 && <p className="small muted pad">Your questions appear here. Only you can see them.</p>}
        {list.data?.map((c) => (
          <div key={c.id} className={`conversation${c.id === current ? " active" : ""}`}>
            <a {...linkProps(`/w/${workspace.id}/c/${c.id}`)} className="grow" aria-current={c.id === current ? "page" : undefined}>
              <span className="ellipsis block">{c.title ?? "New conversation"}</span>
              <span className="small faint">{relative(c.updatedAt)}</span>
            </a>
            <button className="icon-button small-icon" aria-label={`Delete ${c.title ?? "conversation"}`} onClick={() => remove(c.id)}><Trash2 size={14} /></button>
          </div>
        ))}
      </div>
    </aside>
  );
}

export function Ask({ conversationId }: { conversationId?: string }) {
  const workspace = useWorkspace();
  const [usage] = useLoad(() => api.usage(workspace.id), [workspace.id]);
  const [conversation, , setConversation] = useLoad<ConversationDetail | null>(
    () => (conversationId ? api.conversation(workspace.id, conversationId) : Promise.resolve(null)), [workspace.id, conversationId]);
  const [question, setQuestion] = useState("");
  const [pending, setPending] = useState<Pending | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [listKey, setListKey] = useState(0);
  const abort = useRef<AbortController | null>(null);
  const bottom = useRef<HTMLDivElement>(null);
  const messages = conversation.data?.messages ?? [];

  useEffect(() => () => abort.current?.abort(), []);
  useEffect(() => { bottom.current?.scrollIntoView({ block: "end" }); }, [messages.length, pending?.text, pending?.sources]);

  async function ask(e?: FormEvent) {
    e?.preventDefault();
    const text = question.trim();
    if (!text || pending) return;
    setError(null);
    setQuestion("");
    setPending({ question: text, sources: null, text: "" });
    abort.current = new AbortController();
    try {
      let id = conversationId;
      if (!id) {
        id = (await api.startConversation(workspace.id)).id;
        setConversation({ id, title: null, createdAt: new Date().toISOString(), updatedAt: new Date().toISOString(), messages: [] });
        history.replaceState(null, "", `/w/${workspace.id}/c/${id}`);
      }

      const result = await api.ask(workspace.id, id, text, {
        onSources: (sources) => setPending((p) => (p ? { ...p, sources } : p)),
        onDelta: (piece) => setPending((p) => (p ? { ...p, text: p.text + piece } : p)),
      }, abort.current.signal);

      const before = conversation.data?.id === id ? conversation.data.messages : [];
      setConversation({ id, title: conversation.data?.title ?? text, createdAt: conversation.data?.createdAt ?? result.question.createdAt,
        updatedAt: result.answer.createdAt, messages: [...before, result.question, result.answer] });
      setListKey((k) => k + 1);
      if (!conversationId) navigate(`/w/${workspace.id}/c/${id}`, true);
    } catch (err) {
      if ((err as Error).name !== "AbortError") {
        setError((err as Error).message);
        setQuestion(text);
      }
    } finally {
      setPending(null);
    }
  }

  async function feedback(message: Message, helpful: boolean | null) {
    if (!conversation.data) return;
    const updated = await api.feedback(workspace.id, conversation.data.id, message.id, helpful);
    setConversation({ ...conversation.data, messages: conversation.data.messages.map((m) => (m.id === updated.id ? updated : m)) });
  }

  function onKey(e: KeyboardEvent<HTMLTextAreaElement>) {
    if (e.key === "Enter" && !e.shiftKey && !e.nativeEvent.isComposing) {
      e.preventDefault();
      void ask();
    }
  }

  const nothingReady = usage.data && usage.data.ready === 0;
  const stillIndexing = usage.data && usage.data.queued + usage.data.indexing > 0;

  return (
    <div className="ask">
      <Conversations current={conversationId} reloadKey={listKey} />
      <section className="chat" aria-label="Questions and answers">
        <div className="chat-scroll">
          {conversation.state === "failed" && <Failed message={conversation.error} />}
          {conversation.state === "loading" && conversationId && !pending && <Loading label="Loading the conversation" />}
          {messages.length === 0 && !pending && conversation.state !== "loading" && (
            nothingReady ? (
              <Empty title={stillIndexing ? "Your documents are being indexed" : "No documents to answer from yet"} icon={<FileUp size={26} />}>
                <span className="muted">{stillIndexing ? "Questions can be asked as soon as the first one is ready." : "Add Markdown, text or PDF files, and ask about them here."}</span>
                {canEdit(workspace.role) && <a className="button" {...linkProps(`/w/${workspace.id}/documents`)}>Go to documents</a>}
              </Empty>
            ) : (
              <Empty title={`Ask ${workspace.name}`} icon={<MessagesSquare size={26} />}>
                <span className="muted measure">Answers come only from this workspace's documents and cite the passages they use. Ask in English or Turkish.</span>
              </Empty>
            )
          )}
          {messages.map((m) => m.role === "User"
            ? <div key={m.id} className="turn question">{m.content}</div>
            : <AnswerMessage key={m.id} workspaceId={workspace.id} message={m} onFeedback={(h) => feedback(m, h)} />)}
          {pending && (
            <>
              <div className="turn question">{pending.question}</div>
              <article className="turn answer streaming" aria-live="polite" aria-busy="true">
                {pending.text
                  ? <CitedText text={pending.text} onCite={() => {}} />
                  : <span className="muted row"><Loader2 size={15} className="spin" />{pending.sources === null ? "Searching the documents…" : `Reading ${pending.sources.length} passage${pending.sources.length === 1 ? "" : "s"}…`}</span>}
                {pending.text && <span className="cursor" aria-hidden="true" />}
                {pending.sources && pending.sources.length > 0 && !pending.text && (
                  <SourceList workspaceId={workspace.id} sources={pending.sources} active={null} label="Passages found" />
                )}
              </article>
            </>
          )}
          <div ref={bottom} />
        </div>
        <form className="ask-box" onSubmit={ask}>
          {error && <p className="form-error" role="alert">{error}</p>}
          <div className="ask-input">
            <textarea className="textarea" rows={1} value={question} onChange={(e) => setQuestion(e.target.value)} onKeyDown={onKey}
              placeholder={nothingReady ? "Add documents to ask about them" : "Ask a question about your documents"} aria-label="Question" maxLength={2000}
              disabled={!!nothingReady} />
            <button className="button primary icon-only" disabled={!question.trim() || !!pending || !!nothingReady} aria-label="Ask"><ArrowUp size={18} /></button>
          </div>
          <span className="hint">Enter to ask, Shift+Enter for a new line.</span>
        </form>
      </section>
    </div>
  );
}
