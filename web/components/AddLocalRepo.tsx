"use client";

import { useState } from "react";
import { apiPost, ApiError } from "@/lib/api";
import { btn, btnPrimary, field, spinner } from "@/lib/ui";

type Props = {
  onAdded: () => void;
};

export default function AddLocalRepo({ onAdded }: Props) {
  const [open, setOpen] = useState(false);
  const [name, setName] = useState("");
  const [path, setPath] = useState("");
  const [defaultBranch, setDefaultBranch] = useState("main");
  const [submitting, setSubmitting] = useState(false);
  const [message, setMessage] = useState<{ tone: "good" | "danger"; text: string } | null>(null);

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
      setMessage({ tone: "good", text: "Local repository added. Run Analyze to start." });
      setName("");
      setPath("");
      setDefaultBranch("main");
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
              The repo stays inactive until you run Analyze.
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
