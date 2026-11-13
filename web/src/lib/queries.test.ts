import { QueryClient } from "@tanstack/react-query";
import { describe, expect, it } from "vitest";
import type { LifeOsWriteClient } from "@/lib/production-ui-types";
import { decorateWrites, qk } from "@/lib/queries";

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
  ["forest", false, "areas"],
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
