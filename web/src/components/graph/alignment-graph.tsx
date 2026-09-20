import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  Background,
  BackgroundVariant,
  Controls,
  MarkerType,
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
import {
  SubjectGraphNode,
  type SubjectFlowNode,
  type SubjectNodeData,
} from "@/components/graph/subject-node";
import { layoutAlignmentGraph } from "@/lib/graph-layout";
import {
  areaAccent,
  buildAlignmentGraph,
  estimateNodeSize,
  type GraphLayoutKind,
  type OrphanFilter,
} from "@/lib/graph-model";
import type { AlignmentEdge, MapLens } from "@/lib/derive";
import type { AreaRow, Relation, SubjectListItem } from "@/lib/production-ui-types";
import { typeAccent } from "@/lib/subject-meta";

const nodeTypes = { subject: SubjectGraphNode };

const RELATION_LABEL: Record<Relation, string> = {
  serves: "serves",
  results_in: "results in",
  supersedes: "supersedes",
};

function relationStroke(relation: Relation): string {
  if (relation === "serves") return "var(--type-goal)";
  if (relation === "results_in") return "var(--type-project)";
  return "var(--attention)";
}

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
  subjects,
  edges,
  areas,
  lens,
  orphans,
  area,
  colorByArea,
  layout,
  selected,
  onSelect,
}: {
  subjects: SubjectListItem[];
  edges: AlignmentEdge[];
  areas: AreaRow[];
  lens: MapLens;
  orphans: OrphanFilter;
  area: string | undefined;
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

  const model = useMemo(
    () => buildAlignmentGraph(subjects, edges, { lens, area, orphans }),
    [subjects, edges, lens, area, orphans],
  );

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
        style: { width: size.width, height: size.height },
        selected: n.subject.id === selected,
      };
    });

    const nextEdges: Edge[] = model.edges.map((e) => ({
      id: e.id,
      // Parent → child so layered DOWN is Value → Goal → Project → Task.
      source: e.toId,
      target: e.fromId,
      type: layout === "layered" ? "smoothstep" : "default",
      label: RELATION_LABEL[e.relation],
      selectable: false,
      markerEnd: {
        type: MarkerType.ArrowClosed,
        width: 14,
        height: 14,
        color: relationStroke(e.relation),
      },
      style: {
        stroke: relationStroke(e.relation),
        strokeWidth: e.relation === "supersedes" ? 1.6 : 1.25,
        strokeDasharray: e.relation === "supersedes" ? "5 4" : undefined,
      },
      labelStyle: { fontSize: 10, fill: "var(--muted-foreground)" },
      labelBgStyle: { fill: "var(--card)", fillOpacity: 0.92 },
      labelBgPadding: [3, 5] as [number, number],
      labelBgBorderRadius: 6,
    }));

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

  useEffect(() => {
    setNodes((nds) => {
      let changed = false;
      const next = nds.map((n) => {
        const areaColor = areaAccent(n.data.subject.area, areas);
        const selectedNow = n.id === selected;
        if (
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
          data: { ...n.data, colorByArea, areaColor },
        };
      });
      return changed ? next : nds;
    });
  }, [colorByArea, areas, selected, setNodes]);

  useEffect(() => {
    const neighbors = neighborSet(hovered, edgesRef.current);
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
        const className = dimClass(!!hovered && e.source !== hovered && e.target !== hovered);
        if (e.className === className) return e;
        changed = true;
        return { ...e, className };
      });
      return changed ? next : eds;
    });
  }, [hovered, setNodes, setRfEdges]);

  useEffect(() => {
    if (!laidOut || model.nodes.length === 0) return;
    const frame = requestAnimationFrame(() => {
      void fitView({ padding: 0.18, duration: 220 });
    });
    return () => cancelAnimationFrame(frame);
  }, [laidOut, layoutKey, fitView, model.nodes.length]);

  const onNodeClick: NodeMouseHandler<SubjectFlowNode> = useCallback(
    (_event, node) => {
      onSelect(node.id);
    },
    [onSelect],
  );

  const onNodeDoubleClick: NodeMouseHandler<SubjectFlowNode> = useCallback(
    (_event, node) => {
      void fitView({ nodes: [{ id: node.id }], padding: 0.5, duration: 280 });
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
          description="Widen the lens, include archived work, or show orphans — the graph is filtered, not missing."
        />
      </div>
    );
  }

  return (
    <ReactFlow
      className="lifeos-graph h-full"

      nodes={nodes}
      edges={rfEdges}
      onNodesChange={onNodesChange}
      onEdgesChange={onEdgesChange}
      nodeTypes={nodeTypes}
      colorMode={colorMode}
      onNodeClick={onNodeClick}
      onNodeDoubleClick={onNodeDoubleClick}
      onNodeMouseEnter={(_, node) => setHovered(node.id)}
      onNodeMouseLeave={() => setHovered(null)}
      nodesConnectable={false}
      edgesReconnectable={false}
      zoomOnDoubleClick={false}
      minZoom={0.2}
      maxZoom={1.75}
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
  subjects: SubjectListItem[];
  edges: AlignmentEdge[];
  areas: AreaRow[];
  lens: MapLens;
  orphans: OrphanFilter;
  area: string | undefined;
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
