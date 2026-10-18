import {
  BaseEdge,
  getStraightPath,
  useInternalNode,
  type EdgeProps,
  type InternalNode,
} from "@xyflow/react";
import { borderPoint, boxCenter, type Box } from "@/lib/graph-geometry";

/** Clearance between a card's edge and the line end, so arrowheads don't touch the card. */
const GAP = 4;

function nodeBox(node: InternalNode): Box {
  return {
    x: node.internals.positionAbsolute.x,
    y: node.internals.positionAbsolute.y,
    width: node.measured.width ?? node.width ?? 0,
    height: node.measured.height ?? node.height ?? 0,
  };
}

/**
 * Force-view edge: a straight line between two cards, attached where it meets
 * each card's border. Force places a child beside or above its parent, where
 * the fixed bottom → top handles would loop the line behind both cards.
 */
export function FloatingEdge({
  id,
  source,
  target,
  style,
  markerStart,
  markerEnd,
  label,
  labelStyle,
  labelShowBg,
  labelBgStyle,
  labelBgPadding,
  labelBgBorderRadius,
  interactionWidth,
}: EdgeProps) {
  const sourceNode = useInternalNode(source);
  const targetNode = useInternalNode(target);
  if (!sourceNode || !targetNode) return null;

  const from = nodeBox(sourceNode);
  const to = nodeBox(targetNode);
  const start = borderPoint(from, boxCenter(to), GAP);
  const end = borderPoint(to, boxCenter(from), GAP);
  const [path, labelX, labelY] = getStraightPath({
    sourceX: start.x,
    sourceY: start.y,
    targetX: end.x,
    targetY: end.y,
  });

  return (
    <BaseEdge
      id={id}
      path={path}
      style={style}
      markerStart={markerStart}
      markerEnd={markerEnd}
      label={label}
      labelX={labelX}
      labelY={labelY}
      labelStyle={labelStyle}
      labelShowBg={labelShowBg}
      labelBgStyle={labelBgStyle}
      labelBgPadding={labelBgPadding}
      labelBgBorderRadius={labelBgBorderRadius}
      interactionWidth={interactionWidth}
    />
  );
}
