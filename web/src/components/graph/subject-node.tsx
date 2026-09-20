import { Handle, Position, type Node, type NodeProps } from "@xyflow/react";
import { StatusPill } from "@/components/primitives/status-pill";
import { TypeBadge } from "@/components/primitives/type-badge";
import { defaultStatus } from "@/lib/production-ui-types";
import type { SubjectListItem } from "@/lib/production-ui-types";
import { typeAccent } from "@/lib/subject-meta";
import { cn } from "@/lib/utils";

export type SubjectNodeData = {
  subject: SubjectListItem;
  degree: number;
  colorByArea: boolean;
  areaColor: string;
};

export type SubjectFlowNode = Node<SubjectNodeData, "subject">;

export function SubjectGraphNode({ data, selected }: NodeProps<SubjectFlowNode>) {
  const { subject, colorByArea, areaColor } = data;
  const accent = colorByArea ? areaColor : typeAccent(subject.type);
  const status = subject.status || defaultStatus(subject.type) || null;

  return (
    <div
      className={cn(
        "flex h-full w-full flex-col justify-center gap-1 rounded-xl bg-card px-3 py-2 shadow-(--shadow-lift) ring-1 ring-foreground/10",
        selected && "ring-2 ring-primary/50",
      )}
      style={{ borderLeft: `3px solid ${accent}` }}
    >
      <Handle
        type="target"
        position={Position.Top}
        isConnectable={false}
        className="!size-1.5 !border-0 !bg-border"
      />
      <div className="flex items-center justify-between gap-2">
        <TypeBadge type={subject.type} className="min-w-0 truncate" />
        <StatusPill status={status} />
      </div>
      <p className="truncate text-sm font-medium leading-tight">{subject.title}</p>
      <Handle
        type="source"
        position={Position.Bottom}
        isConnectable={false}
        className="!size-1.5 !border-0 !bg-border"
      />
    </div>
  );
}
