"use client";

import { badge, badgeGreen, badgeRed, badgeYellow } from "@/lib/ui";

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
  Pending: badge,
  Cloning: badgeYellow,
  Parsing: badgeYellow,
  Analyzing: badgeYellow,
  Indexing: badgeYellow,
  Completed: badgeGreen,
  Failed: badgeRed,
  Cancelled: badge,
};

export default function StatusBadge({ status }: { status: number }) {
  const label = STATUS_LABELS[status] ?? `Status ${status}`;
  return <span className={`${badge} ${TONES[label] ?? badgeRed}`}>{label}</span>;
}
