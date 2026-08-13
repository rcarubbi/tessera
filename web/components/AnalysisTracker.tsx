"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { TopBar } from "@/components/TopBar";
import StatusBadge from "@/components/StatusBadge";
import ReprocessControls from "@/components/ReprocessControls";
import { useAuth } from "@/components/AuthContext";
import { apiGet, apiPost, ApiError } from "@/lib/api";
import type { Repository } from "@/lib/types";
import { btn, btnDanger, btnSmall, card, cardError, spinner, statCard, statLabel, statValue } from "@/lib/ui";

const STAGES = [
  { status: 0, label: "Pending" },
  { status: 1, label: "Cloning" },
  { status: 2, label: "Parsing" },
  { status: 3, label: "Analyzing" },
  { status: 4, label: "Indexing" },
  { status: 5, label: "Completed" },
];

const POLL_MS = 3000;

export default function AnalysisTracker({ repoId }: { repoId: string }) {
  const { user, hydrated, logout } = useAuth();
  const router = useRouter();
  const [repo, setRepo] = useState<Repository | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [paused, setPaused] = useState(false);
  const [cancelling, setCancelling] = useState(false);
  const [cancelError, setCancelError] = useState<string | null>(null);
  const [now, setNow] = useState(() => Date.now());
  const [frozenAt, setFrozenAt] = useState<number | null>(null);

  const load = useCallback(() => {
    setError(null);
    apiGet<Repository>(`/api/repositories/${repoId}`)
      .then(setRepo)
      .catch((e) => {
        if (e instanceof ApiError && e.status === 401) {
          logout();
          router.replace("/login");
          return;
        }
        setError(e.message);
      });
  }, [repoId, logout, router]);

  useEffect(() => {
    if (!hydrated) return;
    if (!user) {
      router.replace("/login");
      return;
    }
    load();
  }, [user, hydrated, router, load]);

  const terminal = repo !== null && (repo.status === 5 || repo.status === 6 || repo.status === 7);
  const isFailed = repo !== null && repo.status === 6;
  const isCancelled = repo !== null && repo.status === 7;
  const inProgress = repo !== null && repo.status >= 1 && repo.status <= 4;

  useEffect(() => {
    if (terminal && frozenAt === null) {
      setFrozenAt(Date.now());
    } else if (!terminal && frozenAt !== null) {
      setFrozenAt(null);
    }
  }, [terminal, frozenAt]);

  const cancel = async () => {
    setCancelling(true);
    setCancelError(null);
    try {
      await apiPost(`/api/repositories/${repoId}/cancel`);
      await load();
    } catch (e) {
      setCancelError(e instanceof ApiError ? e.message : (e as Error).message);
    } finally {
      setCancelling(false);
    }
  };

  useEffect(() => {
    if (paused || terminal) return;
    const timer = setInterval(load, POLL_MS);
    return () => clearInterval(timer);
  }, [load, paused, terminal]);

  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(timer);
  }, []);

  const analysisStartedAt = repo?.analysisStartedAt ? new Date(repo.analysisStartedAt) : null;
  const completedAt = repo?.completedAt ? new Date(repo.completedAt) : null;
  const completedMs =
    repo?.completedAt && analysisStartedAt && completedAt && !isNaN(completedAt.getTime())
      ? completedAt.getTime() - analysisStartedAt.getTime()
      : null;
  const elapsedMs =
    repo && analysisStartedAt && !isNaN(analysisStartedAt.getTime())
      ? (terminal ? (completedMs ?? frozenAt ?? now) : now) - analysisStartedAt.getTime()
      : null;
  const timeLabel = repo?.status === 5 && completedMs !== null ? "Total processing time" : "Processing time";
  const showProgress =
    repo !== null &&
    repo.totalCount > 0 &&
    (repo.status === 2 || repo.status === 3);
  const percent =
    repo && repo.totalCount > 0 ? Math.min(100, Math.round((repo.processedCount / repo.totalCount) * 100)) : 0;

  return (
    <div className="app-shell">
      <TopBar />
      <main className="app-main">
        <div className="mx-auto max-w-[760px] px-5 py-6">
          <div className="mb-6 flex flex-wrap items-center justify-between gap-3">
            <div className="min-w-0">
              <div className="mb-1 flex items-center gap-3">
                <h1 className="text-2xl font-bold">{repo?.fullName ?? "…"}</h1>
                {repo && <StatusBadge status={repo.status} />}
              </div>
              <div className="text-sm text-dim">
                Analysis progress · branch {repo?.defaultBranch ?? "…"}
              </div>
            </div>
            <div className="flex items-center gap-2">
              <Link href={`/repos/${repoId}`} className="text-sm text-accent hover:underline">
                Open repository →
              </Link>
            </div>
          </div>

          {error && <div className={`${card} ${cardError} mb-4 text-danger`}>{error}</div>}
          {!repo && !error && <div className="text-dim">Loading analysis progress…</div>}

          {repo && (
            <>
              <div className={`${card} mb-4 flex flex-col gap-4`}>
                <div className="flex items-center justify-between gap-3">
                  <div className="text-sm font-medium">Pipeline stages</div>
                  <div className="flex items-center gap-2">
                    {inProgress && (
                      <button
                        type="button"
                        className={`${btn} ${btnSmall} ${btnDanger}`}
                        onClick={cancel}
                        disabled={cancelling || repo?.cancelRequested}
                        title={repo?.cancelRequested ? "Cancellation requested — waiting for the worker to stop" : "Stop this analysis"}
                      >
                        {cancelling || repo?.cancelRequested ? <span className={spinner} /> : null}
                        {cancelling || repo?.cancelRequested ? "Cancelling…" : "Cancel"}
                      </button>
                    )}
                    <button type="button" className={`${btn} ${btnSmall}`} onClick={() => setPaused((p) => !p)}>
                      {paused ? "Resume auto-refresh" : "Pause auto-refresh"}
                    </button>
                    <button type="button" className={`${btn} ${btnSmall}`} onClick={load}>
                      Refresh
                    </button>
                  </div>
                </div>

                {cancelError && <div className="text-xs text-danger">Cancel failed: {cancelError}</div>}

                <div className="flex flex-wrap items-center gap-1">
                  {STAGES.map((s, i) => {
                    const reached = repo.status >= s.status;
                    const isCurrent = repo.status === s.status;
                    return (
                      <div key={s.status} className="flex items-center gap-1">
                        <span
                          className={`rounded-md px-2 py-1 text-xs font-medium ${
                            isCurrent
                              ? "bg-accent/15 text-accent"
                              : reached
                                ? "bg-inset text-fg"
                                : "bg-inset text-dim"
                          }`}
                        >
                          {i + 1}. {s.label}
                        </span>
                        {i < STAGES.length - 1 && <span className="text-dim">→</span>}
                      </div>
                    );
                  })}
                  {isFailed && (
                    <span className="rounded-md bg-danger/10 px-2 py-1 text-xs font-medium text-danger">
                      Failed
                    </span>
                  )}
                  {isCancelled && (
                    <span className="rounded-md bg-inset px-2 py-1 text-xs font-medium text-dim">
                      Cancelled
                    </span>
                  )}
                </div>

                <div className="border-t border-border pt-4">
                  {showProgress ? (
                    <>
                      <div className="mb-1 flex items-center justify-between text-sm">
                        <span className="text-dim">
                          {STAGES[repo.status]?.label} · {repo.processedCount} / {repo.totalCount} items
                        </span>
                        <span className="font-medium">{percent}%</span>
                      </div>
                      <div className="h-2 w-full overflow-hidden rounded-full bg-inset">
                        <div
                          className="h-full rounded-full bg-accent transition-all duration-700"
                          style={{ width: `${percent}%` }}
                        />
                      </div>
                    </>
                  ) : (
                    <div className="flex items-center gap-2 text-sm text-dim">
                      {repo.status === 5 || repo.status === 6 || repo.status === 7 ? (
                        <span className={repo.status === 7 ? "text-dim" : "text-good"}>
                          {repo.status === 5
                            ? "Analysis completed."
                            : repo.status === 6
                              ? "Analysis failed."
                              : "Analysis cancelled."}
                        </span>
                      ) : (
                        <>
                          <span className={spinner} />
                          Working on <strong className="text-fg">{STAGES[repo.status]?.label}</strong>
                        </>
                      )}
                    </div>
                  )}
                </div>
              </div>

              <div className="mb-4">
                <ReprocessControls repoId={repoId} fullName={repo.fullName} disabled={inProgress} onReprocessed={load} />
              </div>

              <div className="grid grid-cols-2 gap-4 sm:grid-cols-3">
                <div className={statCard}>
                  <span className={statValue}>{fmtDuration(elapsedMs)}</span>
                  <span className={statLabel}>{timeLabel}</span>
                </div>
                <div className={statCard}>
                  <span className={statValue}>
                    {repo.nodeCount} / {repo.edgeCount}
                  </span>
                  <span className={statLabel}>Nodes / edges in last snapshot</span>
                </div>
                <div className={statCard}>
                  <span className={statValue}>{repo.lastProcessedCommit ? short(repo.lastProcessedCommit) : "—"}</span>
                  <span className={statLabel}>Last processed commit</span>
                </div>
              </div>

              {repo.status === 6 && (
                <div className={`${card} ${cardError} mt-4 flex flex-col gap-3`}>
                  <div className="text-sm font-medium text-danger">Analysis failed</div>
                  {repo.errorMessage && (
                    <pre className="max-h-48 overflow-auto rounded-md bg-inset p-3 text-xs text-dim whitespace-pre-wrap">
                      {repo.errorMessage}
                    </pre>
                  )}
                </div>
              )}

              <div className="mt-4 text-center text-xs text-dim">
                Last update {new Date(repo.updatedAt).toLocaleString()} · auto-refreshes every{" "}
                {POLL_MS / 1000}s{paused ? " (paused)" : ""}
              </div>
            </>
          )}
        </div>
      </main>
    </div>
  );
}

function fmtDuration(ms: number | null) {
  if (ms === null || ms < 0) return "—";
  const total = Math.floor(ms / 1000);
  const h = Math.floor(total / 3600);
  const m = Math.floor((total % 3600) / 60);
  const s = total % 60;
  if (h > 0) return `${h}h ${m}m ${s}s`;
  if (m > 0) return `${m}m ${s}s`;
  return `${s}s`;
}

function short(sha: string) {
  return sha.length > 10 ? sha.slice(0, 10) : sha;
}
