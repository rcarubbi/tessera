"use client";

import { useEffect, useRef, useState } from "react";
import Markdown from "@/components/Markdown";
import { getChatMessages, postChatMessage, streamChat, type ChatStreamEvent, type Citation } from "@/lib/api";
import { badge, badgeGreen, badgePurple, badgeRed, badgeYellow, btn, btnDanger, btnPrimary, card, field, spinner } from "@/lib/ui";

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
  const lastRef = useRef<Turn | null>(null);

  useEffect(() => {
    let cancelled = false;
    getChatMessages(repoId)
      .then((msgs) => {
        if (cancelled) return;
        const loaded: Turn[] = [];
        let current: Turn | null = null;
        for (const m of msgs) {
          if (m.role === "user") {
            current = { question: m.content, answer: "", warnings: m.warnings ?? [], citations: m.citations ?? [] };
            loaded.push(current);
          } else if (m.role === "assistant" && current) {
            current.answer = m.content;
            current.mode = m.mode ?? undefined;
            current.warnings = m.warnings ?? [];
            current.citations = m.citations ?? [];
          }
        }
        setTurns(loaded);
      })
      .catch(() => {});
    return () => {
      cancelled = true;
    };
  }, [repoId]);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [turns, streaming, question]);

  const updateLast = (fn: (t: Turn) => Turn) => {
    if (lastRef.current) lastRef.current = fn(lastRef.current);
    setTurns((prev) => {
      const next = [...prev];
      const last = fn({ ...next[next.length - 1] });
      next[next.length - 1] = last;
      return next;
    });
  };

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    const q = input.trim();
    if (!q || streaming) return;
    setInput("");
    setQuestion(q);
    setStreaming(true);

    const turn: Turn = { question: q, answer: "", warnings: [], citations: [] };
    lastRef.current = turn;
    setTurns((prev) => [...prev, turn]);
    const abort = new AbortController();
    abortRef.current = abort;

    try {
      await streamChat(
        repoId,
        q,
        (ev: ChatStreamEvent) => {
          if (ev.kind === "mode") updateLast((t) => ({ ...t, mode: ev.mode }));
          else if (ev.kind === "warnings") updateLast((t) => ({ ...t, warnings: ev.warnings ?? [] }));
          else if (ev.kind === "delta") updateLast((t) => ({ ...t, answer: t.answer + (ev.text ?? "") }));
          else if (ev.kind === "citations") updateLast((t) => ({ ...t, citations: ev.citations ?? [] }));
          else if (ev.kind === "error") updateLast((t) => ({ ...t, error: ev.error }));
        },
        abort.signal,
      );
    } catch (err) {
      if ((err as Error).name === "AbortError") {
        updateLast((t) => ({ ...t, error: "Stopped." }));
      } else {
        updateLast((t) => ({ ...t, error: (err as Error).message }));
      }
    } finally {
      const done = lastRef.current;
      if (done && !done.error && done.answer) {
        postChatMessage(repoId, { role: "user", content: done.question }).catch(() => {});
        postChatMessage(repoId, {
          role: "assistant",
          content: done.answer,
          mode: done.mode,
          citations: done.citations,
          warnings: done.warnings,
        }).catch(() => {});
      }
      lastRef.current = null;
      setStreaming(false);
      setQuestion("");
      abortRef.current = null;
    }
  };

  const stop = () => abortRef.current?.abort();

  return (
    <div className={`${card} flex flex-col`} style={{ minHeight: 560, maxHeight: "calc(100vh - 240px)" }}>
      <div className="flex-1 overflow-auto pr-1">
        {turns.length === 0 && (
          <div className="mt-5 text-center text-dim">
            Ask anything about the architecture. Try{" "}
            <em>&quot;what breaks if I change Order?&quot;</em>
          </div>
        )}
        {turns.map((t, i) => (
          <div key={i} className="mb-4">
            <div className="flex justify-end">
              <div className="rounded-[10px_10px_2px_10px] border border-border bg-inset px-3 py-2 text-sm" style={{ maxWidth: "85%" }}>
                {t.question}
              </div>
            </div>
            <div className="mt-2 rounded-[0_10px_10px_10px] border border-border bg-bg px-3.5 py-3">
              {t.mode && (
                <div className="mb-2 flex items-center gap-1.5">
                  <span className={`${badge} ${t.mode === "NoContext" ? badgeRed : t.mode === "Graph" ? badgeGreen : badgePurple}`}>
                    {t.mode}
                  </span>
                  {streaming && i === turns.length - 1 && <span className={spinner} />}
                </div>
              )}
              <div className="markdown">
                {t.answer === "" && streaming && i === turns.length - 1 ? (
                  <span className="text-dim">thinking…</span>
                ) : (
                  <Markdown>{t.answer}</Markdown>
                )}
              </div>
              {t.mode === "NoContext" && (
                <div className="mt-2 text-dim">
                  No relevant context was found in this snapshot. Try naming a file or entity, e.g.{" "}
                  <em>&quot;what breaks if I change Order?&quot;</em>.
                </div>
              )}
              {t.citations.length > 0 && (
                <div className="mt-2.5">
                  <div className="mb-1 text-xs text-dim">Citations</div>
                  {t.citations.map((c) => (
                    <button
                      key={`${c.key}-${c.line}`}
                      className="mr-1 mb-1 inline-block cursor-pointer rounded-full border border-border bg-inset px-2 py-0.5 font-mono text-xs text-accent hover:border-accent"
                      onClick={() => onSelect(c.key)}
                      title={c.key}
                    >
                      {c.label}
                    </button>
                  ))}
                </div>
              )}
              {t.warnings.length > 0 && (
                <div className="mt-2.5">
                  {t.warnings.map((w, j) => (
                    <span key={j} className={`${badge} ${badgeYellow} mr-1 mb-1`}>{w}</span>
                  ))}
                </div>
              )}
              {t.error && <div className="mt-2 text-danger">{t.error}</div>}
            </div>
          </div>
        ))}
        <div ref={bottomRef} />
      </div>

      <form onSubmit={submit} className="mt-3 flex gap-2">
        <input
          value={input}
          onChange={(e) => setInput(e.target.value)}
          placeholder={streaming ? "Streaming…" : "Ask about the architecture…"}
          className={`${field} flex-1`}
          disabled={streaming}
        />
        {streaming ? (
          <button type="button" className={`${btn} ${btnDanger}`} onClick={stop}>Stop</button>
        ) : (
          <button type="submit" className={`${btn} ${btnPrimary}`} disabled={!input.trim()}>Ask</button>
        )}
      </form>
    </div>
  );
}
