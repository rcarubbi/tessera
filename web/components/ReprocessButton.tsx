"use client";

import { useState } from "react";
import { apiPost, ApiError } from "@/lib/api";

type Props = {
  repoId: string;
  fullName: string;
  onReprocessed: () => void;
};

type State = "idle" | "loading" | "done" | "error";

export default function ReprocessButton({ repoId, fullName, onReprocessed }: Props) {
  const [state, setState] = useState<State>("idle");
  const [message, setMessage] = useState<string | null>(null);

  const reprocess = async () => {
    setState("loading");
    setMessage(null);
    try {
      await apiPost(`/api/repositories/${repoId}/reprocess`);
      setState("done");
      setMessage("Requeued");
      onReprocessed();
    } catch (e) {
      setState("error");
      setMessage(e instanceof ApiError ? e.message : (e as Error).message);
    }
  };

  return (
    <span className="inline-flex flex-col items-end gap-1">
      <button
        type="button"
        className="btn btn-small"
        onClick={reprocess}
        disabled={state === "loading" || state === "done"}
        title={`Re-queue ${fullName} for reprocessing`}
      >
        {state === "loading" && <span className="spinner" />}
        {state === "done" ? "✓" : "↻"} {state === "done" ? "Requeued" : "Reprocess"}
      </button>
      {state === "error" && message && <span className="text-xs text-danger">{message}</span>}
      {state === "done" && <span className="text-xs text-good">{message}</span>}
    </span>
  );
}
