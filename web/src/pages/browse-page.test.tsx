import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { toast } from "sonner";
import { CreateActions, type CreateOpts } from "@/components/create/create-context";
import { createWriteClient } from "@/lib/client";
import { resetStore } from "@/lib/mock/store";
import { mockReads } from "@/lib/mock/api";
import type { SubjectListItem, SubjectType } from "@/lib/production-ui-types";
import { BrowsePage } from "@/pages/browse-page";

const router = vi.hoisted(() => {
  let search: Record<string, unknown> = {};
  const listeners = new Set<() => void>();
  return {
    get: () => search,
    set: (next: Record<string, unknown>) => {
      search = next;
      listeners.forEach((l) => l());
    },
    subscribe: (l: () => void) => {
      listeners.add(l);
      return () => {
        listeners.delete(l);
      };
    },
  };
});

const navigate = vi.fn(
  (opts: { search: Record<string, unknown> | ((prev: Record<string, unknown>) => Record<string, unknown>) }) => {
    router.set(typeof opts.search === "function" ? opts.search(router.get()) : opts.search);
  },
);

vi.mock("@tanstack/react-router", async () => {
  const { useSyncExternalStore } = await import("react");
  return {
    useSearch: () => useSyncExternalStore(router.subscribe, router.get),
    useNavigate: () => navigate,
  };
});

vi.mock("@/components/detail/subject-detail", () => ({
  SubjectDetail: ({ subjectId }: { subjectId: string }) => <div data-testid="detail">{subjectId}</div>,
}));

vi.mock("sonner", () => ({
  toast: Object.assign(vi.fn(), { success: vi.fn(), error: vi.fn(), message: vi.fn() }),
}));

const openNew = vi.fn<(opts?: CreateOpts) => void>();

beforeEach(() => {
  resetStore();
  localStorage.clear();
  navigate.mockClear();
  openNew.mockClear();
  vi.mocked(toast.success).mockClear();
  router.set({});
});

function renderPage(search: Record<string, unknown> = {}) {
  router.set(search);
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 0 } } });
  const view = render(
    <QueryClientProvider client={client}>
      <CreateActions.Provider value={{ openNew, openCapture: () => {}, openVoice: () => {} }}>
        <BrowsePage />
      </CreateActions.Provider>
    </QueryClientProvider>,
  );
  return { ...view, client };
}

async function listOf(name: string) {
  return screen.findByRole("list", { name: `${name} list` });
}

function rowTitles(list: HTMLElement) {
  return within(list)
    .getAllByRole("button")
    .map((b) => b.querySelector(".font-medium")?.textContent);
}

const typeButton = (name: RegExp) =>
  within(screen.getByRole("complementary", { name: "Browse filters" })).getByRole("button", { name });

