/** Geometry for the Force view's straight edges (graph cards are axis-aligned boxes). */

export interface Point {
  x: number;
  y: number;
}

export interface Box {
  x: number;
  y: number;
  width: number;
  height: number;
}

export function boxCenter(box: Box): Point {
  return { x: box.x + box.width / 2, y: box.y + box.height / 2 };
}

/**
 * Where the line from `box`'s centre toward `toward` leaves the box, pushed
 * `gap` px further out so an arrowhead sits just off the card's edge.
 */
export function borderPoint(box: Box, toward: Point, gap = 0): Point {
  const c = boxCenter(box);
  const dx = toward.x - c.x;
  const dy = toward.y - c.y;
  if (dx === 0 && dy === 0) return c;
  const halfW = box.width / 2 + gap;
  const halfH = box.height / 2 + gap;
  const scale = Math.min(
    dx === 0 ? Infinity : halfW / Math.abs(dx),
    dy === 0 ? Infinity : halfH / Math.abs(dy),
  );
  return { x: c.x + dx * scale, y: c.y + dy * scale };
}
