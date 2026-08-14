"use client";

import { useState } from "react";
import { apiPost, ApiError } from "@/lib/api";
import { btn, btnPrimary, btnSmall, spinner } from "@/lib/ui";

type Props = {
  repoId: string;
  fullName: string;
  disabled: boolean;
  onReprocessed: () => void;
};

export default function ReprocessControls({ repoId, fullName, disabled, onReprocessed }: Props) {
  const [showOptions, setShowOptions] = useState(false);
  const [includeStatic, setIncludeStatic] = useState(false);
  const [includeAi, setIncludeAi] = useState(false);
  const [includeIndexing, setIncludeIndexing] = useState(true);
  const [submitting, setSubmitting] = useState<"full" | "incremental" | null>(null);
  const [message, setMessage] = useState<{ tone: "good" | "danger"; text: string } | null>(null);

  const start = async (mode: "full" | "incremental") => {
    setSubmitting(mode);
    setMessage(null);
    try {
      await apiPost(`/api/repositories/${repoId}/reprocess`, {
        mode,
        includeStatic,
        includeAi,
        includeIndexing,
      });
      setMessage({ tone: "good", text: mode === "full" ? "Full reprocess queued." : "Incremental reprocess queued." });
      onReprocessed();
    } catch (e) {
      setMessage({ tone: "danger", text: e instanceof ApiError ? e.message : (e as Error).message });
    } finally {
      setSubmitting(null);
    }
  };

  return (
    <div className="flex flex-col gap-4 rounded-lg border border-border bg-panel p-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex flex-col gap-0.5">
          <div className="text-sm font-medium text-fg">Reprocess</div>
          <div className="text-xs text-dim">Re-run analysis of {fullName}</div>
        </div>
        <div className="text-sm font-medium">Reprocess</div>
        <div className="flex flex-wrap items-center gap-2">
          <button
            type="button"
            className={`${btn} ${btnSmall}`}
            onClick={() => start("full")}
            disabled={disabled || submitting !== null}
            title={`Re-analyze every node of ${fullName} from scratch`}
          >
            {submitting === "full" && <span className={spinner} />}
            Reprocess all
          </button>
          <button
            type="button"
            className={`${btn} ${btnSmall}`}
            onClick={() => setShowOptions((v) => !v)}
            disabled={disabled || submitting !== null}
            aria-expanded={showOptions}
          >
            Reprocess missing
          </button>
        </div>
      </div>

      {showOptions && (
        <div className="flex flex-col gap-4 border-t border-border pt-4">
          <div className="flex flex-wrap items-center gap-x-6 gap-y-3">
            <label className="flex cursor-pointer items-center gap-2 text-[13px] select-none">
              <span className="relative inline-flex">
                <input
                  type="checkbox"
                  className="peer sr-only"
                  checked={includeStatic}
                  onChange={(e) => setIncludeStatic(e.target.checked)}
                />
                <span className="h-4 w-7 rounded-full bg-inset ring-1 ring-border transition-colors peer-checked:bg-accent" />
                <span className="absolute left-0.5 top-0.5 h-3 w-3 rounded-full bg-fg transition-transform peer-checked:translate-x-3" />
              </span>
              <span className="text-dim">Include static analysis</span>
            </label>
            <label className="flex cursor-pointer items-center gap-2 text-[13px] select-none">
              <span className="relative inline-flex">
                <input
                  type="checkbox"
                  className="peer sr-only"
                  checked={includeAi}
                  onChange={(e) => setIncludeAi(e.target.checked)}
                />
                <span className="h-4 w-7 rounded-full bg-inset ring-1 ring-border transition-colors peer-checked:bg-accent" />
                <span className="absolute left-0.5 top-0.5 h-3 w-3 rounded-full bg-fg transition-transform peer-checked:translate-x-3" />
              </span>
              <span className="text-dim">Include AI analysis</span>
            </label>
            <label className="flex cursor-pointer items-center gap-2 text-[13px] select-none">
              <span className="relative inline-flex">
                <input
                  type="checkbox"
                  className="peer sr-only"
                  checked={includeIndexing}
                  onChange={(e) => setIncludeIndexing(e.target.checked)}
                />
                <span className="h-4 w-7 rounded-full bg-inset ring-1 ring-border transition-colors peer-checked:bg-accent" />
                <span className="absolute left-0.5 top-0.5 h-3 w-3 rounded-full bg-fg transition-transform peer-checked:translate-x-3" />
              </span>
              <span className="text-dim">Include indexing</span>
            </label>
          </div>
          <div className="flex flex-wrap items-center gap-3">
            <button
              type="button"
              className={`${btn} ${btnSmall} ${btnPrimary}`}
              onClick={() => start("incremental")}
              disabled={disabled || submitting !== null || (!includeStatic && !includeAi && !includeIndexing)}
              title="Re-analyze only nodes missing the selected analysis"
            >
              {submitting === "incremental" && <span className={spinner} />}
              Start incremental reprocess
            </button>
            {!includeStatic && !includeAi && !includeIndexing && (
              <span className="text-xs text-dim">Select at least one option</span>
            )}
          </div>
        </div>
      )}

      {message && (
        <div className={`px-1 text-xs ${message.tone === "good" ? "text-good" : "text-danger"}`}>{message.text}</div>
      )}

      {disabled && <div className="px-1 text-xs text-dim">Disabled while an analysis is running.</div>}
    </div>
  );
}
