import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { SubjectDetail } from "@/components/detail/subject-detail";
import { CreateActions, type CreateOpts } from "@/components/create/create-context";
import { TooltipProvider } from "@/components/ui/tooltip";
import { getState, mockTaskSeriesHistory, resetStore } from "@/lib/mock/store";
import { tagUniverse } from "@/lib/mock/seed";

vi.mock("sonner", () => ({
  toast: Object.assign(vi.fn(), {
    success: vi.fn(),
    error: vi.fn(),
    message: vi.fn(),
  }),
}));

function renderDetail(
  id: string,
  opts: { openNew?: (opts?: CreateOpts) => void; onOpenSubject?: (id: string) => void } = {},
) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false, staleTime: 0 } },
  });
  return render(
    <QueryClientProvider client={client}>
      <TooltipProvider>
        <CreateActions.Provider
          value={{ openNew: opts.openNew ?? (() => {}), openCapture: () => {}, openVoice: () => {} }}
        >
          <SubjectDetail subjectId={id} onOpenSubject={opts.onOpenSubject} />
        </CreateActions.Provider>
      </TooltipProvider>
    </QueryClientProvider>,
  );
}

describe("SubjectDetail edit session", () => {
  beforeEach(() => {
    resetStore();
  });

  it("starts read-only, enables Save only when dirty, and Cancel reverts", async () => {
    expect(getState().details["goal-lifeos"]?.title).toMatch(/Ship LifeOS/);
    const user = userEvent.setup();
    renderDetail("goal-lifeos");
    expect(
      await screen.findByRole("heading", { name: /Ship LifeOS production UI/i }, { timeout: 4000 }),
    ).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Save" })).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Edit" }));
    const save = screen.getByRole("button", { name: "Save" });
    expect(save).toBeDisabled();

    const description = screen.getByLabelText("Description");
    await user.clear(description);
    await user.type(description, "Hardened for every interface.");
    await waitFor(() => expect(save).toBeEnabled());

    await user.click(screen.getByRole("button", { name: "Cancel" }));
    expect(screen.queryByRole("button", { name: "Save" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Edit" })).toBeInTheDocument();
  });
});

describe("SubjectDetail renames", () => {
  beforeEach(() => {
    resetStore();
  });

  it("renames from Edit, keeps the permanent short id, and shows no full URN", async () => {
    const user = userEvent.setup();
    renderDetail("goal-lifeos");
    await screen.findByRole("heading", { name: /Ship LifeOS production UI/i }, { timeout: 4000 });
    expect(screen.getByText("g10002")).toBeInTheDocument();
    expect(screen.queryByText(/urn:bsk:/)).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Edit" }));
    const title = screen.getByLabelText("Title");
    await user.clear(title);
    await user.type(title, "Ship LifeOS everywhere");
    await user.click(screen.getByRole("button", { name: "Save" }));

    expect(
      await screen.findByRole("heading", { name: "Ship LifeOS everywhere" }, { timeout: 4000 }),
    ).toBeInTheDocument();
    expect(getState().details["goal-lifeos"]?.title).toBe("Ship LifeOS everywhere");
    expect(getState().details["goal-lifeos"]?.urn).toMatch(/ship-lifeos-production-ui/);
    expect(screen.getByText("g10002")).toBeInTheDocument();
  });

  it("renames a tag everywhere from the Tags tab", async () => {
    const user = userEvent.setup();
    renderDetail("goal-lifeos");
    await screen.findByRole("heading", { name: /Ship LifeOS production UI/i }, { timeout: 4000 });

    await user.click(screen.getByRole("tab", { name: /Tags/ }));
    const lifeosChip = (await screen.findByText("lifeos")).closest("span")!;
    await user.click(within(lifeosChip).getByRole("button", { name: "Rename tag" }));
    const input = screen.getByLabelText("New tag name");
    await user.clear(input);
    await user.type(input, "LifeOS-build");
    await user.click(screen.getByRole("button", { name: "Rename" }));

    expect(await screen.findByText("lifeos-build", {}, { timeout: 4000 })).toBeInTheDocument();
    expect(getState().tagsByItem["goal-lifeos"]).toContain("lifeos-build");
    expect(Object.values(getState().tagsByItem).some((t) => t.includes("lifeos"))).toBe(false);
  });
});

