import { CheckCircle2, FileText, FileUp, Loader2, RefreshCw, Search, Trash2, XCircle } from "lucide-react";
import { useEffect, useRef, useState, type DragEvent } from "react";
import { api, ApiError } from "../api";
import { StatusBadge } from "../components/Badges";
import { Dialog } from "../components/Dialog";
import { Empty, Failed, SkeletonRows } from "../components/States";
import { useSession, useWorkspace } from "../context";
import { accepted, bytes, canEdit, formatLabel, isAccepted, relative } from "../format";
import { useDebounced, useLoad } from "../hooks";
import { linkProps, navigate } from "../router";
import type { DocumentStatus, KbDocument } from "../types";

interface UploadItem { name: string; state: "waiting" | "uploading" | "done" | "failed"; message?: string }

function UsageMeter({ used, limit }: { used: number; limit: number }) {
  const share = Math.min(100, Math.round((used / limit) * 100));
  return (
    <div className="usage" title={`${bytes(used)} of ${bytes(limit)}`}>
      <span className="small muted">{bytes(used)} of {bytes(limit)}</span>
      <div className={`bar${share >= 90 ? " full" : ""}`}><span style={{ width: `${share}%` }} /></div>
    </div>
  );
}

function Uploader({ onUploaded }: { onUploaded: () => void }) {
  const workspace = useWorkspace();
  const { refresh } = useSession();
  const [items, setItems] = useState<UploadItem[]>([]);
  const [over, setOver] = useState(false);
  const input = useRef<HTMLInputElement>(null);

  async function upload(files: File[]) {
    const queue: UploadItem[] = files.map((f) => ({ name: f.name, state: "waiting" }));
    setItems(queue);
    const update = (i: number, item: Partial<UploadItem>) => setItems((all) => all.map((x, j) => (j === i ? { ...x, ...item } : x)));
    for (const [i, file] of files.entries()) {
      if (!isAccepted(file.name)) {
        update(i, { state: "failed", message: "Only Markdown, text and PDF files." });
        continue;
      }
      update(i, { state: "uploading" });
      try {
        await api.upload(workspace.id, file);
        update(i, { state: "done" });
        onUploaded();
      } catch (e) {
        update(i, { state: "failed", message: e instanceof ApiError ? e.message : "Upload failed." });
      }
    }
    void refresh();
  }

  function drop(e: DragEvent) {
    e.preventDefault();
    setOver(false);
    void upload([...e.dataTransfer.files]);
  }

  return (
    <div className="stack-8">
      <div className={`dropzone${over ? " over" : ""}`} onDragOver={(e) => { e.preventDefault(); setOver(true); }} onDragLeave={() => setOver(false)} onDrop={drop}>
        <FileUp size={22} />
        <span><strong>Drop files here</strong> or <button type="button" className="link" onClick={() => input.current?.click()}>choose files</button></span>
        <span className="small muted">Markdown, text or PDF, up to 10 MB each</span>
        <input ref={input} type="file" accept={accepted} multiple hidden aria-label="Choose files"
          onChange={(e) => { void upload([...(e.target.files ?? [])]); e.target.value = ""; }} />
      </div>
      {items.length > 0 && (
        <ul className="uploads" aria-label="Uploads">
          {items.map((item, i) => (
            <li key={i} className={`upload ${item.state}`}>
              {item.state === "uploading" && <Loader2 size={15} className="spin" />}
              {item.state === "done" && <CheckCircle2 size={15} />}
              {item.state === "failed" && <XCircle size={15} />}
              {item.state === "waiting" && <FileText size={15} />}
              <span className="ellipsis grow">{item.name}</span>
              <span className="small">{item.state === "failed" ? item.message : item.state === "done" ? "Added, indexing" : item.state === "uploading" ? "Reading…" : "Waiting"}</span>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

const filters: (DocumentStatus | undefined)[] = [undefined, "Ready", "Queued", "Indexing", "Failed"];

export function Documents() {
  const workspace = useWorkspace();
  const { refresh } = useSession();
  const [q, setQ] = useState("");
  const [status, setStatus] = useState<DocumentStatus | undefined>();
  const [tick, setTick] = useState(0);
  const [deleting, setDeleting] = useState<KbDocument | null>(null);
  const query = useDebounced(q);
  const [page, reload] = useLoad(() => api.documents(workspace.id, { q: query, status, pageSize: 100 }), [workspace.id, query, status, tick]);
  const [usage, reloadUsage] = useLoad(() => api.usage(workspace.id), [workspace.id, tick]);
  const editor = canEdit(workspace.role);
  const busy = (usage.data?.queued ?? 0) + (usage.data?.indexing ?? 0) > 0;

  // While anything waits for indexing, check again every two seconds.
  useEffect(() => {
    if (!busy) return;
    const timer = setTimeout(() => setTick((t) => t + 1), 2000);
    return () => clearTimeout(timer);
  }, [busy, page.data, usage.data]);

  async function reindex(doc: KbDocument) {
    await api.reindex(workspace.id, doc.id);
    setTick((t) => t + 1);
  }

  async function remove(doc: KbDocument) {
    await api.deleteDocument(workspace.id, doc.id);
    setDeleting(null);
    reload();
    reloadUsage();
    void refresh();
  }

  const empty = page.data && page.data.total === 0 && !query && !status;

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Documents</h1>
          <p className="muted">{usage.data ? `${usage.data.ready} ready of ${usage.data.documents} · ${usage.data.chunks} passages` : " "}</p>
        </div>
        {usage.data && <UsageMeter used={usage.data.storageBytes} limit={usage.data.storageLimitBytes} />}
      </div>

      {editor && <Uploader onUploaded={() => setTick((t) => t + 1)} />}

      <div className="card table-card">
        <div className="toolbar">
          <div className="search">
            <Search size={16} />
            <input className="input" placeholder="Find a document by name" value={q} onChange={(e) => setQ(e.target.value)} aria-label="Find a document" />
          </div>
          <div className="tabs" role="tablist" aria-label="Status">
            {filters.map((f) => (
              <button key={f ?? "all"} role="tab" aria-selected={status === f} className={`tab${status === f ? " active" : ""}`} onClick={() => setStatus(f)}>
                {f ?? "All"}
              </button>
            ))}
          </div>
          {busy && <span className="small muted row"><Loader2 size={14} className="spin" />Indexing</span>}
        </div>
        {page.state === "failed" && !page.data && <Failed message={page.error} onRetry={reload} />}
        {empty ? (
          <Empty title="No documents yet" icon={<FileText size={24} />}>
            <span className="muted">{editor ? "Add files above. They are searchable as soon as they are indexed." : "Editors and owners can add documents."}</span>
          </Empty>
        ) : page.data?.items.length === 0 ? (
          <Empty title="Nothing matches" />
        ) : (
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr><th>Document</th><th>Status</th><th className="hide-sm">Passages</th><th className="hide-sm">Size</th><th className="hide-sm">Added</th><th aria-label="Actions" /></tr>
              </thead>
              {!page.data ? <SkeletonRows columns={6} /> : (
                <tbody>
                  {page.data.items.map((d) => (
                    <tr key={d.id} className="clickable" onClick={() => navigate(`/w/${workspace.id}/documents/${d.id}`)} data-document={d.title}>
                      <td>
                        <a className="doc-title" {...linkProps(`/w/${workspace.id}/documents/${d.id}`)} onClick={(e) => { e.preventDefault(); }}>{d.title}</a>
                        <span className="small muted block">{formatLabel[d.format]} · {d.fileName}</span>
                      </td>
                      <td>
                        <StatusBadge status={d.status} error={d.error} />
                        {d.status === "Failed" && d.error && <span className="small danger-text block measure-sm">{d.error}</span>}
                      </td>
                      <td className="hide-sm">{d.chunkCount}</td>
                      <td className="hide-sm">{bytes(d.sizeBytes)}</td>
                      <td className="hide-sm"><span title={new Date(d.createdAt).toLocaleString("en-GB")}>{relative(d.createdAt)}</span><span className="small muted block">{d.uploadedBy}</span></td>
                      <td className="actions" onClick={(e) => e.stopPropagation()}>
                        {editor && d.status === "Failed" && (
                          <button className="icon-button" aria-label={`Index ${d.title} again`} title="Index again" onClick={() => reindex(d)}><RefreshCw size={15} /></button>
                        )}
                        {editor && <button className="icon-button" aria-label={`Delete ${d.title}`} title="Delete" onClick={() => setDeleting(d)}><Trash2 size={15} /></button>}
                      </td>
                    </tr>
                  ))}
                </tbody>
              )}
            </table>
          </div>
        )}
      </div>

      {deleting && (
        <Dialog title="Delete document" onClose={() => setDeleting(null)}>
          <p>Delete <strong>{deleting.title}</strong> and its {deleting.chunkCount} passages? Answers that cited it keep their quotes.</p>
          <div className="dialog-actions">
            <button className="button" onClick={() => setDeleting(null)}>Cancel</button>
            <button className="button danger" onClick={() => remove(deleting)}>Delete</button>
          </div>
        </Dialog>
      )}
    </>
  );
}
