import { FileText, Search as SearchIcon } from "lucide-react";
import { useEffect, useState, type FormEvent } from "react";
import { api } from "../api";
import { Empty, Failed, Loading } from "../components/States";
import { useWorkspace } from "../context";
import { place } from "../format";
import { linkProps } from "../router";
import type { SearchMode, SearchResult } from "../types";

const modes: { mode: SearchMode; label: string; hint: string }[] = [
  { mode: "Hybrid", label: "Hybrid", hint: "Meaning and words, merged" },
  { mode: "Vector", label: "Meaning", hint: "Nearest embeddings only" },
  { mode: "Keyword", label: "Words", hint: "Full-text search only" },
];

export function Search({ initial }: { initial?: string }) {
  const workspace = useWorkspace();
  const [q, setQ] = useState(initial ?? "");
  const [mode, setMode] = useState<SearchMode>("Hybrid");
  const [result, setResult] = useState<SearchResult | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function run(query = q, how = mode) {
    if (!query.trim()) return;
    setBusy(true);
    setError(null);
    try {
      setResult(await api.search(workspace.id, query, how));
      history.replaceState(null, "", `/w/${workspace.id}/search?q=${encodeURIComponent(query)}`);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  useEffect(() => { if (initial) void run(initial); }, []); // eslint-disable-line react-hooks/exhaustive-deps

  const submit = (e: FormEvent) => { e.preventDefault(); void run(); };
  const best = result?.hits[0]?.score ?? 1;

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Search</h1>
          <p className="muted">The passages a question would be answered from, and where each one ranked.</p>
        </div>
      </div>
      <form className="card search-box" onSubmit={submit}>
        <div className="search grow">
          <SearchIcon size={16} />
          <input className="input" value={q} onChange={(e) => setQ(e.target.value)} placeholder="Search the workspace" aria-label="Search" autoFocus />
        </div>
        <div className="tabs" role="tablist" aria-label="Search mode">
          {modes.map((m) => (
            <button key={m.mode} type="button" role="tab" title={m.hint} aria-selected={mode === m.mode} className={`tab${mode === m.mode ? " active" : ""}`}
              onClick={() => { setMode(m.mode); if (result) void run(q, m.mode); }}>{m.label}</button>
          ))}
        </div>
        <button className="button primary" disabled={busy || !q.trim()}>Search</button>
      </form>

      {error && <Failed message={error} />}
      {busy && !result && <Loading label="Searching" />}
      {result && result.hits.length === 0 && <Empty title="No passages match">Try other words, or the Meaning mode.</Empty>}
      {result && result.hits.length > 0 && (
        <ol className="hits" aria-label="Results">
          {result.hits.map((h, i) => (
            <li key={h.chunkId} className="card hit">
              <div className="hit-head">
                <span className="hit-rank">{i + 1}</span>
                <a className="source-place grow" {...linkProps(`/w/${workspace.id}/documents/${h.documentId}`)}><FileText size={13} />{place(h)}</a>
                <span className="score" title={`Fused score ${h.score.toFixed(4)}`}><span style={{ width: `${Math.round((h.score / best) * 100)}%` }} /></span>
              </div>
              <p className="hit-text">{h.text}</p>
              <div className="row wrap small">
                {h.vectorRank != null && <span className="badge plain">Meaning #{h.vectorRank}{h.similarity != null && ` · ${h.similarity.toFixed(2)}`}</span>}
                {h.keywordRank != null && <span className="badge plain">Words #{h.keywordRank}</span>}
              </div>
            </li>
          ))}
        </ol>
      )}
      {result && <p className="small faint mono pad">{result.embeddingModel}</p>}
    </>
  );
}
