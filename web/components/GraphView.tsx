"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import dynamic from "next/dynamic";
import { apiGet } from "@/lib/api";
import type { Graph, GraphEdge, GraphNode } from "@/lib/types";
import type { GraphCanvasRef, InternalGraphNode } from "reagraph";
import { card, cardError, select } from "@/lib/ui";

const GraphCanvas = dynamic(() => import("reagraph").then((m) => m.GraphCanvas), {
  ssr: false,
});

const KIND_COLORS: Record<string, string> = {
  Class: "#58a6ff",
  Interface: "#bc8cff",
  Struct: "#3fb950",
  Enum: "#d29922",
  Record: "#f0883e",
  Method: "#7ee787",
  Function: "#7ee787",
  Module: "#f85149",
  Property: "#79c0ff",
  Event: "#ff7b72",
};

const GRAPH_THEME = {
  canvas: { background: "#0d1117" },
  node: {
    fill: "#8b949e",
    activeFill: "#58a6ff",
    opacity: 1,
    selectedOpacity: 1,
    inactiveOpacity: 0.18,
    label: { stroke: "#0d1117", color: "#e6edf3", activeColor: "#58a6ff" },
  },
  ring: { fill: "#2d333b", activeFill: "#58a6ff" },
  edge: {
    fill: "#2d333b",
    activeFill: "#58a6ff",
    opacity: 1,
    selectedOpacity: 1,
    inactiveOpacity: 0.12,
    label: { stroke: "#0d1117", color: "#8b949e", activeColor: "#58a6ff", fontSize: 6 },
  },
  arrow: { fill: "#2d333b", activeFill: "#58a6ff" },
  lasso: { border: "1px solid #58a6ff", background: "rgba(88,166,255,0.1)" },
};

