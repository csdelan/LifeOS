import type { CSSProperties } from "react";
import { Handle, Position, type Node, type NodeProps } from "@xyflow/react";
import { CoverImage } from "@/components/cover/cover-image";
import { NodeBackdrop } from "@/components/graph/node-backdrop";
import { StatusPill } from "@/components/primitives/status-pill";
import { TypeBadge } from "@/components/primitives/type-badge";
import { TypeIcon } from "@/components/primitives/type-icon";
import { defaultStatus } from "@/lib/production-ui-types";
import type { SubjectListItem } from "@/lib/production-ui-types";
import { creditText } from "@/lib/cover";
import { typeAccent } from "@/lib/subject-meta";
import { cn } from "@/lib/utils";

export type SubjectNodeData = {
  subject: SubjectListItem;
  degree: number;
  colorByArea: boolean;
  areaColor: string;
};

export type SubjectFlowNode = Node<SubjectNodeData, "subject">;

/** The width a node's cover is asked for: nodes are at most 292 px wide. */
const NODE_COVER_WIDTH = 300;

export function SubjectGraphNode({ data, selected }: NodeProps<SubjectFlowNode>) {
  const { subject, colorByArea, areaColor } = data;
  const accent = colorByArea ? areaColor : typeAccent(subject.type);
  const status = subject.status || defaultStatus(subject.type) || null;
  const cover = subject.cover ?? null;

  return (
    <div
      className={cn("lifeos-node", selected && "is-selected", cover && "has-cover")}
      data-type={subject.type}
      style={{ "--node-accent": accent } as CSSProperties}
    >
      <Handle
        type="target"
        position={Position.Top}
        isConnectable={false}
        className="!size-1.5 !border-0 !bg-border"
      />
      {/* The art slot ignores the pointer, so the photo's credit is the node's tooltip. */}
      <div className="lifeos-node__shell" title={cover ? creditText(cover) : undefined}>
        <div className="lifeos-node__art">
          {cover ? (
            // The cover in place of the type's drawing (COVER-3), under a dark scrim: the
            // node's words turn white, at 4.5:1 or more over any photo.
            <>
              <CoverImage
                url={cover.url}
                color={cover.color}
                positionY={cover.positionY}
                width={NODE_COVER_WIDTH}
                className="absolute inset-0"
              />
              <div className="lifeos-node__scrim" />
            </>
          ) : (
            <NodeBackdrop type={subject.type} />
          )}
        </div>
        <div className="lifeos-node__body">
          <div className="flex items-center justify-between gap-2">
            <span className="flex min-w-0 items-center gap-1.5">
              <TypeIcon type={subject.type} className={cn("size-3.5", cover && "text-white")} />
              <TypeBadge
                type={subject.type}
                withGlyph={false}
                className={cn("min-w-0 truncate", cover && "text-white")}
              />
            </span>
            <StatusPill status={status} className={cover ? "bg-black/55 text-white" : undefined} />
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
