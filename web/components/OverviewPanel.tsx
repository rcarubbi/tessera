"use client";

import { useEffect, useState } from "react";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { apiGet } from "@/lib/api";

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
    <div className="card">
      <div className="mb-4">
        <h2 className="text-lg font-bold">Project overview</h2>
        <p className="text-sm text-dim">
          AI-generated summary of the analyzed snapshot, produced during analysis.
        </p>
      </div>

      {loading && (
        <div className="muted py-10 text-center">
          <span className="spinner spinner-sm" /> Loading…
        </div>
      )}

      {error && <div className="muted py-10 text-center">{error}</div>}

      {overview && (
        <div>
          <div className="mb-3 flex flex-wrap gap-2">
            <span className="badge">{overview.nodeCount} nodes</span>
            <span className="badge badge-purple">model {overview.model}</span>
            <span className="badge">
              {new Date(overview.generatedAt).toLocaleString()}
            </span>
          </div>
          <div className="markdown">
            <ReactMarkdown remarkPlugins={[remarkGfm]}>{overview.overview}</ReactMarkdown>
          </div>
        </div>
      )}
    </div>
  );
}
