"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import DiagramViewer from "./DiagramViewer";
import { sanitizeMermaidChart } from "@/lib/mermaidSanitize";

export default function Mermaid({ chart }: { chart: string }) {
  const ref = useRef<HTMLDivElement>(null);
  const [error, setError] = useState<string | null>(null);
  const [open, setOpen] = useState(false);
  const safeChart = useMemo(() => sanitizeMermaidChart(chart), [chart]);

  useEffect(() => {
    let cancelled = false;
    let cleanup: (() => void) | undefined;
    (async () => {
      try {
        const mod = await import("mermaid");
        const mermaid = mod.default;
        mermaid.initialize({
          startOnLoad: false,
          theme: "dark",
          securityLevel: "loose",
          fontFamily: "inherit",
        });
        const id = `mmd-${Math.random().toString(36).slice(2, 10)}`;
        const { svg } = await mermaid.render(id, safeChart);
        if (!cancelled && ref.current) {
          ref.current.innerHTML = svg;
        }
      } catch (e) {
        if (!cancelled) setError((e as Error).message);
      }
    })();
    return () => {
      cancelled = true;
      cleanup?.();
    };
  }, [safeChart]);

  if (error) {
    return (
      <pre className="my-3 overflow-auto rounded-md bg-inset p-3 text-xs text-dim whitespace-pre-wrap">
        {chart}
      </pre>
    );
  }

  return (
    <div className="group relative my-3 aspect-[14/9]">
      <div
        ref={ref}
        className="h-full cursor-zoom-in overflow-auto rounded-md border border-border transition-colors hover:border-border"
        onClick={() => setOpen(true)}
      />
      <div className="pointer-events-none absolute right-2 top-2 z-10 rounded-md border border-border bg-panel/90 px-2 py-0.5 text-[10px] text-dim opacity-0 transition-opacity group-hover:opacity-100">
        Click to expand
      </div>
      {open && <DiagramViewer chart={safeChart} onClose={() => setOpen(false)} />}
    </div>
  );
}
