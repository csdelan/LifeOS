import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { toast } from "sonner";
import { CreateActions, type CreateOpts } from "@/components/create/create-context";
import { createWriteClient } from "@/lib/client";
import { createSubject, resetStore } from "@/lib/mock/store";
import { mockReads } from "@/lib/mock/api";
import type { SubjectType } from "@/lib/production-ui-types";
import { LIST_PAGE } from "@/components/primitives/use-paged";
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

/** The list's rows once the server's answer to the last change has come: it's read, not folded here. */
async function rowsBecome(list: HTMLElement, titles: (string | undefined)[]) {
  await waitFor(() => expect(rowTitles(list)).toEqual(titles));
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
    await rowsBecome(list, ["React + Vite for Production UI"]);
    expect(screen.getByText("1 of 2 shown")).toBeInTheDocument();

    await user.click(screen.getByRole("checkbox", { name: "Closed" }));
    await rowsBecome(list, ["Blazor for Production UI", "React + Vite for Production UI"]);

    await user.type(screen.getByRole("searchbox", { name: "Filter by title or URN" }), "blazor");
    await rowsBecome(list, ["Blazor for Production UI"]);

    await user.click(screen.getByRole("button", { name: "Clear filters" }));
    expect(screen.getByRole("checkbox", { name: "Implementing" })).not.toBeChecked();
    // The pane clears its own filters; the text box keeps what was typed.
    await rowsBecome(list, ["Blazor for Production UI"]);
  });

  it("shows the columns that fit what's looked at, and sorts by a column header", async () => {
    const user = userEvent.setup();
    renderPage({ type: "Decision" });
    const list = await listOf("Decision");
    expect(screen.getByRole("button", { name: "Sort by Status" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Sort by Due" })).not.toBeInTheDocument();

    // Status sorts in the Decision's own order: Implementing comes before Closed.
    await user.click(screen.getByRole("button", { name: "Sort by Status" }));
    await rowsBecome(list, ["React + Vite for Production UI", "Blazor for Production UI"]);
    await user.click(screen.getByRole("button", { name: "Status, sorted ascending" }));
    await rowsBecome(list, ["Blazor for Production UI", "React + Vite for Production UI"]);

    await user.click(typeButton(/^Task\s*\d+$/));
    await listOf("Task");
    expect(screen.getByRole("button", { name: "Sort by Due" })).toBeInTheDocument();
  });

  it("sorts by Tags and, across all types, by Type from their headers", async () => {
    const user = userEvent.setup();
    renderPage({ type: "Value" });
    const values = await listOf("Identity Statement");
    await user.click(screen.getByRole("button", { name: "Sort by Tags" }));
    expect(screen.getByRole("button", { name: "Tags, sorted ascending" })).toBeInTheDocument();
    // By the tags as the column shows them: #craft…, #family…, #health…, #identity…, #trading…
    await waitFor(() =>
      expect(rowTitles(values).slice(0, 5)).toEqual([
        "Craftsman of systems",
        "Present with my family",
        "Steward of my health",
        "I tell myself the truth",
        "I take only defined risk",
      ]),
    );

    await user.click(typeButton(/^All types/));
    const all = await listOf("Everything");
    await user.click(screen.getByRole("button", { name: "Sort by Type" }));
    // Identity Statements first, as in the type list; the last rows are a later type.
    await waitFor(() => {
      const rows = within(all).getAllByRole("button");
      expect(rows[0]).toHaveTextContent("Identity Statement");
      expect(rows.at(-1)).not.toHaveTextContent("Identity Statement");
    });
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
    await waitFor(() => expect(rowTitles(list)).toContain("Blazor for Production UI"));
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

  it("offers New only where it makes something: the type looked at, or anything from All types", async () => {
    const user = userEvent.setup();
    const header = () => screen.getByRole("heading", { level: 1 }).closest("header")!;
    renderPage({ type: "Constraint" });
    await listOf("Constraint");
    // New can't make a Constraint (nor a Season or Reward), so there's no plain New to mislead.
    expect(within(header()).queryByRole("button", { name: /^New/ })).not.toBeInTheDocument();

    await user.click(typeButton(/^Reward\s*\d+$/));
    await listOf("Reward");
    expect(within(header()).queryByRole("button", { name: /^New/ })).not.toBeInTheDocument();

    await user.click(typeButton(/^All types/));
    await listOf("Everything");
    await user.click(within(header()).getByRole("button", { name: "New" }));
    expect(openNew).toHaveBeenCalledWith(expect.objectContaining({ type: undefined }));
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

// Each makes hundreds of subjects and draws two hundred rows: about two seconds alone, and more
// than the default five while the whole suite runs alongside.
describe("Browse page with thousands of items (code review CR76, CR120)", { timeout: 20_000 }, () => {
  it("reads the first two hundred rows, and Show more reads the next", async () => {
    let last = "";
    for (let i = 0; i < 230; i++) last = createSubject({ type: "Task", title: `Repetition ${String(i).padStart(3, "0")}` }).id;
    const tasks = (await mockReads.listSubjects({ type: "Task" })).length;
    const browse = vi.spyOn(mockReads, "browse");
    const user = userEvent.setup();
    renderPage({ type: "Task", selected: last });

    // The server sends a page: Repetition 229 sorts past it, open in the panel but not drawn.
    const list = await listOf("Task");
    expect(within(list).getAllByRole("listitem")).toHaveLength(LIST_PAGE);
    expect(browse).toHaveBeenLastCalledWith(expect.objectContaining({ type: "Task", limit: LIST_PAGE }));
    expect(screen.getByText(`${LIST_PAGE} of ${tasks} shown`)).toBeInTheDocument();
    expect(screen.getByTestId("detail")).toHaveTextContent(last);

    await user.click(screen.getByRole("button", { name: /^Show \d+ more$/ }));

    await waitFor(() => expect(within(list).getAllByRole("listitem")).toHaveLength(tasks));
    expect(browse).toHaveBeenLastCalledWith(expect.objectContaining({ limit: 2 * LIST_PAGE }));
    expect(within(list).getByRole("button", { name: /Repetition 229/ })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^Show \d+ more$/ })).not.toBeInTheDocument();
    browse.mockRestore();
  });

  it("starts again from the first page when the filters change", async () => {
    for (let i = 0; i < 450; i++) createSubject({ type: "Task", title: `Repetition ${i}` });
    const user = userEvent.setup();
    renderPage({ type: "Task" });
    const list = await listOf("Task");
    await user.click(screen.getByRole("button", { name: /^Show \d+ more$/ }));
    await waitFor(() => expect(within(list).getAllByRole("listitem")).toHaveLength(2 * LIST_PAGE));

    await user.type(screen.getByRole("searchbox"), "Repetition");

    await waitFor(() => expect(within(list).getAllByRole("listitem")).toHaveLength(LIST_PAGE));
  });
});

describe("Browse page with the kernel's Spaces among the items", () => {
  it("still shows All types, leaving the Spaces to the Environment page", async () => {
    createSubject({ type: "Space" as SubjectType, title: "Kitchen" });
    renderPage({ type: "all" });
    const all = await listOf("Everything");
    const shown = rowTitles(all);
    expect(shown.length).toBeGreaterThan(0);
    expect(shown).not.toContain("Kitchen");
    // The count leaves it out too, so it matches the list.
    expect(typeButton(/^All types\s*\d+$/)).toHaveTextContent(new RegExp(`^All types\\s*${shown.length}$`));
  });
});
