"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import Mermaid from "@/components/Mermaid";
import Markdown from "@/components/Markdown";
import { apiGet } from "@/lib/api";
import type { ExplainResult } from "@/lib/types";
import { badge, badgePurple, btn, btnSmall, card, path, spinner } from "@/lib/ui";

const STEPS = ["Summary", "Critical components", "Explore"] as const;

export default function ExplainerView({
  repoId,
  commit,
  onSelect,
}: {
  repoId: string;
  commit: string | null;
  onSelect: (key: string) => void;
}) {
  const [result, setResult] = useState<ExplainResult | null>(null);
  const [step, setStep] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    setLoading(true);
    setError(null);
    const commitParam = commit ? `?commit=${encodeURIComponent(commit)}` : "";
    apiGet<ExplainResult>(`/api/repositories/${repoId}/explain${commitParam}`)
      .then(setResult)
      .catch((e) => setError((e as Error).message))
      .finally(() => setLoading(false));
  }, [repoId, commit]);

  return (
    <div className={card}>
      <div className="mb-4">
        <h2 className="text-lg font-bold">Explain this system</h2>
        <p className="text-sm text-dim">
          A guided walkthrough of the analyzed snapshot — every claim links to the source.
        </p>
      </div>

      {loading && (
        <div className="flex items-center justify-center gap-2 py-10 text-dim">
          <span className={spinner} /> Loading…
        </div>
      )}

      {error && <div className="py-10 text-center text-danger">{error}</div>}

      {result && !result.hasSnapshot && (
        <div className="py-10 text-center">
          <div className="mb-2 font-medium text-fg">
            {result.emptyReason ?? "No analysis available yet."}
          </div>
          <p className="mb-4 text-sm text-dim">Run an analysis to generate a system explanation.</p>
          <Link href={`/repos/${repoId}/progress`} className={`${btn} ${btnSmall}`}>
            Track analysis →
          </Link>
        </div>
      )}

      {result?.hasSnapshot && (
        <div>
          <div className="mb-4 flex gap-1 rounded-lg border border-border bg-panel p-1">
            {STEPS.map((label, i) => (
              <button
                key={label}
                type="button"
                onClick={() => setStep(i)}
                className={`flex-1 cursor-pointer rounded-md px-3 py-2 text-sm font-medium transition-colors ${
                  step === i
                    ? "bg-inset text-fg"
                    : "text-dim hover:text-fg"
                }`}
              >
                {i + 1}. {label}
              </button>
            ))}
          </div>

          {result.commitSha && (
            <div className="mb-3 flex flex-wrap gap-2">
              <span className={badge}>{result.nodeCount} nodes</span>
              <span className={`${badge} ${badgePurple}`}>model {result.model}</span>
              <span className={badge}>{new Date(result.generatedAt).toLocaleString()}</span>
              <span className={badge}>commit {result.commitSha.slice(0, 10)}</span>
            </div>
          )}

          {step === 0 && <SummaryStep result={result} />}
          {step === 1 && <CriticalStep result={result} onSelect={onSelect} />}
          {step === 2 && <ExploreStep result={result} onSelect={onSelect} />}
        </div>
      )}
    </div>
  );
}

function SummaryStep({ result }: { result: ExplainResult }) {
  return (
    <div>
      {result.summary ? (
        <div className="markdown">
          <Markdown>{result.summary}</Markdown>
        </div>
      ) : (
        <div className="text-dim">No summary available for this snapshot.</div>
      )}

      {result.diagrams && result.diagrams.length > 0 && (
        <div className="mt-4 border-t border-border pt-4">
          <div className="mb-1 font-semibold text-dim">Component diagrams</div>
          <div className="space-y-3">
            {result.diagrams.map((chart, i) => (
              <Mermaid key={i} chart={chart} />
            ))}
          </div>
        </div>
      )}
    </div>
  );
}

function CriticalStep({ result, onSelect }: { result: ExplainResult; onSelect: (key: string) => void }) {
  return (
    <div>
      <div className="mb-2 text-sm text-dim">
        Top components by graph centrality (in + out degree) — the highest-coupled nodes in the system.
      </div>
      {result.criticalComponents.length === 0 ? (
        <div className="text-dim">No graph edges yet, so no critical components could be ranked.</div>
      ) : (
        <ol className="space-y-1.5">
          {result.criticalComponents.map((c, i) => (
            <li key={c.key} className="flex min-w-0 items-center gap-2 text-sm">
              <span className="w-5 shrink-0 text-right text-dim">{i + 1}.</span>
              <button
                type="button"
                className="cursor-pointer font-medium text-accent hover:underline"
                onClick={() => onSelect(c.key)}
              >
                {c.symbol}
              </button>
              <span className={`${path} truncate`}>
                {c.path}:{c.line}
              </span>
              <span className={badge}>centrality {c.centrality}</span>
            </li>
          ))}
        </ol>
      )}
    </div>
  );
}

function ExploreStep({ result, onSelect }: { result: ExplainResult; onSelect: (key: string) => void }) {
  const [showOverview, setShowOverview] = useState(false);
  return (
    <div className="space-y-5">
      {result.rawOverview && (
        <div>
          <button
            type="button"
            className={`${btn} ${btnSmall}`}
            onClick={() => setShowOverview((v) => !v)}
          >
            {showOverview ? "Hide" : "View"} full overview
          </button>
          {showOverview && (
            <div className="markdown mt-3">
              <Markdown>{result.rawOverview}</Markdown>
            </div>
          )}
        </div>
      )}

      <div>
        <div className="mb-1.5 font-semibold text-dim">Main components ({result.mainComponents.length})</div>
        {result.mainComponents.length === 0 ? (
          <div className="text-dim">No resolvable components in the overview.</div>
        ) : (
          <ul className="space-y-2">
            {result.mainComponents.map((c) => (
              <li key={c.key} className="rounded-md border border-border bg-inset px-3 py-2">
                <div className="flex flex-wrap items-center gap-2">
                  <button
                    type="button"
                    className="cursor-pointer font-medium text-accent hover:underline"
                    onClick={() => onSelect(c.key)}
                  >
                    {c.symbol}
                  </button>
                  <span className={path}>
                    {c.path}:{c.line}
                  </span>
                  <span className={badge}>{c.kind}</span>
                </div>
                <div className="mt-1 text-sm text-dim">{c.role}</div>
              </li>
            ))}
          </ul>
        )}
      </div>

      <div>
        <div className="mb-1.5 font-semibold text-dim">Architectural notes</div>
        {result.architecturalNotes.length === 0 ? (
          <div className="text-dim">None available.</div>
        ) : (
          <ul className="list-disc space-y-0.5 pl-5 text-sm">
            {result.architecturalNotes.map((n, i) => (
              <li key={i}>{n}</li>
            ))}
          </ul>
        )}
      </div>

      <div>
        <div className="mb-1.5 font-semibold text-dim">External systems</div>
        {result.externalSystems.length === 0 ? (
          <div className="text-dim">None identified (best-effort).</div>
        ) : (
          <ul className="list-disc space-y-0.5 pl-5 text-sm">
            {result.externalSystems.map((s, i) => (
              <li key={i}>{s}</li>
            ))}
          </ul>
        )}
      </div>
    </div>
  );
}
