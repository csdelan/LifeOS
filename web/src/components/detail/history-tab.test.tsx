import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import type { ReactNode } from "react";
import { beforeEach, describe, expect, it } from "vitest";
import { HistoryTab } from "@/components/detail/history-tab";
import { JournalTab } from "@/components/detail/journal-tab";
import { addDays, formatDay, todayIso } from "@/lib/dates";
import { resetStore } from "@/lib/mock/store";

function renderWith(node: ReactNode) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={client}>{node}</QueryClientProvider>);
}

beforeEach(() => resetStore());

// Agent-access plan §5.6 step 2.4. The mock's Map graph Task: Compass, an agent, started it, moved
// its due date and set its priority, and wrote in its journal; Chris renamed it.
describe("an item's history and journal say which agent wrote what", () => {
  it("lists status and field changes newest first, each field from and to, via the agent that made it", async () => {
    renderWith(<HistoryTab subjectId="task-graph" type="Task" />);
    const rows = await screen.findAllByRole("listitem");
    const today = todayIso();

    expect(rows).toHaveLength(4);
    expect(rows[0]).toHaveTextContent(`Due date ${formatDay(addDays(today, 1))} → ${formatDay(addDays(today, 3))}`);
    expect(rows[0]).toHaveTextContent("Priority set to High");
    expect(rows[0]).toHaveTextContent("via Compass");
    expect(rows[1]).toHaveTextContent("In progress");
    expect(rows[1]).toHaveTextContent("via Compass");
    expect(rows[2]).toHaveTextContent("Title Map graph view → Ship the Map graph view");
    expect(rows[2]).not.toHaveTextContent("via");
    expect(rows[3]).toHaveTextContent("Not started");
    expect(rows[3]).not.toHaveTextContent("via");
  });

  it("says via the agent on a journal entry it wrote", async () => {
    renderWith(<JournalTab subjectId="task-graph" />);
    expect(await screen.findByText(/layered layout first/)).toBeInTheDocument();
    expect(screen.getByRole("listitem")).toHaveTextContent("via Compass");
  });
});
