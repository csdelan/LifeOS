import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { renderHook, waitFor } from "@testing-library/react";
import { createElement, type ReactNode } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { mockReads } from "@/lib/mock/api";
import type { LifeOsWriteClient } from "@/lib/production-ui-types";
import { decorateWrites, qk, useAlignmentEdges, useAlignmentForest, useSubjects } from "@/lib/queries";

afterEach(() => {
  vi.restoreAllMocks();
});

// Code review CR36: a write refreshes the named groups of reads it moves.

/** A write client whose writes all succeed and do nothing. */
const idle = new Proxy({} as LifeOsWriteClient, {
  get: () => async () => undefined,
});

/** Seeds a cached read for each key, runs the write, and says which reads it left stale. */
async function staleAfter(write: (writes: LifeOsWriteClient) => Promise<unknown>, keys: readonly (readonly unknown[])[]) {
  const qc = new QueryClient();
  for (const key of keys) qc.setQueryData(key, []);
  await write(decorateWrites(idle, qc));
  return keys.filter((key) => qc.getQueryState(key)?.isInvalidated).map((key) => key.join("/"));
}

const archivedListKeys = [
  qk.subjects,
  qk.dashboard,
  ["edges", false],
  qk.subject("s1"),
  qk.listItem("s1"),
  qk.history("s1"),
  qk.inbox,
  qk.habits,
  qk.occurrences,
  qk.people,
  qk.rewards,
  qk.taskSeries,
  qk.timers,
  qk.spaces,
  qk.metrics,
  qk.domains,
];

describe("write invalidation", () => {
  it("archiving a subject refreshes every list that leaves archived ones out, Habits among them", async () => {
    const stale = await staleAfter((w) => w.archive("s1"), archivedListKeys);
    expect(stale).toEqual(archivedListKeys.map((key) => key.join("/")));
  });

  it("restoring a subject refreshes the same reads as archiving it, the Inbox among them", async () => {
    const stale = await staleAfter((w) => w.restore("s1"), archivedListKeys);
    expect(stale).toEqual(archivedListKeys.map((key) => key.join("/")));
  });

  it("an attribute change refreshes the subject's history, since it leaves an event", async () => {
    const stale = await staleAfter((w) => w.setAttributes("s1", {}), [qk.history("s1"), qk.history("s2")]);
    expect(stale).toEqual(["history/s1"]);
  });

  it("a write leaves the reads it doesn't move alone", async () => {
    const stale = await staleAfter((w) => w.appendJournal("s1", "text"), [qk.journal("s1"), qk.habits, qk.wallet]);
    expect(stale).toEqual(["journal/s1"]);
  });
});

// Code review CR37: the Map's outline is built from the reads its graph caches anyway.
describe("the Map's reads", () => {
  it("ask for the subject list and the edges once each", async () => {
    const subjects = vi.spyOn(mockReads, "listSubjects");
    const edges = vi.spyOn(mockReads, "edges");
    const client = new QueryClient();
    const wrapper = ({ children }: { children: ReactNode }) => createElement(QueryClientProvider, { client }, children);
    const { result } = renderHook(
      () => ({
        forest: useAlignmentForest(false, "all"),
        subjects: useSubjects(undefined, false, true),
        edges: useAlignmentEdges(false),
      }),
      { wrapper },
    );

    await waitFor(() => expect(result.current.forest.data?.length).toBeGreaterThan(0));
    expect(subjects).toHaveBeenCalledTimes(1);
    expect(edges).toHaveBeenCalledTimes(1);
  });

  it("a subject list that isn't enabled isn't read, as the palette's while it's closed", async () => {
    const subjects = vi.spyOn(mockReads, "listSubjects");
    const client = new QueryClient();
    const wrapper = ({ children }: { children: ReactNode }) => createElement(QueryClientProvider, { client }, children);
    const { result } = renderHook(() => useSubjects(undefined, true, false, { enabled: false }), { wrapper });

    expect(result.current.fetchStatus).toBe("idle");
    expect(subjects).not.toHaveBeenCalled();
  });
});
