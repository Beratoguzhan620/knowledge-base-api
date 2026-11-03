import { ArrowLeft, Download, Pencil, RefreshCw } from "lucide-react";
import { useEffect, useState, type FormEvent } from "react";
import { api } from "../api";
import { StatusBadge } from "../components/Badges";
import { Failed, Loading } from "../components/States";
import { useWorkspace } from "../context";
import { bytes, canEdit, formatLabel, relative } from "../format";
import { useLoad } from "../hooks";
import { linkProps } from "../router";
import type { Chunk } from "../types";

export function DocumentPage({ documentId }: { documentId: string }) {
  const workspace = useWorkspace();
  const [tick, setTick] = useState(0);
  const [doc, , setDoc] = useLoad(() => api.document(workspace.id, documentId), [workspace.id, documentId, tick]);
  const [chunks, setChunks] = useState<Chunk[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [renaming, setRenaming] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let current = true;
    api.chunks(workspace.id, documentId, page).then((result) => {
      if (!current) return;
      setChunks((before) => (page === 1 ? result.items : [...before, ...result.items]));
      setTotal(result.total);
    }, (e: Error) => current && setError(e.message));
    return () => { current = false; };
  }, [workspace.id, documentId, page]);

  useEffect(() => {
    if (doc.data?.status !== "Queued" && doc.data?.status !== "Indexing") return;
    const timer = setTimeout(() => setTick((t) => t + 1), 2000);
    return () => clearTimeout(timer);
  }, [doc.data]);

  async function rename(e: FormEvent) {
    e.preventDefault();
    if (!renaming) return;
    try {
      setDoc(await api.renameDocument(workspace.id, documentId, renaming));
      setRenaming(null);
    } catch (err) {
      setError((err as Error).message);
    }
  }

  async function download() {
    if (!doc.data) return;
    const blob = await api.download(workspace.id, doc.data);
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = doc.data.fileName;
    a.click();
    URL.revokeObjectURL(url);
  }

  if (doc.state === "failed" && !doc.data) return <Failed message={doc.error} />;
  if (!doc.data) return <Loading />;
  const d = doc.data;

  return (
    <>
      <a className="back" {...linkProps(`/w/${workspace.id}/documents`)}><ArrowLeft size={14} />Documents</a>
      <div className="page-head">
        <div className="grow">
          {renaming !== null ? (
            <form className="row" onSubmit={rename}>
              <input className="input" value={renaming} onChange={(e) => setRenaming(e.target.value)} aria-label="Title" autoFocus maxLength={200} />
              <button className="button primary">Save</button>
              <button type="button" className="button" onClick={() => setRenaming(null)}>Cancel</button>
            </form>
          ) : (
            <h1 className="row">{d.title}
              {canEdit(workspace.role) && <button className="icon-button" aria-label="Rename" title="Rename" onClick={() => setRenaming(d.title)}><Pencil size={15} /></button>}
            </h1>
          )}
          <p className="muted">{formatLabel[d.format]} · {bytes(d.sizeBytes)} · {d.chunkCount} passages · added {relative(d.createdAt)} by {d.uploadedBy}</p>
        </div>
        <div className="row">
          {canEdit(workspace.role) && d.status !== "Indexing" && (
            <button className="button" onClick={async () => setDoc(await api.reindex(workspace.id, d.id))}><RefreshCw size={15} />Index again</button>
          )}
          <button className="button" onClick={download}><Download size={15} />Download</button>
        </div>
      </div>

      <div className="doc-layout">
        <div className="card passages">
          {error && <p className="form-error" role="alert">{error}</p>}
          {chunks.map((c) => (
            <section key={c.id} className="passage" id={`chunk-${c.id}`}>
              <div className="passage-head">
                <span className="mono faint">#{c.ordinal + 1}</span>
                {c.heading && <span className="passage-heading">{c.heading}</span>}
                {c.page && <span className="badge plain">page {c.page}</span>}
              </div>
              <p className="passage-text">{c.text}</p>
            </section>
          ))}
          {chunks.length < total && <button className="button" onClick={() => setPage((p) => p + 1)}>Show more passages</button>}
        </div>
        <aside className="card section facts-card">
          <h2>Indexing</h2>
          <dl className="facts">
            <div><dt>Status</dt><dd><StatusBadge status={d.status} error={d.error} /></dd></div>
            {d.error && <div><dt>Last error</dt><dd className="danger-text small">{d.error}</dd></div>}
            <div><dt>Embedding model</dt><dd className="mono">{d.embeddingModel ?? "not indexed yet"}</dd></div>
            {d.indexedAt && <div><dt>Indexed</dt><dd>{relative(d.indexedAt)}</dd></div>}
            <div><dt>Language for keywords</dt><dd>{d.language}</dd></div>
            <div><dt>Characters</dt><dd>{d.characterCount.toLocaleString("en-GB")}</dd></div>
          </dl>
        </aside>
      </div>
    </>
  );
}
