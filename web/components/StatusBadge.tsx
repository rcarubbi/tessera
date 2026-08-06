"use client";

const STATUS_LABELS = ["Pending", "Cloning", "Parsing", "Analyzing", "Indexing", "Completed", "Failed"];

export default function StatusBadge({ status }: { status: number }) {
  const label = STATUS_LABELS[status] ?? `Status ${status}`;
  const tone = status >= 5 ? "green" : status === 6 ? "red" : "yellow";
  return <span className={`badge ${tone}`}>{label}</span>;
}
