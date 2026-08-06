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
  const [hoverKey, setHoverKey] = useState<string | null>(null);
  const [pos, setPos] = useState<Map<string, Pos> | null>(null);

  const commitParam = commit ? `&commit=${encodeURIComponent(commit)}` : "";

  useEffect(() => {
    setLoading(true);
    setError(null);
    apiGet<Graph>(`/api/repositories/${repoId}/graph${commitParam}`)
      .then((g) => {
        setGraph(g);
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
    const layout = runLayout(keys, pairs, width, height);
    setPos(layout);
  }, [graph, module, edgeTypes]);

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

    const neighbors = selectedKey ? neighborSet(graph, selectedKey, expandDepth, edgeTypes) : null;

    for (const e of graph.edges) {
      const a = pos.get(e.from);
      const b = pos.get(e.to);
      if (!a || !b) continue;
      const active = neighbors ? neighbors.has(e.from) && neighbors.has(e.to) : true;
      ctx.strokeStyle = active ? "rgba(139,148,158,0.5)" : "rgba(139,148,158,0.08)";
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
      const isSelected = selectedKey === n.key;
      const isHover = hoverKey === n.key;
      const color = KIND_COLORS[n.kind] ?? "#8b949e";
      const r = isSelected ? 9 : isHover ? 8 : 6;
      ctx.globalAlpha = dimmed ? 0.25 : 1;
      ctx.fillStyle = color;
      ctx.beginPath();
      ctx.arc(p.x, p.y, r, 0, Math.PI * 2);
      ctx.fill();
      if (isSelected) {
        ctx.strokeStyle = "#ffffff";
        ctx.lineWidth = 2;
        ctx.stroke();
      }
      ctx.fillStyle = "#e6edf3";
      ctx.font = "11px ui-monospace, Consolas, monospace";
      ctx.fillText(n.symbol, p.x + r + 4, p.y + 4);
      ctx.globalAlpha = 1;
    }
  }, [graph, pos, selectedKey, expandDepth, edgeTypes, hoverKey]);

  useEffect(() => {
    draw();
  }, [draw]);

  const worldFromEvent = (e: React.MouseEvent<HTMLCanvasElement>) => {
    const canvas = canvasRef.current!;
    const rect = canvas.getBoundingClientRect();
    return { x: e.clientX - rect.left, y: e.clientY - rect.top };
  };

  const hitTest = (p: { x: number; y: number }): string | null => {
    if (!graph || !pos) return null;
    let best: string | null = null;
    let bestDist = 20;
    for (const n of graph.nodes) {
      const np = pos.get(n.key);
      if (!np) continue;
      const d = Math.hypot(np.x - p.x, np.y - p.y);
      if (d < bestDist) {
        bestDist = d;
        best = n.key;
      }
    }
    return best;
  };

  const onMove = (e: React.MouseEvent<HTMLCanvasElement>) => {
    setHoverKey(hitTest(worldFromEvent(e)));
  };

  const onClick = (e: React.MouseEvent<HTMLCanvasElement>) => {
    const hit = hitTest(worldFromEvent(e));
    if (hit) onSelect(hit);
  };

  const toggleEdgeType = (type: string) => {
    setEdgeTypes((prev) => {
      const next = new Set(prev);
      if (next.has(type)) next.delete(type);
      else next.add(type);
      return next;
    });
  };

  if (loading) {
    return <div className="panel muted">Loading graph…</div>;
  }
  if (error) {
    return <div className="panel" style={{ color: "var(--red)" }}>{error}</div>;
  }
  if (!graph || graph.nodes.length === 0) {
    return <div className="panel muted">No graph available for this snapshot.</div>;
  }

  return (
    <div>
      <div className="card" style={{ marginBottom: 12, padding: 10, display: "flex", gap: 16, flexWrap: "wrap", alignItems: "center" }}>
        <label style={{ display: "flex", alignItems: "center", gap: 6 }}>
          <span className="muted">Module:</span>
          <select value={module} onChange={(e) => setModule(e.target.value)}>
            <option value="">All</option>
            {modules.map((m) => (
              <option key={m} value={m}>{m}</option>
            ))}
          </select>
        </label>
        <div style={{ display: "flex", gap: 10, flexWrap: "wrap" }}>
          {[...new Set(graph.edges.map((e) => e.type))].sort().map((t) => (
            <label key={t} style={{ display: "flex", alignItems: "center", gap: 4, fontSize: 12 }}>
              <input type="checkbox" checked={edgeTypes.has(t)} onChange={() => toggleEdgeType(t)} />
              {t}
            </label>
          ))}
        </div>
        <label style={{ display: "flex", alignItems: "center", gap: 6 }}>
          <span className="muted">Expand:</span>
          <select value={expandDepth} onChange={(e) => setExpandDepth(Number(e.target.value))}>
            <option value={1}>1 hop</option>
            <option value={2}>2 hops</option>
            <option value={3}>3 hops</option>
          </select>
        </label>
      </div>
      <div className="panel" style={{ padding: 0, overflow: "hidden" }}>
        <canvas
          ref={canvasRef}
          style={{ width: "100%", height: 620, display: "block", cursor: "pointer" }}
          onMouseMove={onMove}
          onMouseLeave={() => setHoverKey(null)}
          onClick={onClick}
        />
        <div className="muted" style={{ padding: "6px 10px", fontSize: 12 }}>
          {graph.nodes.length} nodes · {graph.edges.length} edges · {selectedKey ? `focusing ${selectedKey}` : "click a node to inspect"}
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
    pos.set(k, { x: width / 2 + Math.cos(angle) * width * 0.32, y: height / 2 + Math.sin(angle) * height * 0.32 });
  });
  const speed = 0.06;
  const center = { x: width / 2, y: height / 2 };
  for (let iter = 0; iter < 300; iter++) {
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
