"use client";

import { useEffect, useState } from "react";
import Markdown from "@/components/Markdown";
import { apiGet } from "@/lib/api";
import { badge, badgePurple, card, spinner } from "@/lib/ui";

type Overview = {
  overview: string;
  model: string;
  nodeCount: number;
  generatedAt: string;
};

export default function OverviewPanel({ repoId }: { repoId: string }) {
  const [overview, setOverview] = useState<Overview | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    setLoading(true);
    setError(null);
    apiGet<Overview>(`/api/repositories/${repoId}/overview`)
      .then(setOverview)
      .catch((e) => {
        if (e.status === 404) {
          setError("No overview generated yet. Run a new analysis to generate it.");
        } else {
          setError((e as Error).message);
        }
      })
      .finally(() => setLoading(false));
  }, [repoId]);

  return (
    <div className={card}>
      <div className="mb-4">
        <h2 className="text-lg font-bold">Project overview</h2>
        <p className="text-sm text-dim">
          AI-generated summary of the analyzed snapshot, produced during analysis.
        </p>
      </div>

      {loading && (
        <div className="flex items-center justify-center gap-2 py-10 text-dim">
          <span className={spinner} /> Loading…
        </div>
      )}

      {error && <div className="py-10 text-center text-dim">{error}</div>}

      {overview && (
        <div>
          <div className="mb-3 flex flex-wrap gap-2">
            <span className={badge}>{overview.nodeCount} nodes</span>
            <span className={`${badge} ${badgePurple}`}>model {overview.model}</span>
            <span className={badge}>
              {new Date(overview.generatedAt).toLocaleString()}
            </span>
          </div>
          <div className="markdown">
            <Markdown>{overview.overview}</Markdown>
          </div>
        </div>
      )}
    </div>
  );
}
