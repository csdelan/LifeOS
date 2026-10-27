import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { NewSubjectDialog } from "@/components/create/new-subject-dialog";
import { ApiRequestError } from "@/lib/live/http";
import { createMockWriteClient } from "@/lib/mock/write-client";
import { getState, resetStore } from "@/lib/mock/store";
import { tagUniverse } from "@/lib/mock/seed";
import type { LifeOsWriteClient, SubjectType } from "@/lib/production-ui-types";
import type { CreateParent } from "@/components/create/new-subject-dialog";
import type { LinkedItem, PendingLink } from "@/lib/decisions";

vi.mock("sonner", () => ({
  toast: Object.assign(vi.fn(), {
    success: vi.fn(),
    error: vi.fn(),
    message: vi.fn(),
  }),
}));

let writes: LifeOsWriteClient;

vi.mock("@/lib/client", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/client")>()),
  createWriteClient: () => writes,
}));

afterEach(() => {
  vi.restoreAllMocks();
});

function renderDialog(props: {
  initialType?: SubjectType;
  initialParent?: CreateParent;
  initialLink?: PendingLink;
  onOpenChange?: (open: boolean) => void;
}) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false, staleTime: 0 } },
  });
  return render(
    <QueryClientProvider client={client}>
      <NewSubjectDialog open onOpenChange={props.onOpenChange ?? (() => {})} {...props} />
    </QueryClientProvider>,
  );
}

const steward: CreateParent = { id: "value-steward", title: "Steward of my health", type: "Value" };

describe("NewSubjectDialog", () => {
  it("shows the API error and stays open when the create is rejected", async () => {
    writes = createMockWriteClient();
    vi.spyOn(writes, "newSubject").mockRejectedValue(
      new ApiRequestError("No subject matches 'value-steward'.", 404),
    );
    const onOpenChange = vi.fn();
    renderDialog({ initialParent: steward, onOpenChange });

    fireEvent.change(screen.getByLabelText("Title"), { target: { value: "Run a marathon" } });
    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("No subject matches 'value-steward'.");
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
    expect(screen.getByRole("button", { name: "Save" })).toBeEnabled();
  });

  it("repeats a validation error next to Save", async () => {
    writes = createMockWriteClient();
    const create = vi.spyOn(writes, "newSubject");
    renderDialog({ initialType: "Goal" });

    fireEvent.change(screen.getByLabelText("Title"), { target: { value: "Run a marathon" } });
    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Connect this Goal to an Identity Statement.",
    );
    expect(create).not.toHaveBeenCalled();
  });

  it("creates a Goal under its Identity Statement and closes", async () => {
    writes = createMockWriteClient();
    const create = vi.spyOn(writes, "newSubject");
    const onOpenChange = vi.fn();
    renderDialog({ initialParent: steward, onOpenChange });

    fireEvent.change(screen.getByLabelText("Title"), { target: { value: "Run a marathon" } });
    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
    expect(create).toHaveBeenCalledWith(
      expect.objectContaining({ type: "Goal", parent: "value-steward", relation: "serves" }),
    );
  });

  it("sends serves for a Habit's first parent", async () => {
    writes = createMockWriteClient();
    const create = vi.spyOn(writes, "newSubject");
    const onOpenChange = vi.fn();
    renderDialog({ initialType: "Habit", onOpenChange });

    fireEvent.change(screen.getByLabelText("Title"), { target: { value: "Stretch" } });
    fireEvent.click(await screen.findByRole("checkbox", { name: /Run a half-marathon/ }));
    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
    expect(create).toHaveBeenCalledWith(
      expect.objectContaining({ type: "Habit", parent: "goal-half", relation: "serves" }),
    );
  });

  it("picks tags from the existing ones and keeps a new one typed before Save", async () => {
    resetStore();
    writes = createMockWriteClient();
    const tag = vi.spyOn(writes, "tag");
    const onOpenChange = vi.fn();
    const existing = [...tagUniverse(getState())].sort((a, b) => b.itemCount - a.itemCount)[0].tag;
    const user = userEvent.setup();
    renderDialog({ initialType: "Task", onOpenChange });

    await user.type(screen.getByLabelText("Title"), "Buy trail shoes");
    const tags = screen.getByRole("combobox", { name: /Tags/ });
    await user.click(tags);
    const listbox = await screen.findByRole("listbox", { name: "Existing tags" });
    await user.click(within(listbox).getByText(existing));
    expect(screen.getByRole("button", { name: `Remove tag ${existing}` })).toBeInTheDocument();

    await user.type(tags, "Gear");
    await user.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
    expect(tag).toHaveBeenCalledWith(expect.any(String), { add: [existing, "gear"] });
  });
});

