"use client";

import { useCallback, useEffect, useLayoutEffect, useRef, useState } from "react";
import { btn, btnSmall, spinner } from "@/lib/ui";

const MIN_SCALE = 0.25;
const MAX_SCALE = 32;

export default function DiagramViewer({
  chart,
  onClose,
}: {
  chart: string;
  onClose: () => void;
}) {
  const areaRef = useRef<HTMLDivElement | null>(null);
  const contentRef = useRef<HTMLDivElement | null>(null);
  const dragRef = useRef<{ startX: number; startY: number; ox: number; oy: number } | null>(null);
  const [svg, setSvg] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [view, setView] = useState({ scale: 1, x: 0, y: 0 });

  useEffect(() => {
    let cancelled = false;
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
        const id = `mmd-ov-${Math.random().toString(36).slice(2, 10)}`;
        const { svg } = await mermaid.render(id, chart);
        if (!cancelled) setSvg(svg);
      } catch (e) {
        if (!cancelled) setError((e as Error).message);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [chart]);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);

  useEffect(() => {
    const prev = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    return () => {
      document.body.style.overflow = prev;
    };
  }, []);

  const fit = useCallback(() => {
    const area = areaRef.current;
    const content = contentRef.current;
    if (!area || !content) return;
    const svgEl = content.querySelector("svg");
    if (!svgEl) return;
    const aw = area.clientWidth;
    const ah = area.clientHeight;
    const sw = svgEl.viewBox.baseVal.width || svgEl.clientWidth;
    const sh = svgEl.viewBox.baseVal.height || svgEl.clientHeight;
    if (!aw || !ah || !sw || !sh) return;
    const s = Math.min(Math.max(Math.min(aw / sw, ah / sh), 1), MAX_SCALE);
    setView({ scale: s, x: 0, y: 0 });
  }, []);

  useLayoutEffect(() => {
    if (svg) fit();
  }, [svg, fit]);

  const onWheel = useCallback((e: React.WheelEvent<HTMLDivElement>) => {
    const el = contentRef.current;
    if (!el) return;
    const rect = el.getBoundingClientRect();
    const cx = e.clientX - rect.left - rect.width / 2;
    const cy = e.clientY - rect.top - rect.height / 2;
    setView((v) => {
      const next = Math.min(MAX_SCALE, Math.max(MIN_SCALE, v.scale * Math.pow(1.002, -e.deltaY)));
      if (next === v.scale) return v;
      const k = next / v.scale;
      return {
        scale: next,
        x: (1 - k) * cx + k * v.x,
        y: (1 - k) * cy + k * v.y,
      };
    });
  }, []);

  const onPointerDown = useCallback(
    (e: React.PointerEvent<HTMLDivElement>) => {
      dragRef.current = { startX: e.clientX, startY: e.clientY, ox: view.x, oy: view.y };
      e.currentTarget.setPointerCapture(e.pointerId);
    },
    [view],
  );

  const onPointerMove = useCallback((e: React.PointerEvent<HTMLDivElement>) => {
    const d = dragRef.current;
    if (!d) return;
    setView((v) => ({ ...v, x: d.ox + (e.clientX - d.startX), y: d.oy + (e.clientY - d.startY) }));
  }, []);

  const onPointerUp = useCallback(() => {
    dragRef.current = null;
  }, []);

  const zoomBy = (factor: number) => {
    setView((v) => ({
      scale: Math.min(MAX_SCALE, Math.max(MIN_SCALE, v.scale * factor)),
      x: v.x,
      y: v.y,
    }));
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center">
      <div className="viewer-zoom-in absolute inset-0 bg-black/70" onClick={onClose} aria-hidden="true" />
      <div className="viewer-zoom-in relative z-10 flex h-[85vh] w-[92vw] max-w-7xl flex-col overflow-hidden rounded-xl border border-border bg-panel shadow-2xl">
        <div className="flex items-center justify-between border-b border-border px-4 py-2">
          <span className="text-sm font-semibold text-fg">Diagram</span>
          <div className="flex items-center gap-1.5">
            <button type="button" className={`${btn} ${btnSmall}`} onClick={() => zoomBy(1 / 1.25)} title="Zoom out">
              −
            </button>
            <span className="w-12 text-center text-xs text-dim">{Math.round(view.scale * 100)}%</span>
            <button type="button" className={`${btn} ${btnSmall}`} onClick={() => zoomBy(1.25)} title="Zoom in">
              +
            </button>
            <button type="button" className={`${btn} ${btnSmall}`} onClick={fit} title="Reset view (fit)">
              Reset
            </button>
            <button
              type="button"
              className={`${btn} ${btnSmall} ml-2 border-danger text-danger`}
              onClick={onClose}
              title="Close (Esc)"
            >
              ✕
            </button>
          </div>
        </div>
        <div
          ref={areaRef}
          className="relative flex-1 cursor-grab touch-none select-none overflow-hidden"
          onWheel={onWheel}
          onPointerDown={onPointerDown}
          onPointerMove={onPointerMove}
          onPointerUp={onPointerUp}
          onPointerCancel={onPointerUp}
        >
          {error && (
            <pre className="m-4 max-h-[60vh] overflow-auto whitespace-pre-wrap text-xs text-danger">{chart}</pre>
          )}
          {!svg && !error && <div className={`${spinner} absolute left-1/2 top-1/2 -translate-x-1/2 -translate-y-1/2`} />}
          {svg && (
            <div className="absolute inset-0 flex items-center justify-center">
              <div
                ref={contentRef}
                style={{ transform: `translate(${view.x}px, ${view.y}px) scale(${view.scale})` }}
                dangerouslySetInnerHTML={{ __html: svg }}
              />
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
