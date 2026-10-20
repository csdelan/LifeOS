import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { SubjectDetail } from "@/components/detail/subject-detail";
import { CreateActions } from "@/components/create/create-context";
import { TooltipProvider } from "@/components/ui/tooltip";
import { getState, resetStore } from "@/lib/mock/store";
import { tagUniverse } from "@/lib/mock/seed";

vi.mock("sonner", () => ({
  toast: Object.assign(vi.fn(), {
    success: vi.fn(),
    error: vi.fn(),
    message: vi.fn(),
  }),
}));

function renderDetail(id: string) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false, staleTime: 0 } },
  });
  return render(
    <QueryClientProvider client={client}>
      <TooltipProvider>
        <CreateActions.Provider
          value={{ openNew: () => {}, openCapture: () => {}, openVoice: () => {} }}
        >
          <SubjectDetail subjectId={id} />
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
