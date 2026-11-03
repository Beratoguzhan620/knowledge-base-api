import { AlertTriangle, FileText, SearchX, ThumbsDown, ThumbsUp } from "lucide-react";
import { useState } from "react";
import { linkProps } from "../router";
import { place, splitCitations } from "../format";
import type { Message, Source } from "../types";

/** The answer's text with its citation markers as small numbered buttons that point at the sources below. */
export function CitedText({ text, onCite }: { text: string; onCite: (number: number) => void }) {
  return (
    <div className="answer-text">
      {splitCitations(text).map((part, i) =>
        part.kind === "text" ? <span key={i}>{part.text}</span> : (
          <span key={i} className="cites">
            {part.numbers.map((n) => (
              <button key={n} type="button" className="cite" onClick={() => onCite(n)} aria-label={`Source ${n}`}>{n}</button>
            ))}
          </span>
        ))}
    </div>
  );
}

export function SourceList({ workspaceId, sources, active, label, compact = false }: {
  workspaceId: string; sources: Source[]; active: number | null; label: string; compact?: boolean;
}) {
  if (sources.length === 0) return null;
  return (
    <div className={`sources${compact ? " compact" : ""}`}>
      <span className="sources-label">{label}</span>
      <ol>
        {sources.map((s) => (
          <li key={s.number} id={`source-${s.chunkId}`} className={`source${active === s.number ? " active" : ""}`}>
            <span className="source-number">{s.number}</span>
            <div className="grow">
              <a className="source-place" {...linkProps(`/w/${workspaceId}/documents/${s.documentId}`)}>
                <FileText size={13} />{place(s)}
              </a>
              {!compact && <p className="source-quote">{s.quote}</p>}
            </div>
          </li>
        ))}
      </ol>
    </div>
  );
}

export function AnswerMessage({ workspaceId, message, onFeedback }: {
  workspaceId: string;
  message: Message;
  onFeedback?: (helpful: boolean | null) => void;
}) {
  const [active, setActive] = useState<number | null>(null);
  const cite = (n: number) => {
    setActive(n);
    const source = message.citations.find((c) => c.number === n);
    if (source) document.getElementById(`source-${source.chunkId}`)?.scrollIntoView({ behavior: "smooth", block: "nearest" });
  };

  return (
    <article className={`turn answer outcome-${message.outcome}`} aria-label="Answer">
      {message.outcome === "NotFound" && <span className="badge info"><SearchX size={12} />Not in the documents</span>}
      {message.outcome === "Failed" && <span className="badge danger"><AlertTriangle size={12} />Model unavailable</span>}
      <CitedText text={message.content} onCite={cite} />
      <SourceList workspaceId={workspaceId} sources={message.citations} active={active} label="Sources" />
      <div className="answer-meta">
        {message.model && <span className="mono small faint">{message.model}{message.durationMs != null && ` · ${(message.durationMs / 1000).toFixed(1)} s`}</span>}
        {onFeedback && message.outcome === "Answered" && (
          <span className="feedback">
            <button type="button" className={`icon-button${message.helpful === true ? " on" : ""}`} aria-pressed={message.helpful === true}
              aria-label="Helpful" title="Helpful" onClick={() => onFeedback(message.helpful === true ? null : true)}><ThumbsUp size={15} /></button>
            <button type="button" className={`icon-button${message.helpful === false ? " on" : ""}`} aria-pressed={message.helpful === false}
              aria-label="Not helpful" title="Not helpful" onClick={() => onFeedback(message.helpful === false ? null : false)}><ThumbsDown size={15} /></button>
          </span>
        )}
      </div>
    </article>
  );
}