describe("SubjectDetail task timer", () => {
  beforeEach(() => {
    resetStore();
  });

  it("starts, pauses and completes the timer on a Task", async () => {
    const user = userEvent.setup();
    renderDetail("task-shoes");
    const panel = await screen.findByRole("region", { name: "Timer" }, { timeout: 4000 });
    expect(within(panel).getByText("No time recorded yet")).toBeInTheDocument();
    const start = within(panel).getByRole("button", { name: "Start" });
    await waitFor(() => expect(start).toBeEnabled(), { timeout: 4000 });

    await user.click(start);
    expect(await within(panel).findByText("Running", {}, { timeout: 4000 })).toBeInTheDocument();
    expect(within(panel).getByRole("timer")).toBeInTheDocument();

    await user.click(within(panel).getByRole("button", { name: "Pause" }));
    expect(await within(panel).findByText("Paused", {}, { timeout: 4000 })).toBeInTheDocument();
    expect(within(panel).getByRole("button", { name: "Resume" })).toBeInTheDocument();

    await user.click(within(panel).getByRole("button", { name: "Complete" }));
    await waitFor(() => expect(getState().details["task-shoes"]?.status).toBe("Completed"), {
      timeout: 4000,
    });
    expect(await within(panel).findByRole("button", { name: "Start" }, { timeout: 4000 })).toBeInTheDocument();
  });

  it("shows no timer on other types", async () => {
    renderDetail("goal-lifeos");
    await screen.findByRole("heading", { name: /Ship LifeOS production UI/i }, { timeout: 4000 });
    expect(screen.queryByRole("region", { name: "Timer" })).not.toBeInTheDocument();
  });
});

