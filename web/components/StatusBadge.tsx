"use client";

const STATUS_LABELS = [
  "Pending",
  "Cloning",
  "Parsing",
  "Analyzing",
  "Indexing",
  "Completed",
  "Failed",
  "Cancelled",
];

const TONES: Record<string, string> = {
  Pending: "badge",
  Cloning: "badge-yellow",
  Parsing: "badge-yellow",
  Analyzing: "badge-yellow",
  Indexing: "badge-yellow",
  Completed: "badge-green",
  Failed: "badge-red",
  Cancelled: "badge",
};

export default function StatusBadge({ status }: { status: number }) {
  const label = STATUS_LABELS[status] ?? `Status ${status}`;
  return <span className={`badge ${TONES[label] ?? "badge-red"}`}>{label}</span>;
}
