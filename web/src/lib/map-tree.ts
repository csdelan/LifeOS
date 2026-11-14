/** Finding things in the Map's outline: a node, the trail above it, and every node under some. */
import type { AlignmentNode } from "@/lib/derive";

export function findNode(nodes: AlignmentNode[], id: string): AlignmentNode | null {
  for (const n of nodes) {
    if (n.id === id) return n;
    const c = findNode(n.children, id);
    if (c) return c;
  }
  return null;
}

/** The ids from the top of the outline down to the node's parent, or null when it isn't there. */
export function ancestorIds(nodes: AlignmentNode[], id: string, trail: string[] = []): string[] | null {
  for (const n of nodes) {
    if (n.id === id) return trail;
    const found = ancestorIds(n.children, id, [...trail, n.id]);
    if (found) return found;
  }
  return null;
}

export function collectNodeIds(nodes: AlignmentNode[], into: Set<string> = new Set()): Set<string> {
  for (const n of nodes) {
    into.add(n.id);
    collectNodeIds(n.children, into);
  }
  return into;
}
