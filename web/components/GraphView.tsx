"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { apiGet } from "@/lib/api";
import type { Graph, GraphEdge, GraphNode } from "@/lib/types";

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

type Pos = { x: number; y: number };
type View = { scale: number; x: number; y: number };

const MIN_SCALE = 0.15;
const MAX_SCALE = 6;

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
  const canvasRef = useRef<HTMLCanvasElement | null>(null);
  const [graph, setGraph] = useState<Graph | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [module, setModule] = useState<string>("");
  const [edgeTypes, setEdgeTypes] = useState<Set<string>>(new Set());
  const [expandDepth, setExpandDepth] = useState(1);
  const [hover, setHover] = useState<{ key: string; screenX: number; screenY: number } | null>(null);
  const [pos, setPos] = useState<Map<string, Pos> | null>(null);
  const [view, setView] = useState<View>({ scale: 1, x: 0, y: 0 });

  const dragRef = useRef<{ startX: number; startY: number; viewX: number; viewY: number; moved: boolean } | null>(null);

  const commitParam = commit ? `&commit=${encodeURIComponent(commit)}` : "";

  useEffect(() => {
    setLoading(true);
    setError(null);
    apiGet<Graph>(`/api/repositories/${repoId}/graph${commitParam}`)
      .then((g) => {
        setGraph(g);
        setView({ scale: 1, x: 0, y: 0 });
        setHover(null);
        setEdgeTypes((prev) => (prev.size === 0 ? new Set(g.edges.map((e) => e.type)) : prev));
      })
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, [repoId, commit, commitParam]);

  const modules = useMemo(() => {
    if (!graph) return [] as string[];
    const set = new Set<string>();
    for (const n of graph.nodes) {
      const dir = n.path.split("/").slice(0, -1).join("/") || ".";
      set.add(dir);
    }
    return [...set].sort();
  }, [graph]);

  useEffect(() => {
    if (!graph) return;
    const width = canvasRef.current?.clientWidth ?? 900;
    const height = canvasRef.current?.clientHeight ?? 620;
    const filteredNodes = graph.nodes.filter((n) => !module || n.path.startsWith(module));
    const filteredEdges = graph.edges.filter(
      (e) => (!module || graph.nodes.some((n) => n.key === e.from && n.path.startsWith(module))) && edgeTypes.has(e.type),
    );
    const keys = filteredNodes.map((n) => n.key);
    const pairs = filteredEdges.map((e) => [e.from, e.to] as [string, string]);
    setPos(runLayout(keys, pairs, width, height));
  }, [graph, module, edgeTypes]);

  const kinds = useMemo(() => {
    if (!graph) return [] as string[];
    return [...new Set(graph.nodes.map((n) => n.kind))].sort();
  }, [graph]);

  const draw = useCallback(() => {
    const canvas = canvasRef.current;
    if (!canvas || !graph || !pos) return;
    const ctx = canvas.getContext("2d");
    if (!ctx) return;
    const dpr = window.devicePixelRatio || 1;
    const w = canvas.clientWidth;
    const h = canvas.clientHeight;
    if (canvas.width !== w * dpr) canvas.width = w * dpr;
    if (canvas.height !== h * dpr) canvas.height = h * dpr;

    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.clearRect(0, 0, w, h);
    ctx.setTransform(dpr * view.scale, 0, 0, dpr * view.scale, dpr * view.x, dpr * view.y);

    const neighbors = selectedKey ? neighborSet(graph, selectedKey, expandDepth, edgeTypes) : null;
    const focus = hover?.key ?? selectedKey;
    const focusNeighbors = focus ? neighborSet(graph, focus, 1, edgeTypes) : null;

    for (const e of graph.edges) {
      const a = pos.get(e.from);
      const b = pos.get(e.to);
      if (!a || !b) continue;
      const active = neighbors ? neighbors.has(e.from) && neighbors.has(e.to) : true;
      const focusActive = focusNeighbors ? focusNeighbors.has(e.from) && focusNeighbors.has(e.to) : true;
      ctx.strokeStyle = focusNeighbors
        ? focusActive
          ? "rgba(139,148,158,0.8)"
          : "rgba(139,148,158,0.06)"
        : active
          ? "rgba(139,148,158,0.5)"
          : "rgba(139,148,158,0.08)";
      ctx.lineWidth = 1;
      ctx.beginPath();
      ctx.moveTo(a.x, a.y);
      ctx.lineTo(b.x, b.y);
      ctx.stroke();
    }

    for (const n of graph.nodes) {
      const p = pos.get(n.key);
      if (!p) continue;
      const dimmed = neighbors ? !neighbors.has(n.key) : false;
      const hovered = hover?.key === n.key;
      const isSelected = selectedKey === n.key;
      const color = KIND_COLORS[n.kind] ?? "#8b949e";
      const r = isSelected ? 9 : hovered ? 8 : 6;
      ctx.globalAlpha = focusNeighbors ? (focusNeighbors.has(n.key) ? 1 : 0.2) : dimmed ? 0.25 : 1;
      ctx.fillStyle = color;
      ctx.beginPath();
      ctx.arc(p.x, p.y, r, 0, Math.PI * 2);
      ctx.fill();
      if (isSelected) {
        ctx.strokeStyle = "#ffffff";
        ctx.lineWidth = 2;
        ctx.stroke();
      } else if (hovered) {
        ctx.strokeStyle = color;
        ctx.lineWidth = 2;
        ctx.stroke();
      }
      if (!dimmed && view.scale >= 0.4) {
        ctx.fillStyle = hovered || isSelected ? "#ffffff" : "#e6edf3";
        ctx.font = "11px ui-monospace, Consolas, monospace";
        ctx.fillText(n.symbol, p.x + r + 4, p.y + 4);
      }
      ctx.globalAlpha = 1;
    }
  }, [graph, pos, selectedKey, expandDepth, edgeTypes, hover, view.scale]);

  useEffect(() => {
    draw();
  }, [draw]);

  const worldFromEvent = (e: React.MouseEvent<HTMLCanvasElement>) => {
    const canvas = canvasRef.current!;
    const rect = canvas.getBoundingClientRect();
    const mx = e.clientX - rect.left;
    const my = e.clientY - rect.top;
    return { x: (mx - view.x) / view.scale, y: (my - view.y) / view.scale, mx, my };
  };

  const hitTest = (wx: number, wy: number): string | null => {
    if (!graph || !pos) return null;
    let best: string | null = null;
    let bestDist = 22 / view.scale;
    for (const n of graph.nodes) {
      const np = pos.get(n.key);
      if (!np) continue;
      const d = Math.hypot(np.x - wx, np.y - wy);
      if (d < bestDist) {
        bestDist = d;
        best = n.key;
      }
    }
    return best;
  };

  const onWheel = (e: React.WheelEvent<HTMLCanvasElement>) => {
    e.preventDefault();
    const canvas = canvasRef.current!;
    const rect = canvas.getBoundingClientRect();
    const mx = e.clientX - rect.left;
    const my = e.clientY - rect.top;
    setView((v) => {
      const factor = Math.exp(-e.deltaY * 0.0015);
      const scale = Math.min(MAX_SCALE, Math.max(MIN_SCALE, v.scale * factor));
      const wx = (mx - v.x) / v.scale;
      const wy = (my - v.y) / v.scale;
      return { scale, x: mx - wx * scale, y: my - wy * scale };
    });
  };

  const onMouseDown = (e: React.MouseEvent<HTMLCanvasElement>) => {
    if (e.button !== 0) return;
    dragRef.current = { startX: e.clientX, startY: e.clientY, viewX: view.x, viewY: view.y, moved: false };
  };

  const onMouseMove = (e: React.MouseEvent<HTMLCanvasElement>) => {
    const drag = dragRef.current;
    const { mx, my, x, y } = worldFromEvent(e);
    if (drag) {
      const dx = e.clientX - drag.startX;
      const dy = e.clientY - drag.startY;
      if (Math.abs(dx) + Math.abs(dy) > 3) drag.moved = true;
      if (drag.moved) {
        setView({ ...view, x: drag.viewX + dx, y: drag.viewY + dy });
      }
      setHover(null);
      return;
    }
    setHover({ key: hitTest(x, y) ?? "", screenX: mx, screenY: my });
  };

  const onMouseUp = (e: React.MouseEvent<HTMLCanvasElement>) => {
    const drag = dragRef.current;
    dragRef.current = null;
    if (!drag || drag.moved) return;
    const { x, y } = worldFromEvent(e);
    const hit = hitTest(x, y);
    if (hit) onSelect(hit);
  };

  const onMouseLeave = () => {
    dragRef.current = null;
    setHover(null);
  };

  const zoomBy = (factor: number) => {
    const canvas = canvasRef.current!;
    const cx = canvas.clientWidth / 2;
    const cy = canvas.clientHeight / 2;
    setView((v) => {
      const scale = Math.min(MAX_SCALE, Math.max(MIN_SCALE, v.scale * factor));
      const wx = (cx - v.x) / v.scale;
      const wy = (cy - v.y) / v.scale;
      return { scale, x: cx - wx * scale, y: cy - wy * scale };
    });
  };

  const resetView = () => setView({ scale: 1, x: 0, y: 0 });

  const toggleEdgeType = (type: string) => {
    setEdgeTypes((prev) => {
      const next = new Set(prev);
      if (next.has(type)) next.delete(type);
      else next.add(type);
      return next;
    });
  };

  const hovering = hover?.key ? graph?.nodes.find((n) => n.key === hover.key) : null;

  if (loading) {
    return <div className="panel text-dim">Loading graph…</div>;
  }
  if (error) {
    return <div className="card card-error text-danger">{error}</div>;
  }
  if (!graph || graph.nodes.length === 0) {
    return <div className="panel text-dim">No graph available for this snapshot.</div>;
  }

  return (
    <div>
      <div className="mb-3 flex flex-wrap items-center gap-4 rounded-lg border border-border bg-panel px-3 py-2.5">
        <label className="flex items-center gap-1.5 text-[13px]">
          <span className="text-dim">Module:</span>
          <select className="field" value={module} onChange={(e) => setModule(e.target.value)}>
            <option value="">All</option>
            {modules.map((m) => (
              <option key={m} value={m}>{m}</option>
            ))}
          </select>
        </label>
        <div className="flex flex-wrap items-center gap-3">
          {[...new Set(graph.edges.map((e) => e.type))].sort().map((t) => (
            <label key={t} className="flex items-center gap-1 text-xs">
              <input type="checkbox" checked={edgeTypes.has(t)} onChange={() => toggleEdgeType(t)} />
              {t}
            </label>
          ))}
        </div>
        <label className="flex items-center gap-1.5 text-[13px]">
          <span className="text-dim">Expand:</span>
          <select className="field" value={expandDepth} onChange={(e) => setExpandDepth(Number(e.target.value))}>
            <option value={1}>1 hop</option>
            <option value={2}>2 hops</option>
            <option value={3}>3 hops</option>
          </select>
        </label>
        <div className="ml-auto flex items-center gap-1">
          <button className="btn btn-small" onClick={() => zoomBy(1.3)} title="Zoom in">+</button>
          <button className="btn btn-small" onClick={() => zoomBy(1 / 1.3)} title="Zoom out">−</button>
          <button className="btn btn-small" onClick={resetView} title="Reset view">Reset</button>
        </div>
      </div>

      <div className="relative overflow-hidden rounded-lg border border-border bg-panel">
        <canvas
          ref={canvasRef}
          style={{ width: "100%", height: 620, display: "block", cursor: dragRef.current?.moved ? "grabbing" : "grab", touchAction: "none" }}
          onWheel={onWheel}
          onMouseDown={onMouseDown}
          onMouseMove={onMouseMove}
          onMouseUp={onMouseUp}
          onMouseLeave={onMouseLeave}
          onDoubleClick={resetView}
        />

        {hovering && (
          <div
            className="pointer-events-none absolute z-10 max-w-[320px] rounded-md border border-border bg-inset px-3 py-2 text-xs shadow-lg"
            style={{
              left: Math.min(hover!.screenX + 12, (canvasRef.current?.clientWidth ?? 600) - 200),
              top: Math.max(hover!.screenY - 40, 8),
            }}
          >
            <div className="font-semibold text-fg">{hovering.symbol}</div>
            <div className="truncate font-mono text-dim">{hovering.path}:{hovering.line}</div>
            <div className="mt-0.5 text-dim">{hovering.kind} · {hovering.language}</div>
          </div>
        )}

        <div className="absolute left-3 top-3 rounded-md border border-border bg-panel/90 px-3 py-2 text-[11px]">
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

        <div className="border-t border-border bg-inset px-2.5 py-1.5 text-xs text-dim">
          {graph.nodes.length} nodes · {graph.edges.length} edges ·{" "}
          {selectedKey ? `focusing ${selectedKey}` : "drag to pan · wheel to zoom · double-click to reset · click a node to inspect"}
        </div>
      </div>
    </div>
  );
}