describe("SubjectDetail repeating tasks (TASKS-3)", () => {
  beforeEach(() => {
    resetStore();
  });

  function openTrash() {
    const open = mockTaskSeriesHistory("task-trash")!.repetitions.find((r) => !r.closed)!;
    return open.id;
  }

  it("says how a repetition repeats, lists the history, and marks it missed", async () => {
    const user = userEvent.setup();
    const id = openTrash();
    renderDetail(id);
    const panel = await screen.findByRole("region", { name: "Repeats" }, { timeout: 4000 });
    expect(within(panel).getByText("Every Wednesday")).toBeInTheDocument();
    expect(within(panel).getByText(/of \d+ done/)).toBeInTheDocument();
    const list = await within(panel).findByRole("list", { name: "Repetitions" }, { timeout: 4000 });
    expect(within(list).getByText("This one")).toBeInTheDocument();
    expect(within(list).getAllByText("Missed").length).toBeGreaterThanOrEqual(1);
    // Its price is the series'.
    expect(screen.getByText(/The repeating task's price/)).toBeInTheDocument();

    await user.click(within(panel).getByRole("button", { name: "Mark missed" }));
    await waitFor(() => expect(getState().details[id]?.status).toBe("Missed"), { timeout: 4000 });
    // The next one was made.
    expect(mockTaskSeriesHistory("task-trash")!.repetitions.filter((r) => !r.closed)).toHaveLength(1);
  });

  it("offers Stop repeating on the series, which has no timer", async () => {
    const user = userEvent.setup();
    renderDetail("task-trash");
    const panel = await screen.findByRole("region", { name: "Repeats" }, { timeout: 4000 });
    expect(screen.queryByRole("region", { name: "Timer" })).not.toBeInTheDocument();
    expect(screen.getByText(/Every repetition pays this/)).toBeInTheDocument();

    await user.click(within(panel).getByRole("button", { name: "Stop repeating" }));
    await waitFor(() => expect(getState().details["task-trash"]?.status).toBe("Cancelled"), { timeout: 4000 });
    expect(await within(panel).findByRole("button", { name: "Repeat again" }, { timeout: 4000 })).toBeInTheDocument();
  });
});

describe("SubjectDetail tag autocomplete", () => {
  beforeEach(() => {
    resetStore();
  });

  it("suggests existing tags and adds the one picked", async () => {
    const own = getState().tagsByItem["task-longrun"] ?? [];
    const target = tagUniverse(getState()).find((t) => !own.includes(t.tag))!.tag;
    const user = userEvent.setup();
    renderDetail("task-longrun");
    await screen.findByRole("heading", { name: /long run/i }, { timeout: 4000 });

    await user.click(screen.getByRole("tab", { name: /Tags/ }));
    const input = await screen.findByRole("combobox", { name: "Add a tag" });
    await user.type(input, target.slice(0, 3));
    const listbox = await screen.findByRole("listbox", { name: "Existing tags" });
    for (const tag of own) {
      expect(within(listbox).queryByText(tag)).not.toBeInTheDocument();
    }
    await user.click(within(listbox).getByText(target));

    await waitFor(() => expect(getState().tagsByItem["task-longrun"]).toContain(target), {
      timeout: 4000,
    });
    expect(input).toHaveValue("");
  });
});

describe("SubjectDetail covers", () => {
  beforeEach(() => {
    resetStore();
  });

  function storedPosition(id: string): unknown {
    const attrs = JSON.parse(getState().details[id]?.attributes ?? "{}") as { cover?: { position_y?: unknown } };
    return attrs.cover?.position_y;
  }

  it("shows a cover across the top with its credit, linked with Unsplash's referral", async () => {
    renderDetail("goal-lifeos");
    await screen.findByRole("heading", { name: /Ship LifeOS production UI/i }, { timeout: 4000 });
    const banner = screen.getByTestId("cover-banner");
    expect(banner).toHaveTextContent("Photo by Mina Cho on Unsplash");
    const links = within(banner).getAllByRole("link");
    expect(links.map((a) => a.textContent)).toEqual(["Mina Cho", "Unsplash"]);
    for (const link of links) {
      expect(link).toHaveAttribute("href", expect.stringContaining("utm_source=lifeos&utm_medium=referral"));
      expect(link).toHaveAttribute("target", "_blank");
    }
    // Decorative: the title beside it says what it's about.
    expect(banner.querySelector("img")).toHaveAttribute("alt", "");
    expect(screen.queryByRole("button", { name: "Add cover" })).not.toBeInTheDocument();
  });

  it("adds a cover from the picker, searched with the title", async () => {
    const user = userEvent.setup();
    renderDetail("goal-half");
    await screen.findByRole("heading", { name: /Run a half-marathon/i }, { timeout: 4000 });
    expect(screen.queryByTestId("cover-banner")).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Add cover" }));
    const picker = await screen.findByRole("dialog", { name: "Choose a cover" });
    expect(within(picker).getByRole("searchbox", { name: "Search Unsplash" })).toHaveValue("Run a half-marathon");
    await within(picker).findByRole("button", { name: /Use the photo by Ada Hollis/ }, { timeout: 4000 });

    const search = within(picker).getByRole("searchbox", { name: "Search Unsplash" });
    await user.clear(search);
    await user.type(search, "lake{Enter}");
    await user.click(await within(picker).findByRole("button", { name: /Use the photo by Noor Haddad/ }, { timeout: 4000 }));

    await waitFor(() => expect(screen.getByTestId("cover-banner")).toHaveTextContent("Photo by Noor Haddad on Unsplash"), {
      timeout: 4000,
    });
    await waitFor(() => expect(screen.queryByRole("dialog", { name: "Choose a cover" })).not.toBeInTheDocument());
  });

  it("repositions with the arrow keys and saves, then removes the cover", async () => {
    const user = userEvent.setup();
    renderDetail("proj-web");
    await screen.findByRole("heading", { name: /Production web UI/i }, { timeout: 4000 });
    expect(storedPosition("proj-web")).toBe(50);

    screen.getByRole("button", { name: "Cover options" }).focus();
    await user.keyboard("{Enter}");
    await user.click(await screen.findByRole("menuitem", { name: "Reposition" }));
    const slider = await screen.findByRole("slider", { name: /Cover position/ });
    await waitFor(() => expect(slider).toHaveFocus());
    await user.keyboard("{ArrowUp}{ArrowUp}");
    expect(slider).toHaveAttribute("aria-valuenow", "60");
    expect(slider).toHaveAttribute("aria-valuetext", "Showing the photo 60% of the way down");
    await user.click(screen.getByRole("button", { name: "Save position" }));
    await waitFor(() => expect(storedPosition("proj-web")).toBe(60));
    await waitFor(() => expect(screen.queryByRole("slider")).not.toBeInTheDocument());

    screen.getByRole("button", { name: "Cover options" }).focus();
    await user.keyboard("{Enter}");
    await user.click(await screen.findByRole("menuitem", { name: "Remove cover" }));
    expect(await screen.findByRole("button", { name: "Add cover" }, { timeout: 4000 })).toBeInTheDocument();
    expect(screen.queryByTestId("cover-banner")).not.toBeInTheDocument();
  });

  it("offers no cover on a Task", async () => {
    renderDetail("task-longrun");
    await screen.findByRole("heading", { level: 2 }, { timeout: 4000 });
    expect(screen.queryByRole("button", { name: "Add cover" })).not.toBeInTheDocument();
    expect(screen.queryByTestId("cover-banner")).not.toBeInTheDocument();
  });
});

describe("SubjectDetail Decisions (GEN-13)", () => {
  beforeEach(() => {
    resetStore();
  });

  const stack = {
    id: "dec-stack",
    title: "React + Vite for Production UI",
    type: "Decision",
    status: "Implementing",
  };

  it("shows a Decision's links as Context and Resulting work, with Replaces, not Parents and Children", async () => {
    const user = userEvent.setup();
    const onOpenSubject = vi.fn();
    renderDetail("dec-stack", { onOpenSubject });
    await screen.findByRole("heading", { name: stack.title }, { timeout: 4000 });
    await user.click(screen.getByRole("tab", { name: /Relationships/ }));

    const context = await screen.findByRole("region", { name: "Context" }, { timeout: 4000 });
    expect(context).toHaveTextContent(/Nothing yet/);
    const work = screen.getByRole("region", { name: "Resulting work" });
    await within(work).findByRole("button", { name: "Ship LifeOS production UI" }, { timeout: 4000 });
    expect(work).toHaveTextContent(/doesn't move this Decision's status/);
    const replaces = screen.getByRole("region", { name: "Replaces" });
    expect(within(replaces).getByRole("button", { name: "Blazor for Production UI" })).toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Replaced by" })).not.toBeInTheDocument();
    // Links outside the two groups still show, in words.
    const other = screen.getByRole("region", { name: "Other links" });
    expect(other).toHaveTextContent(/Served by.*Production web UI/);
    expect(screen.queryByRole("heading", { name: "Parents" })).not.toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Children" })).not.toBeInTheDocument();
    // A Decision adds to a group, not by naming a relation.
    expect(screen.getByRole("combobox", { name: "Group" })).toHaveTextContent("Context");
    expect(screen.queryByRole("combobox", { name: "Relation" })).not.toBeInTheDocument();

    await user.click(within(replaces).getByRole("button", { name: "Blazor for Production UI" }));
    expect(onOpenSubject).toHaveBeenCalledWith("dec-blazor");
  });

  it("shows the Decision that replaced one", async () => {
    const user = userEvent.setup();
    renderDetail("dec-blazor");
    await screen.findByRole("heading", { name: "Blazor for Production UI" }, { timeout: 4000 });
    await user.click(screen.getByRole("tab", { name: /Relationships/ }));
    const replacedBy = await screen.findByRole("region", { name: "Replaced by" }, { timeout: 4000 });
    expect(replacedBy).toHaveTextContent("React + Vite for Production UI");
  });

  it("creates resulting work of a chosen type, or a replacement, from the Decision", async () => {
    const user = userEvent.setup();
    const openNew = vi.fn();
    renderDetail("dec-stack", { openNew });
    await screen.findByRole("heading", { name: stack.title }, { timeout: 4000 });
    expect(screen.queryByRole("button", { name: "Record decision" })).not.toBeInTheDocument();

    screen.getByRole("button", { name: "Resulting work" }).focus();
    await user.keyboard("{Enter}");
    const menu = await screen.findByRole("menu");
    expect(within(menu).getAllByRole("menuitem").map((i) => i.textContent?.trim())).toEqual([
      "Goal",
      "Project",
      "Task",
      "Habit",
      "Commitment",
      "Appointment",
    ]);
    await user.click(within(menu).getByRole("menuitem", { name: "Project" }));
    expect(openNew).toHaveBeenLastCalledWith({ type: "Project", link: { kind: "resulting-work", decision: stack } });

    await user.click(screen.getByRole("button", { name: "Replacement" }));
    expect(openNew).toHaveBeenLastCalledWith({ type: "Decision", link: { kind: "replacement", decision: stack } });
  });

  it("hides the Decision's actions while editing", async () => {
    const user = userEvent.setup();
    renderDetail("dec-stack");
    await screen.findByRole("heading", { name: stack.title }, { timeout: 4000 });
    await user.click(screen.getByRole("button", { name: "Edit" }));
    expect(screen.queryByRole("button", { name: "Resulting work" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Replacement" })).not.toBeInTheDocument();
  });

  it("records a Decision from a Problem with the Problem as its Context", async () => {
    const user = userEvent.setup();
    const openNew = vi.fn();
    renderDetail("prob-sleep", { openNew });
    await screen.findByRole("heading", { name: "How do I sleep through the night?" }, { timeout: 4000 });

    await user.click(screen.getByRole("button", { name: "Record decision" }));
    expect(openNew).toHaveBeenCalledWith({
      type: "Decision",
      link: {
        kind: "context",
        item: { id: "prob-sleep", title: "How do I sleep through the night?", type: "Problem", status: "Working" },
      },
    });
    // A Problem's Relationships stay Parents and Children.
    await user.click(screen.getByRole("tab", { name: /Relationships/ }));
    expect(await screen.findByRole("heading", { name: "Parents" }, { timeout: 4000 })).toBeInTheDocument();
  });
});
