"use client";

import { useState } from "react";
import { apiDelete, ApiError } from "@/lib/api";
import { spinner } from "@/lib/ui";

type Props = {
  repoId: string;
  onDeleted: () => void;
  onError: (message: string) => void;
};

export default function DeleteRepoButton({ repoId, onDeleted, onError }: Props) {
  const [confirming, setConfirming] = useState(false);
  const [deleting, setDeleting] = useState(false);

  const confirm = async () => {
    setDeleting(true);
    try {
      await apiDelete(`/api/repositories/${repoId}`);
      onDeleted();
    } catch (err) {
      onError(err instanceof ApiError ? err.message : (err as Error).message);
      setConfirming(false);
    } finally {
      setDeleting(false);
    }
  };

  if (!confirming) {
    return (
      <button
        type="button"
        className="flex items-center gap-1 text-xs text-danger hover:underline disabled:opacity-50"
        onClick={() => setConfirming(true)}
        disabled={deleting}
      >
        <TrashIcon />
        Delete
      </button>
    );
  }

  return (
    <span className="flex items-center gap-2">
      {deleting && <span className={spinner} />}
      <button
        type="button"
        aria-label="Confirm delete"
        title="Confirm delete"
        className="flex h-6 w-6 items-center justify-center rounded border border-good/50 bg-good/10 text-xs text-good hover:bg-good/20 disabled:opacity-50"
        onClick={confirm}
        disabled={deleting}
      >
        ✓
      </button>
      <button
        type="button"
        aria-label="Cancel delete"
        title="Cancel delete"
        className="flex h-6 w-6 items-center justify-center rounded border border-border text-xs text-dim hover:bg-border/40 disabled:opacity-50"
        onClick={() => setConfirming(false)}
        disabled={deleting}
      >
        ✕
      </button>
    </span>
  );
}

function TrashIcon() {
  return (
    <svg
      width="13"
      height="13"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      <path d="M3 6h18" />
      <path d="M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2" />
      <path d="m19 6-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6" />
    </svg>
  );
}
