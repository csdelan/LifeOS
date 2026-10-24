import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  Background,
  BackgroundVariant,
  Controls,
  MiniMap,
  ReactFlow,
  ReactFlowProvider,
  useEdgesState,
  useNodesState,
  useReactFlow,
  type ColorMode,
  type Edge,
  type NodeMouseHandler,
} from "@xyflow/react";
import { useTheme } from "next-themes";
import { NetworkIcon } from "lucide-react";
import "@xyflow/react/dist/style.css";
import "./graph.css";
import { EmptyState } from "@/components/primitives/empty-state";
import { GraphSkeleton } from "@/components/primitives/skeletons";
import { FloatingEdge } from "@/components/graph/floating-edge";
import { toFlowEdge } from "@/components/graph/flow-edges";
import { shownStatus } from "@/lib/task-series";
import {
  SubjectGraphNode,
  type SubjectFlowNode,
  type SubjectNodeData,
} from "@/components/graph/subject-node";
import { layoutAlignmentGraph } from "@/lib/graph-layout";
import {
  areaAccent,
  estimateNodeSize,
  type AlignmentGraphModel,
  type GraphLayoutKind,
} from "@/lib/graph-model";
import type { AreaRow } from "@/lib/production-ui-types";
import { typeLabel } from "@/lib/production-ui-types";
import { motionMs } from "@/lib/media-query";
import { typeAccent } from "@/lib/subject-meta";

const nodeTypes = { subject: SubjectGraphNode };
const edgeTypes = { floating: FloatingEdge };

function neighborSet(id: string | null, edges: Edge[]): Set<string> | null {
  if (!id) return null;
  const next = new Set<string>([id]);
  for (const e of edges) {
    if (e.source === id) next.add(e.target);
    if (e.target === id) next.add(e.source);
  }
  return next;
}

function dimClass(dim: boolean): string | undefined {
  return dim ? "dimmed" : undefined;
}

