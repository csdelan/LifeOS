import type { CSSProperties } from "react";
import { Handle, Position, type Node, type NodeProps } from "@xyflow/react";
import { NodeBackdrop } from "@/components/graph/node-backdrop";
import { StatusPill } from "@/components/primitives/status-pill";
import { TypeBadge } from "@/components/primitives/type-badge";
import { TypeIcon } from "@/components/primitives/type-icon";
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
      className={cn("lifeos-node", selected && "is-selected")}
      data-type={subject.type}
      style={{ "--node-accent": accent } as CSSProperties}
    >
      <Handle
        type="target"
        position={Position.Top}
        isConnectable={false}
        className="!size-1.5 !border-0 !bg-border"
      />
      <div className="lifeos-node__shell">
        <div className="lifeos-node__art">
          <NodeBackdrop type={subject.type} />
        </div>
        <div className="lifeos-node__body">
          <div className="flex items-center justify-between gap-2">
            <span className="flex min-w-0 items-center gap-1.5">
              <TypeIcon type={subject.type} className="size-3.5" />
              <TypeBadge type={subject.type} withGlyph={false} className="min-w-0 truncate" />
            </span>
            <StatusPill status={status} />
          </div>
          <p className="truncate text-sm font-medium leading-tight">{subject.title}</p>
        </div>
      </div>
      <Handle
        type="source"
        position={Position.Bottom}
        isConnectable={false}
        className="!size-1.5 !border-0 !bg-border"
      />
    </div>
  );
}