function runLayout(keys: string[], edges: [string, string][], width: number, height: number): Map<string, Pos> {
  const pos = new Map<string, Pos>();
  const n = keys.length;
  keys.forEach((k, i) => {
    const angle = (i / Math.max(n, 1)) * Math.PI * 2;
    const jitter = Math.sin(i * 127.1) * 0.15;
    pos.set(k, {
      x: width / 2 + Math.cos(angle + jitter) * width * 0.32,
      y: height / 2 + Math.sin(angle + jitter) * height * 0.32,
    });
  });
  const speed = 0.07;
  const center = { x: width / 2, y: height / 2 };
  for (let iter = 0; iter < 400; iter++) {
    const disp = new Map<string, { x: number; y: number }>();
    keys.forEach((k) => disp.set(k, { x: 0, y: 0 }));
    for (let i = 0; i < n; i++) {
      for (let j = i + 1; j < n; j++) {
        const a = pos.get(keys[i])!;
        const b = pos.get(keys[j])!;
        let dx = a.x - b.x;
        let dy = a.y - b.y;
        let d2 = dx * dx + dy * dy;
        if (d2 < 0.01) d2 = 0.01;
        const d = Math.sqrt(d2);
        const f = 6000 / d2;
        dx /= d;
        dy /= d;
        disp.get(keys[i])!.x += dx * f;
        disp.get(keys[i])!.y += dy * f;
        disp.get(keys[j])!.x -= dx * f;
        disp.get(keys[j])!.y -= dy * f;
      }
    }
    for (const [u, v] of edges) {
      const a = pos.get(u);
      const b = pos.get(v);
      if (!a || !b) continue;
      let dx = a.x - b.x;
      let dy = a.y - b.y;
      const d = Math.max(Math.sqrt(dx * dx + dy * dy), 0.001);
      const target = 120;
      const f = (d - target) * 0.02;
      dx /= d;
      dy /= d;
      disp.get(u)!.x -= dx * f;
      disp.get(u)!.y -= dy * f;
      disp.get(v)!.x += dx * f;
      disp.get(v)!.y += dy * f;
    }
    for (const k of keys) {
      const p = pos.get(k)!;
      const d = disp.get(k)!;
      d.x += (center.x - p.x) * 0.001;
      d.y += (center.y - p.y) * 0.001;
    }
    for (const k of keys) {
      const p = pos.get(k)!;
      const d = disp.get(k)!;
      p.x += d.x * speed;
      p.y += d.y * speed;
    }
  }
  return pos;
}

function neighborSet(graph: Graph, root: string, depth: number, edgeTypes: Set<string>): Set<string> {
  const adj = new Map<string, Set<string>>();
  for (const e of graph.edges) {
    if (!edgeTypes.has(e.type)) continue;
    if (!adj.has(e.from)) adj.set(e.from, new Set());
    if (!adj.has(e.to)) adj.set(e.to, new Set());
    adj.get(e.from)!.add(e.to);
    adj.get(e.to)!.add(e.from);
  }
  const visited = new Set<string>([root]);
  let frontier = [root];
  for (let d = 0; d < depth; d++) {
    const next: string[] = [];
    for (const k of frontier) {
      for (const nb of adj.get(k) ?? []) {
        if (!visited.has(nb)) {
          visited.add(nb);
          next.push(nb);
        }
      }
    }
    frontier = next;
  }
  return visited;
}