describe("NewSubjectDialog with a Decision link (GEN-13)", () => {
  const stack: LinkedItem = {
    id: "dec-stack",
    title: "React + Vite for Production UI",
    type: "Decision",
    status: "Implementing",
  };

  function edgesOf(id: string) {
    return getState().edges.filter((e) => e.fromId === id || e.toId === id);
  }

  it("creates resulting work linked from the Decision, shown before Save, and leaves its status", async () => {
    resetStore();
    writes = createMockWriteClient();
    const create = vi.spyOn(writes, "newSubject");
    const link = vi.spyOn(writes, "link");
    const onOpenChange = vi.fn();
    renderDialog({ initialType: "Task", initialLink: { kind: "resulting-work", decision: stack }, onOpenChange });

    expect(screen.getByRole("heading", { name: "New Task" })).toBeInTheDocument();
    const pending = screen.getByRole("group", { name: "Resulting work of" });
    expect(pending).toHaveTextContent("React + Vite for Production UI");
    expect(pending).toHaveAccessibleDescription(/The Decision's status stays as it is/);
    // The type can still change, among work that carries a Decision out.
    expect(screen.getByRole("combobox", { name: "Subject type" })).toHaveTextContent("Task");

    fireEvent.change(screen.getByLabelText("Title"), { target: { value: "Scaffold the Vite app" } });
    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
    expect(create).toHaveBeenCalledWith(expect.objectContaining({ type: "Task", parent: undefined }));
    const made = await create.mock.results[0].value;
    expect(link).toHaveBeenCalledWith("dec-stack", "results_in", made.id);
    expect(edgesOf(made.id)).toEqual([{ fromId: "dec-stack", relation: "results_in", toId: made.id }]);
    expect(getState().details["dec-stack"]?.status).toBe("Implementing");
  });

  it("creates a Decision with the item it was started from as its Context", async () => {
    resetStore();
    writes = createMockWriteClient();
    const onOpenChange = vi.fn();
    const problem: LinkedItem = { id: "prob-sleep", title: "How do I sleep through the night?", type: "Problem" };
    renderDialog({ initialType: "Decision", initialLink: { kind: "context", item: problem }, onOpenChange });

    expect(screen.getByRole("group", { name: "Context" })).toHaveTextContent("How do I sleep through the night?");
    // Only a Decision is made here.
    expect(screen.queryByRole("combobox", { name: "Subject type" })).not.toBeInTheDocument();

    fireEvent.change(screen.getByLabelText("Title"), { target: { value: "No caffeine after noon" } });
    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
    const made = getState().subjects.find((s) => s.title === "No caffeine after noon")!;
    expect(made.type).toBe("Decision");
    expect(edgesOf(made.id)).toEqual([{ fromId: "prob-sleep", relation: "results_in", toId: made.id }]);
    expect(getState().details["prob-sleep"]?.status).toBe("Working");
  });

  it("creates a replacement that supersedes the original, which becomes Cancelled and keeps its conclusion", async () => {
    resetStore();
    writes = createMockWriteClient();
    const create = vi.spyOn(writes, "newSubject");
    const onOpenChange = vi.fn();
    renderDialog({ initialType: "Decision", initialLink: { kind: "replacement", decision: stack }, onOpenChange });

    const pending = screen.getByRole("group", { name: "Replaces" });
    expect(pending).toHaveTextContent("React + Vite for Production UI");
    expect(pending).toHaveAccessibleDescription(/Saving marks it Cancelled/);

    fireEvent.change(screen.getByLabelText("Title"), { target: { value: "SvelteKit for Production UI" } });
    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
    // One write makes it and its link.
    expect(create).toHaveBeenCalledWith(
      expect.objectContaining({ type: "Decision", parent: "dec-stack", relation: "supersedes" }),
    );
    const made = await create.mock.results[0].value;
    expect(edgesOf(made.id)).toEqual([{ fromId: made.id, relation: "supersedes", toId: "dec-stack" }]);
    const original = getState().details["dec-stack"]!;
    expect(original.status).toBe("Cancelled");
    expect(original.title).toBe("React + Vite for Production UI");
    expect(original.scope).toBe("React + TypeScript + Vite. Kernel stays C#.");
    expect(getState().history["dec-stack"]?.map((h) => h.status).slice(0, 1)).toEqual(["Cancelled"]);
  });
});
