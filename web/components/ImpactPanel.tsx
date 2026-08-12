"use client";

import { useEffect, useMemo, useState } from "react";
import Mermaid from "@/components/Mermaid";
import { apiGet } from "@/lib/api";
import type { ClassifiedImpactItem, ImpactRating, ImpactReport } from "@/lib/types";
import { badge, badgeGreen, badgeOrange, badgePurple, badgeRed, badgeYellow, card, path, spinner, statCard, statLabel, statValue } from "@/lib/ui";

const ratingTone: Record<ImpactRating, string> = {
  LOW: badgeGreen,
  MEDIUM: badgeYellow,
  HIGH: badgeOrange,
  CRITICAL: badgeRed,
};

const typeBadge: Record<string, string> = {
  test: badgePurple,
  "api-contract": badgeOrange,
  "database-entity": badgeRed,
  other: "",
};

type Props = {
  repoId: string;
  commit: string | null;
  entityKey: string;
  onFocus: (key: string) => void;
};

export default function ImpactPanel({ repoId, commit, entityKey, onFocus }: Props) {
  const [report, setReport] = useState<ImpactReport | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const commitParam = commit ? `&commit=${encodeURIComponent(commit)}` : "";

  useEffect(() => {
    setLoading(true);
    setError(null);
    apiGet<ImpactReport>(`/api/repositories/${repoId}/impact?entity=${encodeURIComponent(entityKey)}&maxDepth=3${commitParam}`)
      .then(setReport)
      .catch((e) => setError((e as Error).message))
      .finally(() => setLoading(false));
  }, [repoId, entityKey, commitParam]);

  const direct = useMemo(() => report?.items.filter((i) => i.depth === 1) ?? [], [report]);
  const indirect = useMemo(() => report?.items.filter((i) => i.depth > 1) ?? [], [report]);

  const chart = useMemo(() => buildChain(report), [report]);

  return (
    <div className="mt-4">
      <div className="flex items-center justify-between">
        <div className="font-semibold text-dim">Impact</div>
        {report && <span className={`${badge} ${ratingTone[report.rating]}`}>{report.rating}</span>}
      </div>

      {loading && (
        <div className="mt-2 flex items-center gap-2 text-sm text-dim">
          <span className={spinner} /> Loading impact…
        </div>
      )}
      {error && <div className="mt-2 text-sm text-danger">{error}</div>}

      {report && (
        <div className="mt-2">
          <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
            <div className={statCard}>
              <span className={statValue}>{report.totalCount}</span>
              <span className={statLabel}>affected</span>
            </div>
            <div className={statCard}>
              <span className={statValue}>{report.directCount}</span>
              <span className={statLabel}>direct</span>
            </div>
            <div className={statCard}>
              <span className={statValue}>{report.indirectCount}</span>
              <span className={statLabel}>indirect</span>
            </div>
            <div className={statCard}>
              <span className={statValue}>{report.maxDepth}</span>
              <span className={statLabel}>max depth</span>
            </div>
          </div>

          <div className="mt-2 flex flex-wrap gap-1.5">
            <span className={`${badge} ${report.byType.tests > 0 ? typeBadge.test : ""}`}>{report.byType.tests} test(s)</span>
            <span className={`${badge} ${report.byType.apiContracts > 0 ? typeBadge["api-contract"] : ""}`}>{report.byType.apiContracts} api-contract(s)</span>
            <span className={`${badge} ${report.byType.databaseEntities > 0 ? typeBadge["database-entity"] : ""}`}>{report.byType.databaseEntities} db-entity(ies)</span>
            <span className={badge}>{report.byType.other} other</span>
          </div>

          {report.totalCount === 0 ? (
            <div className="mt-3 text-sm text-dim">No dependents found — changing this entity is currently safe.</div>
          ) : (
            <>
              <div className="mt-3">
                <div className="mb-1 text-xs font-semibold text-dim">Direct dependents ({direct.length})</div>
                <AffectedList items={direct} onFocus={onFocus} />
              </div>
              <div className="mt-3">
                <div className="mb-1 text-xs font-semibold text-dim">Indirect dependents ({indirect.length})</div>
                <AffectedList items={indirect} onFocus={onFocus} />
              </div>
              {chart && (
                <div className="mt-3">
                  <div className="mb-1 text-xs font-semibold text-dim">Dependency chain</div>
                  <Mermaid chart={chart} />
                </div>
              )}
            </>
          )}
        </div>
      )}
    </div>
  );
}

function AffectedList({ items, onFocus }: { items: ClassifiedImpactItem[]; onFocus: (key: string) => void }) {
  if (items.length === 0) {
    return <div className="text-sm text-dim">none</div>;
  }
  return (
    <ul className="space-y-0.5">
      {items.map((item) => (
        <li key={item.key}>
          <button type="button" className="cursor-pointer text-accent hover:underline" onClick={() => onFocus(item.key)}>
            {item.symbol}
          </button>{" "}
          <span className={path}>{item.path}:{item.line}</span>{" "}
          <span className={`${badge} ${typeBadge[item.classification]}`}>{item.classification}</span>
          <span className="text-[11px] text-dim" title={item.reason}>· depth {item.depth}</span>
        </li>
      ))}
    </ul>
  );
}

function buildChain(report: ImpactReport | null): string | null {
  if (!report || report.items.length === 0) return null;

  const symbols = new Map<string, string>([[report.entity, report.entity]]);
  for (const item of report.items) {
    if (!symbols.has(item.key)) symbols.set(item.key, item.symbol);
  }

  const lines: string[] = ["flowchart LR"];
  const nodes = new Set<string>();
  const edges = new Set<string>();
  for (const item of report.items) {
    const trace = item.trace;
    for (let i = 0; i < trace.length; i++) {
      const key = trace[i];
      if (!nodes.has(key)) {
        nodes.add(key);
        lines.push(`  ${key}["${escapeLabel(symbols.get(key) ?? key)}"]`);
      }
      if (i > 0) {
        const edge = `${trace[i - 1]} --> ${key}`;
        if (!edges.has(edge)) {
          edges.add(edge);
          lines.push(`  ${edge}`);
        }
      }
    }
  }
  return lines.join("\n");
}

function escapeLabel(value: string): string {
  return value.replace(/"/g, "#quot;").replace(/\n/g, " ");
}