export default function GraphView({
  repoId,
  commit,
  onSelect,
  selectedKey,
}: {
  repoId: string;
  commit: string | null;
  onSelect: (key: string) => void;
  selectedKey: string | null;
}) {
  const wrapRef = useRef<HTMLDivElement | null>(null);
  const graphRef = useRef<GraphCanvasRef | null>(null);
  const accordionRef = useRef<HTMLDivElement | null>(null);
  const [graph, setGraph] = useState<Graph | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [module, setModule] = useState<string>("");
  const [edgeTypes, setEdgeTypes] = useState<Set<string>>(new Set());
  const [expandDepth, setExpandDepth] = useState(1);
  const [showMethods, setShowMethods] = useState(true);
  const [hover, setHover] = useState<{ node: GraphNode; x: number; y: number } | null>(null);

  const commitParam = commit ? `&commit=${encodeURIComponent(commit)}` : "";

  useEffect(() => {
    setLoading(true);
    setError(null);
    apiGet<Graph>(`/api/repositories/${repoId}/graph${commitParam}`)
      .then((g) => {
        setGraph(g);
        setHover(null);
        setEdgeTypes((prev) => (prev.size === 0 ? new Set(g.edges.map((e) => e.type)) : prev));
      })
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, [repoId, commit, commitParam]);

  const ready = !loading && !error && !!graph && graph.nodes.length > 0;

  useEffect(() => {
    if (!ready) return;
    let cancelled = false;
    let instance: { destroy(): void } | null = null;
    (async () => {
      const { HSAccordion } = await import("preline/non-auto");
      if (cancelled || !accordionRef.current) return;
      instance = new HSAccordion(accordionRef.current);
    })();
    return () => {
      cancelled = true;
      instance?.destroy();
    };
  }, [ready]);

  const modules = useMemo(() => {
    if (!graph) return [] as string[];
    const set = new Set<string>();
    for (const n of graph.nodes) {
      const dir = n.path.split("/").slice(0, -1).join("/") || ".";
      set.add(dir);
    }
    return [...set].sort();
  }, [graph]);

  const visibleNodes = useMemo(() => {
    if (!graph) return [] as GraphNode[];
    return graph.nodes.filter(
      (n) =>
        (!module || n.path.startsWith(module)) &&
        (showMethods || (n.kind !== "Method" && n.kind !== "Function")),
    );
  }, [graph, module, showMethods]);

  const visibleKeys = useMemo(() => new Set(visibleNodes.map((n) => n.key)), [visibleNodes]);

  const visibleEdges = useMemo(() => {
    if (!graph) return [] as GraphEdge[];
    return graph.edges.filter(
      (e) => edgeTypes.has(e.type) && visibleKeys.has(e.from) && visibleKeys.has(e.to),
    );
  }, [graph, edgeTypes, visibleKeys]);

  const nodes = useMemo(
    () =>
      visibleNodes.map((n) => ({
        id: n.key,
        label: n.symbol,
        fill: KIND_COLORS[n.kind] ?? "#8b949e",
        data: n,
      })),
    [visibleNodes],
  );

  const edges = useMemo(
    () =>
      visibleEdges.map((e, i) => ({
        id: `e-${i}`,
        source: e.from,
        target: e.to,
        data: e,
      })),
    [visibleEdges],
  );

  const selections = useMemo(() => {
    if (!selectedKey || !visibleKeys.has(selectedKey)) return [] as string[];
    return [selectedKey];
  }, [selectedKey, visibleKeys]);

  const actives = useMemo(() => {
    if (!selectedKey || !visibleKeys.has(selectedKey)) return [] as string[];
    const adj = new Map<string, Set<string>>();
    for (const e of visibleEdges) {
      if (!adj.has(e.from)) adj.set(e.from, new Set());
      if (!adj.has(e.to)) adj.set(e.to, new Set());
      adj.get(e.from)!.add(e.to);
      adj.get(e.to)!.add(e.from);
    }
    const focus = new Set<string>([selectedKey]);
    let frontier = [selectedKey];
    for (let d = 0; d < expandDepth; d++) {
      const next: string[] = [];
      for (const k of frontier) {
        for (const nb of adj.get(k) ?? []) {
          if (!focus.has(nb)) {
            focus.add(nb);
            next.push(nb);
          }
        }
      }
      frontier = next;
    }
    const ids: string[] = [...focus];
    visibleEdges.forEach((e, i) => {
      if (focus.has(e.from) && focus.has(e.to)) ids.push(`e-${i}`);
    });
    return ids;
  }, [selectedKey, visibleKeys, visibleEdges, expandDepth]);

  const kinds = useMemo(() => {
    if (!graph) return [] as string[];
    return [...new Set(graph.nodes.map((n) => n.kind))].sort();
  }, [graph]);

  const onNodeClick = useCallback(
    (node: InternalGraphNode) => {
      onSelect(node.id);
    },
    [onSelect],
  );

  const onNodePointerOver = useCallback(
    (node: InternalGraphNode, event: { nativeEvent: PointerEvent }) => {
      const rect = wrapRef.current?.getBoundingClientRect();
      if (!rect) return;
      setHover({
        node: node.data as GraphNode,
        x: event.nativeEvent.clientX - rect.left,
        y: event.nativeEvent.clientY - rect.top,
      });
    },
    [],
  );

  const onNodePointerOut = useCallback(() => setHover(null), []);

  const zoomIn = () => graphRef.current?.zoomIn();
  const zoomOut = () => graphRef.current?.zoomOut();
  const resetView = () => graphRef.current?.resetControls(true);

  const toggleEdgeType = (type: string) => {
    setEdgeTypes((prev) => {
      const next = new Set(prev);
      if (next.has(type)) next.delete(type);
      else next.add(type);
      return next;
    });
  };

  if (loading) {
    return <div className={card}>Loading graph…</div>;
  }
  if (error) {
    return <div className={`${card} ${cardError} text-danger`}>{error}</div>;
  }
  if (!graph || graph.nodes.length === 0) {
    return <div className={card}>No graph available for this snapshot.</div>;
  }

  return (
    <div>
      <div
        ref={accordionRef}
        className="hs-accordion --prevent-on-load-init mb-3 rounded-lg border border-border bg-panel"
      >
        <button
          type="button"
          aria-expanded="false"
          className="hs-accordion-toggle flex w-full cursor-pointer items-center gap-1.5 px-4 py-2.5 text-[13px] font-medium text-fg select-none"
        >
          <svg
            viewBox="0 0 16 16"
            className="h-3.5 w-3.5 text-dim transition-transform hs-accordion-active:rotate-90"
            fill="none"
            stroke="currentColor"
            strokeWidth="1.5"
          >
            <path d="M6 3l5 5-5 5" strokeLinecap="round" strokeLinejoin="round" />
          </svg>
          Settings
        </button>
        <div className="hs-accordion-content space-y-4 border-t border-border px-4 py-3.5" style={{ display: "none" }}>
          <div className="flex flex-wrap items-end gap-x-6 gap-y-3">
            <label className="flex flex-col gap-1">
              <span className="text-[11px] font-medium uppercase tracking-wider text-dim">Module</span>
              <select className={select} value={module} onChange={(e) => setModule(e.target.value)}>
                <option value="">All</option>
                {modules.map((m) => (
                  <option key={m} value={m}>{m}</option>
                ))}
              </select>
            </label>
            <label className="flex flex-col gap-1">
              <span className="text-[11px] font-medium uppercase tracking-wider text-dim">Expand</span>
              <select className={select} value={expandDepth} onChange={(e) => setExpandDepth(Number(e.target.value))}>
                <option value={1}>1 hop</option>
                <option value={2}>2 hops</option>
                <option value={3}>3 hops</option>
              </select>
            </label>
            <label className="flex cursor-pointer items-center gap-2 pb-1 text-[13px] select-none">
              <span className="relative inline-flex">
                <input
                  type="checkbox"
                  className="peer sr-only"
                  checked={showMethods}
                  onChange={(e) => setShowMethods(e.target.checked)}
                />
                <span className="h-4 w-7 rounded-full bg-inset ring-1 ring-border transition-colors peer-checked:bg-accent" />
                <span className="absolute left-0.5 top-0.5 h-3 w-3 rounded-full bg-fg transition-transform peer-checked:translate-x-3" />
              </span>
              <span className="text-dim">Show method nodes</span>
            </label>
          </div>
          <div>
            <div className="mb-2 text-[11px] font-medium uppercase tracking-wider text-dim">Edge types</div>
            <div className="flex flex-wrap gap-x-5 gap-y-2.5">
              {[...new Set(graph.edges.map((e) => e.type))].sort().map((t) => (
                <label key={t} className="flex cursor-pointer items-center gap-2 text-xs select-none">
                  <span className="relative inline-flex">
                    <input type="checkbox" className="peer sr-only" checked={edgeTypes.has(t)} onChange={() => toggleEdgeType(t)} />
                    <span className="h-4 w-7 rounded-full bg-inset ring-1 ring-border transition-colors peer-checked:bg-accent" />
                    <span className="absolute left-0.5 top-0.5 h-3 w-3 rounded-full bg-fg transition-transform peer-checked:translate-x-3" />
                  </span>
                  {t}
                </label>
              ))}
            </div>
          </div>
        </div>
      </div>

      <div className="relative overflow-hidden rounded-lg border border-border bg-bg">
        <div
          ref={wrapRef}
          className="relative"
          style={{ height: "calc(100vh - 240px)", minHeight: 480 }}
        >
          <GraphCanvas
            ref={graphRef}
            nodes={nodes}
            edges={edges}
            selections={selections}
            actives={actives}
            theme={GRAPH_THEME}
            layoutType="forceDirected2d"
            labelType="nodes"
            edgeArrowPosition="none"
            cameraMode="pan"
            onNodeClick={onNodeClick}
            onNodePointerOver={onNodePointerOver}
            onNodePointerOut={onNodePointerOut}
          />
        </div>

        {hover && (
          <div
            className="pointer-events-none absolute z-10 max-w-[320px] rounded-lg border border-border bg-inset px-3 py-2 text-xs shadow-lg"
            style={{
              left: Math.min(hover.x + 12, (wrapRef.current?.clientWidth ?? 600) - 200),
              top: Math.max(hover.y - 40, 8),
            }}
          >
            <div className="font-semibold text-fg">{hover.node.symbol}</div>
            <div className="truncate font-mono text-dim">{hover.node.path}:{hover.node.line}</div>
            <div className="mt-0.5 text-dim">{hover.node.kind} · {hover.node.language}</div>
          </div>
        )}

        <div className="absolute left-3 top-3 rounded-lg border border-border bg-panel/90 px-3 py-2 text-[11px]">
          <div className="mb-1 font-semibold text-dim">Kinds</div>
          <div className="grid grid-cols-2 gap-x-3 gap-y-0.5">
            {kinds.map((k) => (
              <div key={k} className="flex items-center gap-1.5">
                <span className="inline-block h-2.5 w-2.5 rounded-full" style={{ background: KIND_COLORS[k] ?? "#8b949e" }} />
                {k}
              </div>
            ))}
          </div>
        </div>

        <div className="absolute right-3 top-3 flex items-center gap-0.5 rounded-lg border border-border bg-panel/90 p-0.5">
          <button
            type="button"
            onClick={zoomOut}
            title="Zoom out"
            aria-label="Zoom out"
            className="inline-flex h-6 w-6 cursor-pointer items-center justify-center rounded-md text-dim transition-colors hover:bg-inset hover:text-fg"
          >
            −
          </button>
          <button
            type="button"
            onClick={zoomIn}
            title="Zoom in"
            aria-label="Zoom in"
            className="inline-flex h-6 w-6 cursor-pointer items-center justify-center rounded-md text-dim transition-colors hover:bg-inset hover:text-fg"
          >
            +
          </button>
          <button
            type="button"
            onClick={resetView}
            title="Reset view"
            className="inline-flex h-6 cursor-pointer items-center justify-center rounded-md px-2 text-xs text-dim transition-colors hover:bg-inset hover:text-fg"
          >
            Reset
          </button>
        </div>

        <div className="border-t border-border bg-inset px-2.5 py-1.5 text-xs text-dim">
          {graph.nodes.length} nodes · {graph.edges.length} edges ·{" "}
          {selectedKey ? `focusing ${selectedKey}` : "drag to pan · wheel to zoom · click a node to inspect"}
        </div>
      </div>
    </div>
  );
}
