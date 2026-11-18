import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ReactNode } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { HISTORY_PAGE, HistoryTab } from "@/components/detail/history-tab";
import { JOURNAL_PAGE, JournalTab } from "@/components/detail/journal-tab";
import { RepeatPanel } from "@/components/tasks/repeat-panel";
import { RunningTimer } from "@/components/timer/running-timer";
import { mockReads } from "@/lib/mock/api";
import { getState, mockTaskSeriesHistory, resetStore } from "@/lib/mock/store";
import type { TaskRepeat } from "@/lib/production-ui-types";

vi.mock("@tanstack/react-router", () => ({
  Link: ({ children }: { children: ReactNode }) => <a href="/tasks">{children}</a>,
}));

function renderWith(node: ReactNode) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 0 } } });
  return render(<QueryClientProvider client={client}>{node}</QueryClientProvider>);
}

const at = (i: number) => new Date(Date.UTC(2026, 0, 1, 0, i)).toISOString();

beforeEach(() => resetStore());
afterEach(() => vi.restoreAllMocks());

// A failed read said "No journal yet" or "No status history", now "No history yet" (code review CR131).
describe("an item's journal and status history when the read fails", () => {
  it("says the journal failed to load, not that it's empty", async () => {
    vi.spyOn(mockReads, "journal").mockRejectedValue(new Error("offline"));
    renderWith(<JournalTab subjectId="goal-lifeos" />);

    expect(await screen.findByText("The journal failed to load.")).toBeInTheDocument();
    expect(screen.queryByText("No journal yet")).not.toBeInTheDocument();
  });

  it("says the history failed to load, not that there is none", async () => {
    vi.spyOn(mockReads, "history").mockRejectedValue(new Error("offline"));
    renderWith(<HistoryTab subjectId="goal-lifeos" />);

    expect(await screen.findByText("The history failed to load.")).toBeInTheDocument();
    expect(screen.queryByText("No history yet")).not.toBeInTheDocument();
  });
});

// The detail read every journal entry and status change, and a series' every repetition, to show
// the newest few (code review CR121): each now reads a page, and Show older or Show more the next.
describe("an item's journal and status history, a page at a time", () => {
  it("reads the newest journal entries, newest first, and Show older the ones before", async () => {
    const id = "goal-lifeos";
    getState().journals[id] = Array.from({ length: JOURNAL_PAGE + 5 }, (_, i) => ({
      eventId: `j${i}`,
      occurredAt: at(i),
      content: `Entry ${i}`,
    }));
    const journal = vi.spyOn(mockReads, "journal");
    const user = userEvent.setup();
    renderWith(<JournalTab subjectId={id} />);

    expect(await screen.findByText(`Entry ${JOURNAL_PAGE + 4}`)).toBeInTheDocument();
    expect(screen.getAllByRole("listitem")).toHaveLength(JOURNAL_PAGE);
    expect(screen.getAllByRole("listitem")[0]).toHaveTextContent(`Entry ${JOURNAL_PAGE + 4}`);
    expect(screen.queryByText("Entry 4")).not.toBeInTheDocument();
    expect(journal).toHaveBeenLastCalledWith(id, JOURNAL_PAGE);

    await user.click(screen.getByRole("button", { name: "Show older" }));
    expect(await screen.findByText("Entry 0")).toBeInTheDocument();
    expect(screen.getAllByRole("listitem")).toHaveLength(JOURNAL_PAGE + 5);
    expect(journal).toHaveBeenLastCalledWith(id, 2 * JOURNAL_PAGE);
    expect(screen.queryByRole("button", { name: "Show older" })).not.toBeInTheDocument();
  });

  it("reads the newest status changes, and Show older the ones before", async () => {
    const id = "goal-lifeos";
    getState().history[id] = Array.from({ length: HISTORY_PAGE + 1 }, (_, i) => ({
      id: `h${i}`,
      status: i === HISTORY_PAGE ? "Not started" : "Active",
      occurredAt: at(HISTORY_PAGE - i),
    }));
    const history = vi.spyOn(mockReads, "history");
    const user = userEvent.setup();
    renderWith(<HistoryTab subjectId={id} />);

    await waitFor(() => expect(screen.getAllByRole("listitem")).toHaveLength(HISTORY_PAGE));
    expect(screen.queryByText("Not started")).not.toBeInTheDocument();
    expect(history).toHaveBeenLastCalledWith(id, HISTORY_PAGE);

    await user.click(screen.getByRole("button", { name: "Show older" }));
    expect(await screen.findByText("Not started")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Show older" })).not.toBeInTheDocument();
  });

  it("reads a series' newest repetitions, and Show all the rest", async () => {
    const history = mockTaskSeriesHistory("task-trash")!;
    const series = vi.spyOn(mockReads, "taskSeriesHistory");
    const repeating: TaskRepeat = {
      ...history.series,
      isSeries: true,
      seriesId: "task-trash",
      seriesUrn: history.series.urn,
      seriesTitle: history.series.title,
      seriesStatus: history.series.status ?? null,
      occurrence: null,
    };
    const user = userEvent.setup();
    renderWith(<RepeatPanel subjectId="task-trash" repeating={repeating} />);

    const list = await screen.findByRole("list", { name: "Repetitions" });
    expect(series).toHaveBeenLastCalledWith("task-trash", 6);
    expect(within(list).getAllByRole("listitem").length).toBeLessThanOrEqual(6);
    if (history.repetitionCount > 6) {
      await user.click(screen.getByRole("button", { name: `Show all ${history.repetitionCount}` }));
      await waitFor(() => expect(within(list).getAllByRole("listitem")).toHaveLength(history.repetitionCount));
      expect(screen.getByRole("button", { name: "Show fewer" })).toBeInTheDocument();
    }
  });
});

describe("the shell's running timer", () => {
  it("reads the running and paused timers, not every timer ever", async () => {
    const timers = vi.spyOn(mockReads, "timers");
    renderWith(<RunningTimer />);
    await waitFor(() => expect(timers).toHaveBeenCalled());
    expect(timers.mock.calls.every(([opts]) => opts?.active === true)).toBe(true);
  });
});
