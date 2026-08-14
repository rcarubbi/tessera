"use client";

import { useCallback, useEffect, useState } from "react";
import { apiGet, apiPut, ApiError } from "@/lib/api";
import { badge, badgeGreen, badgeOrange, badgePurple, badgeRed, badgeYellow, card, spinner } from "@/lib/ui";
import type { PrReview, PrReviewStatus, Repository } from "@/lib/types";

const statusBadge: Record<PrReviewStatus, string> = {
  Queued: badgeYellow,
  Reviewed: badgePurple,
  Posted: badgeGreen,
  Failed: badgeRed,
};

type Props = {
  repo: Repository | null;
  onRepoUpdated: () => void;
};

export default function PrPanel({ repo, onRepoUpdated }: Props) {
  const [reviews, setReviews] = useState<PrReview[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  const load = useCallback(() => {
    setLoading(true);
    setError(null);
    apiGet<{ items: PrReview[] }>(`/api/repositories/${repo?.id}/pr-reviews`)
      .then((result) => setReviews(result.items))
      .catch((e) => setError((e as Error).message))
      .finally(() => setLoading(false));
  }, [repo?.id]);

  useEffect(() => {
    if (repo?.id) load();
  }, [repo?.id, load]);

  const toggle = async (enabled: boolean) => {
    if (!repo) return;
    setSaving(true);
    setError(null);
    try {
      await apiPut<Repository>(`/api/repositories/${repo.id}/settings`, { enablePrComments: enabled });
      onRepoUpdated();
    } catch (e) {
      setError(e instanceof ApiError ? e.message : (e as Error).message);
    } finally {
      setSaving(false);
    }
  };

  const prUrl = (prNumber: number) =>
    repo ? `https://github.com/${repo.fullName}/pull/${prNumber}` : "#";

  return (
    <div className="flex flex-col gap-4">
      <div className={`${card} flex flex-wrap items-center justify-between gap-3`}>
        <div className="flex flex-col gap-0.5">
          <div className="text-sm font-medium text-fg">PR comments</div>
          <div className="text-xs text-dim">
            Automatically post an impact + dependency + rules analysis comment on pull requests.
          </div>
        </div>
        <label className="flex cursor-pointer items-center gap-2 text-[13px] select-none">
          <span className="relative inline-flex">
            <input
              type="checkbox"
              className="peer sr-only"
              checked={repo?.enablePrComments ?? false}
              disabled={saving || !repo}
              onChange={(e) => toggle(e.target.checked)}
            />
            <span className="h-4 w-7 rounded-full bg-inset ring-1 ring-border transition-colors peer-checked:bg-accent" />
            <span className="absolute left-0.5 top-0.5 h-3 w-3 rounded-full bg-fg transition-transform peer-checked:translate-x-3" />
          </span>
          <span className="text-dim">{repo?.enablePrComments ? "Enabled" : "Disabled"}</span>
        </label>
      </div>

      {error && <div className={`${card} border-danger bg-danger/5 text-danger`}>{error}</div>}

      <div className={`${card} flex flex-col gap-3`}>
        <div className="text-sm font-medium text-fg">Recent PR reviews</div>
        {loading ? (
          <div className="flex items-center gap-2 text-sm text-dim">
            <span className={spinner} /> Loading reviews…
          </div>
        ) : reviews.length === 0 ? (
          <div className="text-sm text-dim">No pull requests analyzed yet. Open a PR in GitHub to see reviews here.</div>
        ) : (
          <ul className="flex flex-col gap-2">
            {reviews.map((r) => (
              <li key={r.id} className="flex flex-col gap-1.5 rounded-lg border border-border bg-inset px-3 py-2.5">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <a
                    href={prUrl(r.prNumber)}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="text-sm font-medium text-accent hover:underline"
                  >
                    #{r.prNumber}
                  </a>
                  <div className="flex flex-wrap items-center gap-2">
                    <span className={`${badge} ${statusBadge[r.status]}`}>{r.status}</span>
                    <span className="font-mono text-[11px] text-dim">{r.headSha.slice(0, 8)}</span>
                    <span className="text-[11px] text-dim">→ {r.baseSha.slice(0, 8)}</span>
                  </div>
                </div>
                {r.errorMessage && <div className="text-xs text-danger">{r.errorMessage}</div>}
                {r.commentBody && (
                  <details className="group text-xs">
                    <summary className="cursor-pointer text-dim select-none group-open:mb-2">Preview comment</summary>
                    <pre className="overflow-x-auto whitespace-pre-wrap rounded-md border border-border bg-panel p-2 font-mono text-[11px] text-fg">
                      {r.commentBody}
                    </pre>
                  </details>
                )}
                <div className="text-[11px] text-dim">Updated {new Date(r.updatedAt).toLocaleString()}</div>
              </li>
            ))}
          </ul>
        )}
      </div>
    </div>
  );
}
