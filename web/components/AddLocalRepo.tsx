"use client";

import { useEffect, useState } from "react";
import { apiGet, apiPost, ApiError } from "@/lib/api";
import { badge, badgeGreen, btn, btnPrimary, field, spinner } from "@/lib/ui";

type AvailableRepo = { name: string; path: string; registered: boolean };
type AvailableResponse = { root: string; repos: AvailableRepo[] };

type Props = {
  onAdded: () => void;
};

export default function AddLocalRepo({ onAdded }: Props) {
  const [open, setOpen] = useState(false);
  const [available, setAvailable] = useState<AvailableResponse | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [selected, setSelected] = useState<string | null>(null);
  const [name, setName] = useState("");
  const [path, setPath] = useState("");
  const [defaultBranch, setDefaultBranch] = useState("main");
  const [submitting, setSubmitting] = useState(false);
  const [message, setMessage] = useState<{ tone: "good" | "danger"; text: string } | null>(null);

  const loadAvailable = () => {
    setAvailable(null);
    setLoadError(null);
    setSelected(null);
    apiGet<AvailableResponse>("/api/repositories/local/available")
      .then(setAvailable)
      .catch((err) => setLoadError(err instanceof ApiError ? err.message : (err as Error).message));
  };

  useEffect(() => {
    if (open) loadAvailable();
  }, [open]);

  const pick = (repo: AvailableRepo) => {
    setSelected(repo.name);
    setName(repo.name);
    setPath(repo.path);
    setMessage(null);
  };

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setSubmitting(true);
    setMessage(null);
    try {
      await apiPost("/api/repositories/local", {
        name: name.trim(),
        cloneUrl: path.trim(),
        defaultBranch: defaultBranch.trim() || "main",
      });
      setMessage({ tone: "good", text: "Local repository added. Analysis starts automatically." });
      setName("");
      setPath("");
      setDefaultBranch("main");
      setSelected(null);
      setOpen(false);
      onAdded();
    } catch (err) {
      setMessage({
        tone: "danger",
        text: err instanceof ApiError ? err.message : (err as Error).message,
      });
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="flex flex-col gap-3">
      <button type="button" className={`${btn} ${btnPrimary}`} onClick={() => setOpen((v) => !v)} aria-expanded={open}>
        {open ? "Close" : "Add local repository"}
      </button>

      {open && (
        <form onSubmit={submit} className="flex flex-col gap-4 rounded-lg border border-border bg-panel p-4">
          <div className="flex flex-col gap-0.5">
            <div className="text-sm font-medium text-fg">Add local repository</div>
            <div className="text-xs text-dim">
              Point the worker at a git repo reachable from its container. No webhook, no push trigger — analysis runs
              manually via Analyze / Reprocess.
            </div>
          </div>

          <div className="flex flex-col gap-1.5">
            <span className="text-sm text-dim">Detected repositories</span>
            {loadError !== null && (
              <div className="flex items-center gap-2">
                <span className="text-xs text-danger">{loadError}</span>
                <button type="button" className={`${btn} px-2 py-1 text-xs`} onClick={loadAvailable}>
                  Retry
                </button>
              </div>
            )}
            {loadError === null && available === null && (
              <div className="flex items-center gap-2 text-xs text-dim">
                <span className={spinner} /> Scanning…
              </div>
            )}
            {loadError === null && available !== null && available.repos.length === 0 && (
              <div className="text-xs text-dim">
                No git repositories found under <code className="font-mono">{available.root}</code>. Drop a repo folder
                into the mounted directory and retry, or type the path below.
              </div>
            )}
            {loadError === null && available !== null && available.repos.length > 0 && (
              <ul className="flex max-h-64 flex-col divide-y divide-border overflow-y-auto rounded-lg border border-border">
                {available.repos.map((repo) => (
                  <li key={repo.path}>
                    <button
                      type="button"
                      onClick={() => pick(repo)}
                      aria-pressed={selected === repo.name}
                      className={`flex w-full items-center justify-between gap-3 px-3 py-2 text-left text-sm hover:bg-border/30 ${
                        selected === repo.name ? "bg-border/40" : ""
                      }`}
                    >
                      <span className="flex min-w-0 flex-col">
                        <span className="truncate font-mono text-fg">{repo.name}</span>
                        <span className="truncate text-xs text-dim">{repo.path}</span>
                      </span>
                      {repo.registered && <span className={`${badge} ${badgeGreen} shrink-0`}>added</span>}
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </div>

          <label className="flex flex-col gap-1.5 text-sm">
            <span className="text-dim">Name</span>
            <input
              className={field}
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="my-app"
              required
              pattern="[A-Za-z0-9._-]{1,100}"
              title="Letters, digits, dots, dashes and underscores (used as the clone folder)."
            />
          </label>

          <label className="flex flex-col gap-1.5 text-sm">
            <span className="text-dim">Path inside the worker</span>
            <input
              className={`${field} font-mono`}
              value={path}
              onChange={(e) => setPath(e.target.value)}
              placeholder="/repos/local/my-app"
              required
              title="Absolute path to a git repository visible inside the worker container."
            />
          </label>

          <label className="flex flex-col gap-1.5 text-sm">
            <span className="text-dim">Default branch</span>
            <input
              className={field}
              value={defaultBranch}
              onChange={(e) => setDefaultBranch(e.target.value)}
              placeholder="main"
            />
          </label>

          <div className="flex flex-wrap items-center gap-3">
            <button type="submit" className={`${btn} ${btnPrimary}`} disabled={submitting}>
              {submitting && <span className={spinner} />}
              Add repository
            </button>
            <span className="text-xs text-dim">
              Analysis (clone → parse → snapshot) starts automatically.
            </span>
          </div>

          {message && (
            <div className={`px-1 text-xs ${message.tone === "good" ? "text-good" : "text-danger"}`}>
              {message.text}
            </div>
          )}
        </form>
      )}
    </div>
  );
}
