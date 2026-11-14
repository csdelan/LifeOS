import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { CreateActions } from "@/components/create/create-context";
import { LIST_PAGE } from "@/components/primitives/use-paged";
import { createSubject, resetStore, setStatus } from "@/lib/mock/store";
import { mockReads } from "@/lib/mock/api";
import { groupTasks } from "@/lib/task-groups";
import { saveView } from "@/lib/view-state";
import { TasksPage } from "@/pages/tasks-page";

vi.mock("@tanstack/react-router", () => ({
  useSearch: () => ({}),
  useNavigate: () => vi.fn(),
}));

vi.mock("@/components/detail/subject-detail", () => ({
  SubjectDetail: ({ subjectId }: { subjectId: string }) => <div data-testid="detail">{subjectId}</div>,
}));

vi.mock("@/lib/task-groups", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/task-groups")>();
  return { ...actual, groupTasks: vi.fn(actual.groupTasks) };
});

const VIEW = { status: "active", due: "any", scheduled: "any", area: "", parent: "", tag: "" };

beforeEach(() => {
  resetStore();
  localStorage.clear();
  vi.mocked(groupTasks).mockClear();
});

function renderPage(status = "active") {
  saveView("tasks", { ...VIEW, status });
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 0 } } });
  return render(
    <QueryClientProvider client={client}>
      <CreateActions.Provider value={{ openNew: () => {}, openCapture: () => {}, openVoice: () => {} }}>
        <TasksPage />
      </CreateActions.Provider>
    </QueryClientProvider>,
  );
}

/** The statuses the page asked the server for, in its Task reads. */
function taskReads(spy: { mock: { calls: [opts?: { type?: string; status?: string }][] } }) {
  return spy.mock.calls.filter(([opts]) => opts?.type === "Task").map(([opts]) => opts?.status ?? "every Task");
}

describe("the Tasks page reads only the Tasks its view shows (code review CR99)", () => {
  it("reads one status's Tasks for that status's view, and open ones for Not started", async () => {
    const spy = vi.spyOn(mockReads, "listSubjects");
    try {
      const done = createSubject({ type: "Task", title: "Done and dusted" });
      setStatus(done.id, "Completed");
      const view = renderPage("Completed");
      expect(await screen.findByRole("button", { name: /Done and dusted/ })).toBeInTheDocument();
      expect(taskReads(spy)).toEqual(["Completed"]);
      view.unmount();

      spy.mockClear();
      renderPage("Not started");
      await screen.findByRole("heading", { name: "Working view" });
      await vi.waitFor(() => expect(taskReads(spy)).toEqual(["open"]));
    } finally {
      spy.mockRestore();
    }
  });
});

// Hundreds of Tasks drawn: about two seconds alone, more while the whole suite runs alongside.
describe("the Tasks page with hundreds of Tasks (code review CR99)", { timeout: 20_000 }, () => {
  it("draws a page of them across its groups, and Show more draws the next", async () => {
    for (let i = 0; i < LIST_PAGE + 50; i++) createSubject({ type: "Task", title: `Bulk task ${String(i).padStart(3, "0")}` });
    const user = userEvent.setup();
    renderPage("all");

    await screen.findAllByRole("button", { name: /^Bulk task/ });
    const before = screen.getAllByRole("button", { name: /^Bulk task/ }).length;
    expect(before).toBeLessThan(LIST_PAGE + 50);
    const more = screen.getByRole("button", { name: /^Show \d+ more$/ });

    await user.click(more);
    expect(screen.getAllByRole("button", { name: /^Bulk task/ })).toHaveLength(LIST_PAGE + 50);
    expect(screen.queryByRole("button", { name: /^Show \d+ more$/ })).not.toBeInTheDocument();
  });

  it("doesn't group the list again while a new Task's title is typed", async () => {
    for (let i = 0; i < 50; i++) createSubject({ type: "Task", title: `Bulk task ${i}` });
    const user = userEvent.setup();
    renderPage("active");
    await screen.findAllByRole("button", { name: /^Bulk task/ });
    const grouped = vi.mocked(groupTasks).mock.calls.length;

    await user.type(screen.getByRole("textbox", { name: "New task title" }), "Water the plants");

    expect(screen.getByRole("textbox", { name: "New task title" })).toHaveValue("Water the plants");
    expect(vi.mocked(groupTasks).mock.calls.length).toBe(grouped);
  });
});