describe("Browse page (BROWSE-2)", () => {
  it("lists every type with its count and the items of the type picked", async () => {
    renderPage({ type: "Decision" });
    const list = await listOf("Decision");
    expect(rowTitles(list)).toEqual(["Blazor for Production UI", "React + Vite for Production UI"]);
    expect(screen.getByText("2 items")).toBeInTheDocument();
    expect(typeButton(/^Decision\s*2$/)).toHaveAttribute("aria-current", "true");
    expect(typeButton(/^All types\s*\d+$/)).not.toHaveAttribute("aria-current");
    // Creatable types are listed even at zero, to pick and create.
    expect(typeButton(/^Area\s*0$/)).toBeInTheDocument();
  });

  it("combines filters: OR within Status, AND with the text box", async () => {
    const user = userEvent.setup();
    renderPage({ type: "Decision" });
    const list = await listOf("Decision");

    await user.click(screen.getByRole("checkbox", { name: "Implementing" }));
    expect(rowTitles(list)).toEqual(["React + Vite for Production UI"]);
    expect(screen.getByText("1 of 2 shown")).toBeInTheDocument();

    await user.click(screen.getByRole("checkbox", { name: "Closed" }));
    expect(rowTitles(list)).toEqual(["Blazor for Production UI", "React + Vite for Production UI"]);

    await user.type(screen.getByRole("searchbox", { name: "Filter by title or URN" }), "blazor");
    expect(rowTitles(list)).toEqual(["Blazor for Production UI"]);

    await user.click(screen.getByRole("button", { name: "Clear filters" }));
    expect(screen.getByRole("checkbox", { name: "Implementing" })).not.toBeChecked();
    // The pane clears its own filters; the text box keeps what was typed.
    expect(rowTitles(list)).toEqual(["Blazor for Production UI"]);
  });

  it("shows the columns that fit what's looked at, and sorts by a column header", async () => {
    const user = userEvent.setup();
    renderPage({ type: "Decision" });
    const list = await listOf("Decision");
    expect(screen.getByRole("button", { name: "Sort by Status" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Sort by Due" })).not.toBeInTheDocument();

    // Status sorts in the Decision's own order: Implementing comes before Closed.
    await user.click(screen.getByRole("button", { name: "Sort by Status" }));
    expect(rowTitles(list)).toEqual(["React + Vite for Production UI", "Blazor for Production UI"]);
    await user.click(screen.getByRole("button", { name: "Status, sorted ascending" }));
    expect(rowTitles(list)).toEqual(["Blazor for Production UI", "React + Vite for Production UI"]);

    await user.click(typeButton(/^Task\s*\d+$/));
    await listOf("Task");
    expect(screen.getByRole("button", { name: "Sort by Due" })).toBeInTheDocument();
  });

  it("opens an item, and keeps it open only for a type it belongs to", async () => {
    const user = userEvent.setup();
    renderPage({ type: "Decision" });
    const list = await listOf("Decision");
    await user.click(within(list).getByRole("button", { name: /Blazor for Production UI/ }));
    expect(router.get()).toEqual({ type: "Decision", selected: "dec-blazor" });
    expect(await screen.findByTestId("detail")).toHaveTextContent("dec-blazor");

    await user.click(typeButton(/^All types/));
    expect(router.get()).toEqual({ type: "all", selected: "dec-blazor" });
    await user.click(typeButton(/^Task\s*\d+$/));
    expect(router.get()).toEqual({ type: "Task", selected: undefined });
  });

  it("says when the filters hide the open item, and Show it brings it back", async () => {
    const user = userEvent.setup();
    renderPage({ type: "Decision", selected: "dec-blazor" });
    const list = await listOf("Decision");
    await user.click(screen.getByRole("checkbox", { name: "Implementing" }));
    expect(
      screen.getByText("“Blazor for Production UI” is open, but the filters hide it in the list."),
    ).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Show it" }));
    expect(rowTitles(list)).toContain("Blazor for Production UI");
    expect(screen.queryByText(/the filters hide it/)).not.toBeInTheDocument();
  });

  it("makes a new item of the type, selects it when the filters show it, and says so when they don't", async () => {
    const user = userEvent.setup();
    const { client } = renderPage({ type: "Decision" });
    await listOf("Decision");

    await user.click(screen.getByRole("button", { name: "New Decision" }));
    expect(openNew).toHaveBeenCalledWith(expect.objectContaining({ type: "Decision" }));
    const onCreated = openNew.mock.calls[0][0]!.onCreated!;

    const writes = createWriteClient();
    await act(async () => {
      const made = await writes.newSubject({ type: "Decision", title: "Use SQLite for local storage", attrs: {} });
      await client.invalidateQueries();
      onCreated(made);
    });
    await vi.waitFor(() => expect(router.get().selected).toMatch(/.+/));
    const sqlite = router.get().selected;
    expect(screen.getByRole("list", { name: "Decision list" })).toHaveTextContent("Use SQLite for local storage");

    // Only Closed decisions shown: the next one is made but stays out of the list.
    await user.click(screen.getByRole("checkbox", { name: "Closed" }));
    await act(async () => {
      const made = await writes.newSubject({ type: "Decision", title: "Keep the kernel in C#", attrs: {} });
      await client.invalidateQueries();
      onCreated(made);
    });
    await vi.waitFor(() =>
      expect(toast.success).toHaveBeenCalledWith(
        "Created Decision “Keep the kernel in C#”. The current filters hide it.",
      ),
    );
    expect(router.get().selected).toBe(sqlite);
  });

  it("remembers the type, the open item and the filters for next time", async () => {
    const user = userEvent.setup();
    const first = renderPage({ type: "Decision", selected: "dec-stack" });
    await listOf("Decision");
    await user.click(screen.getByRole("checkbox", { name: "Implementing" }));
    first.unmount();

    renderPage({});
    await listOf("Decision");
    expect(router.get()).toEqual({ type: "Decision", selected: "dec-stack" });
    expect(screen.getByRole("checkbox", { name: "Implementing" })).toBeChecked();
  });
});

describe("Browse page with the kernel's Spaces in the list", () => {
  it("still shows All types, leaving the Spaces to the Environment page", async () => {
    const list = mockReads.listSubjects.bind(mockReads);
    const kitchen: SubjectListItem = {
      id: "space-kitchen",
      urn: "urn:bsk:space:kitchen-5a1b2c",
      type: "Space" as SubjectType,
      title: "Kitchen",
      status: null,
      archived: false,
      createdAt: "2026-10-20T12:00:00.000Z",
    };
    const spy = vi.spyOn(mockReads, "listSubjects").mockImplementation(async (opts) => [...(await list(opts)), kitchen]);
    try {
      renderPage({ type: "all" });
      const all = await listOf("Everything");
      const shown = rowTitles(all);
      expect(shown.length).toBeGreaterThan(0);
      expect(shown).not.toContain("Kitchen");
      // The count leaves it out too, so it matches the list.
      expect(typeButton(/^All types\s*\d+$/)).toHaveTextContent(new RegExp(`^All types\\s*${shown.length}$`));
    } finally {
      spy.mockRestore();
    }
  });
});
