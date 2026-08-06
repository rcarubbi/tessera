"use client";

import { useEffect, useRef, useState } from "react";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { streamChat, type ChatStreamEvent, type Citation } from "@/lib/api";

type Turn = {
  question: string;
  answer: string;
  mode?: string;
  warnings: string[];
  citations: Citation[];
  error?: string;
};

export default function ChatPanel({
  repoId,
  commit,
  onSelect,
}: {
  repoId: string;
  commit: string | null;
  onSelect: (key: string) => void;
}) {
  const [turns, setTurns] = useState<Turn[]>([]);
  const [input, setInput] = useState("");
  const [streaming, setStreaming] = useState(false);
  const [question, setQuestion] = useState("");
  const abortRef = useRef<AbortController | null>(null);
  const bottomRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    setTurns([]);
  }, [repoId, commit]);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [turns, streaming, question]);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    const q = input.trim();
    if (!q || streaming) return;
    setInput("");
    setQuestion(q);
    setStreaming(true);

    const turn: Turn = { question: q, answer: "", warnings: [], citations: [] };
    setTurns((prev) => [...prev, turn]);
    const abort = new AbortController();
    abortRef.current = abort;

    try {
      await streamChat(
        repoId,
        q,
        (ev: ChatStreamEvent) => {
          setTurns((prev) => {
            const next = [...prev];
            const last = { ...next[next.length - 1] };
            if (ev.kind === "mode") last.mode = ev.mode;
            else if (ev.kind === "warnings") last.warnings = ev.warnings ?? [];
            else if (ev.kind === "delta") last.answer += ev.text ?? "";
            else if (ev.kind === "citations") last.citations = ev.citations ?? [];
            else if (ev.kind === "error") last.error = ev.error;
            next[next.length - 1] = last;
            return next;
          });
        },
        abort.signal,
      );
    } catch (err) {
      if ((err as Error).name === "AbortError") {
        setTurns((prev) => {
          const next = [...prev];
          const last = { ...next[next.length - 1] };
          last.error = "Stopped.";
          next[next.length - 1] = last;
          return next;
        });
      } else {
        setTurns((prev) => {
          const next = [...prev];
          const last = { ...next[next.length - 1] };
          last.error = (err as Error).message;
          next[next.length - 1] = last;
          return next;
        });
      }
    } finally {
      setStreaming(false);
      setQuestion("");
      abortRef.current = null;
    }
  };

  const stop = () => abortRef.current?.abort();

  return (
    <div className="panel" style={{ display: "flex", flexDirection: "column", minHeight: 560, maxHeight: "calc(100vh - 240px)" }}>
      <div style={{ flex: 1, overflow: "auto", paddingRight: 4 }}>
        {turns.length === 0 && (
          <div className="muted" style={{ marginTop: 20, textAlign: "center" }}>
            Ask anything about the architecture. Try{" "}
            <em>&quot;what breaks if I change Order?&quot;</em>
          </div>
        )}
        {turns.map((t, i) => (
          <div key={i} style={{ marginBottom: 18 }}>
              <div style={{ display: "flex", justifyContent: "flex-end" }}>
                <div className="rounded-[10px_10px_2px_10px] border border-border bg-inset px-3 py-2" style={{ maxWidth: "85%" }}>
                  {t.question}
                </div>
              </div>
              <div className="mt-2 rounded-[0_10px_10px_10px] border border-border bg-bg px-3.5 py-3">
                {t.mode && (
                  <div style={{ marginBottom: 8, display: "flex", gap: 6, alignItems: "center" }}>
                    <span className={`badge ${t.mode === "NoContext" ? "badge-red" : t.mode === "Graph" ? "badge-green" : "badge-purple"}`}>
                      {t.mode}
                    </span>
                    {streaming && i === turns.length - 1 && <span className="spinner" />}
                  </div>
                )}
              <div className="markdown">
                {t.answer === "" && streaming && i === turns.length - 1 ? (
                  <span className="muted">thinking…</span>
                ) : (
                  <ReactMarkdown remarkPlugins={[remarkGfm]}>{t.answer}</ReactMarkdown>
                )}
              </div>
              {t.citations.length > 0 && (
                <div style={{ marginTop: 10 }}>
                  <div className="muted" style={{ fontSize: 12, marginBottom: 4 }}>Citations</div>
                  {t.citations.map((c) => (
                    <button key={`${c.key}-${c.line}`} className="citation-chip" onClick={() => onSelect(c.key)} title={c.key}>
                      {c.label}
                    </button>
                  ))}
                </div>
              )}
              {t.warnings.length > 0 && (
                <div style={{ marginTop: 10 }}>
                  {t.warnings.map((w, j) => (
                    <div key={j} className="badge badge-yellow" style={{ margin: 2 }}>{w}</div>
                  ))}
                </div>
              )}
              {t.error && <div className="mt-2 text-danger">{t.error}</div>}
            </div>
          </div>
        ))}
        <div ref={bottomRef} />
      </div>

      <form onSubmit={submit} style={{ display: "flex", gap: 8, marginTop: 12 }}>
        <input
          value={input}
          onChange={(e) => setInput(e.target.value)}
          placeholder={streaming ? "Streaming…" : "Ask about the architecture…"}
          style={{ flex: 1 }}
          disabled={streaming}
        />
        {streaming ? (
          <button type="button" className="btn btn-danger" onClick={stop}>Stop</button>
        ) : (
          <button type="submit" className="btn btn-primary" disabled={!input.trim()}>Ask</button>
        )}
      </form>
    </div>
  );
}