function GraphCanvas({
  model,
  areas,
  colorByArea,
  layout,
  selected,
  onSelect,
}: {
  model: AlignmentGraphModel;
  areas: AreaRow[];
  colorByArea: boolean;
  layout: GraphLayoutKind;
  selected?: string;
  onSelect: (id: string) => void;
}) {
  const { resolvedTheme } = useTheme();
  const colorMode: ColorMode = resolvedTheme === "light" ? "light" : "dark";
  const { fitView } = useReactFlow();
  const [nodes, setNodes, onNodesChange] = useNodesState<SubjectFlowNode>([]);
  const [rfEdges, setRfEdges, onEdgesChange] = useEdgesState<Edge>([]);
  const [hovered, setHovered] = useState<string | null>(null);
  const [laidOut, setLaidOut] = useState(false);
  const [layoutError, setLayoutError] = useState<string | null>(null);
  const layoutGen = useRef(0);
  const edgesRef = useRef(rfEdges);
  edgesRef.current = rfEdges;

  const layoutKey = useMemo(() => {
    const ids = model.nodes.map((n) => n.subject.id).join(",");
    const eids = model.edges.map((e) => e.id).join(",");
    return `${layout}|${ids}|${eids}`;
  }, [layout, model]);

  useEffect(() => {
    const gen = ++layoutGen.current;
    setLayoutError(null);
    if (model.nodes.length === 0) {
      setNodes([]);
      setRfEdges([]);
      setLaidOut(true);
      return;
    }
    setLaidOut(false);

    const nextNodes: SubjectFlowNode[] = model.nodes.map((n) => {
      const size = estimateNodeSize(n.subject.title, n.degree);
      return {
        id: n.subject.id,
        type: "subject",
        position: { x: 0, y: 0 },
        width: size.width,
        height: size.height,
        data: {
          subject: n.subject,
          degree: n.degree,
          colorByArea,
          areaColor: areaAccent(n.subject.area, areas),
        },
        ariaLabel: `${n.subject.title}, ${typeLabel(n.subject.type)}${
          n.subject.isSeries ? `, ${shownStatus(n.subject)}` : n.subject.status ? `, ${n.subject.status}` : ""
        }`,
        style: { width: size.width, height: size.height },
        selected: n.subject.id === selected,
      };
    });

    const nextEdges: Edge[] = model.edges.map((e) => toFlowEdge(e, layout));

    void layoutAlignmentGraph(
      nextNodes.map((n) => ({
        id: n.id,
        width: n.width ?? 200,
        height: n.height ?? 72,
      })),
      nextEdges.map((e) => ({ id: e.id, source: e.source, target: e.target })),
      layout,
    )
      .then((pos) => {
        if (gen !== layoutGen.current) return;
        setNodes(
          nextNodes.map((n) => ({
            ...n,
            position: pos.get(n.id) ?? n.position,
          })),
        );
        setRfEdges(nextEdges);
        setLaidOut(true);
      })
      .catch((err: unknown) => {
        if (gen !== layoutGen.current) return;
        setLayoutError(err instanceof Error ? err.message : "Graph failed to load");
        setLaidOut(true);
      });
  }, [layoutKey]); // layout + membership; color/selection applied below

  // The latest read of each subject too, so a new title or cover shows without a new layout.
  useEffect(() => {
    const latest = new Map(model.nodes.map((m) => [m.subject.id, m.subject]));
    setNodes((nds) => {
      let changed = false;
      const next = nds.map((n) => {
        const subject = latest.get(n.id) ?? n.data.subject;
        const areaColor = areaAccent(subject.area, areas);
        const selectedNow = n.id === selected;
        if (
          n.data.subject === subject &&
          n.data.colorByArea === colorByArea &&
          n.data.areaColor === areaColor &&
          n.selected === selectedNow
        ) {
          return n;
        }
        changed = true;
        return {
          ...n,
          selected: selectedNow,
          data: { ...n.data, subject, colorByArea, areaColor },
        };
      });
      return changed ? next : nds;
    });
  }, [model, colorByArea, areas, selected, setNodes]);

  useEffect(() => {
    const highlight = hovered ?? selected ?? null;
    const neighbors = neighborSet(highlight, edgesRef.current);
    setNodes((nds) => {
      let changed = false;
      const next = nds.map((n) => {
        const className = dimClass(!!neighbors && !neighbors.has(n.id));
        if (n.className === className) return n;
        changed = true;
        return { ...n, className };
      });
      return changed ? next : nds;
    });
    setRfEdges((eds) => {
      let changed = false;
      const next = eds.map((e) => {
        const className = dimClass(
          !!highlight && e.source !== highlight && e.target !== highlight,
        );
        if (e.className === className) return e;
        changed = true;
        return { ...e, className };
      });
      return changed ? next : eds;
    });
  }, [hovered, selected, setNodes, setRfEdges]);

  useEffect(() => {
    if (!laidOut || model.nodes.length === 0) return;
    const frame = requestAnimationFrame(() => {
      void fitView({ padding: 0.18, duration: motionMs(220) });
    });
    return () => cancelAnimationFrame(frame);
  }, [laidOut, layoutKey, fitView, model.nodes.length]);

  const onNodeClick: NodeMouseHandler<SubjectFlowNode> = useCallback(
    (_event, node) => {
      setHovered(node.id);
      onSelect(node.id);
    },
    [onSelect],
  );

  const onNodeDoubleClick: NodeMouseHandler<SubjectFlowNode> = useCallback(
    (_event, node) => {
      void fitView({ nodes: [{ id: node.id }], padding: 0.5, duration: motionMs(280) });
    },
    [fitView],
  );

  if (layoutError) {
    return (
      <div className="h-full">
        <EmptyState
          tone="error"
          icon={NetworkIcon}
          title="Graph failed to load"
          description="This is not an empty graph — layout never returned."
        />
      </div>
    );
  }

  if (!laidOut && model.nodes.length > 0) {
    return (
      <div className="h-full">
        <GraphSkeleton />
      </div>
    );
  }

  if (model.nodes.length === 0) {
    return (
      <div className="h-full">
        <EmptyState
          icon={NetworkIcon}
          title="No nodes match these filters"
          description="Widen the lens, show finished or archived work, or show orphans — the graph is filtered, not missing."
        />
      </div>
    );
  }

  return (
    <ReactFlow
      className={`lifeos-graph h-full${layout === "force" ? " is-force" : ""}`}
      nodes={nodes}
      edges={rfEdges}
      onNodesChange={onNodesChange}
      onEdgesChange={onEdgesChange}
      nodeTypes={nodeTypes}
      edgeTypes={edgeTypes}
      colorMode={colorMode}
      onNodeClick={onNodeClick}
      onNodeDoubleClick={onNodeDoubleClick}
      onNodeMouseEnter={(_, node) => setHovered(node.id)}
      onNodeMouseLeave={() => setHovered(null)}
      onPaneClick={() => setHovered(null)}
      nodesConnectable={false}
      edgesReconnectable={false}
      zoomOnDoubleClick={false}
      panOnDrag
      zoomOnPinch
      panOnScroll={false}
      minZoom={0.2}
      maxZoom={1.75}
      aria-label="Alignment graph. Use Outline view for an accessible equivalent."
      attributionPosition="bottom-left"
      defaultEdgeOptions={{
        interactionWidth: 16,
      }}
    >
      <Background
        id="dots"
        variant={BackgroundVariant.Dots}
        gap={18}
        size={1.2}
        color="color-mix(in oklch, var(--foreground) 14%, transparent)"
      />
      <Controls showInteractive={false} />
      <MiniMap
        pannable
        zoomable
        nodeStrokeWidth={2}
        nodeColor={(n) => {
          const data = n.data as SubjectNodeData | undefined;
          if (!data) return "var(--muted)";
          return data.colorByArea ? data.areaColor : typeAccent(data.subject.type);
        }}
      />
    </ReactFlow>
  );
}

export function AlignmentGraph(props: {
  /** The filtered graph (buildAlignmentGraph): the page builds it, so it can count what's hidden. */
  model: AlignmentGraphModel;
  areas: AreaRow[];
  colorByArea: boolean;
  layout: GraphLayoutKind;
  selected?: string;
  onSelect: (id: string) => void;
}) {
  return (
    <div className="h-full min-h-0 w-full">
      <ReactFlowProvider>
        <GraphCanvas {...props} />
      </ReactFlowProvider>
    </div>
  );
}
