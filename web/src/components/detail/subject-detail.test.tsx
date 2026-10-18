import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { SubjectDetail } from "@/components/detail/subject-detail";
import { CreateActions } from "@/components/create/create-context";
import { TooltipProvider } from "@/components/ui/tooltip";
import { getState, resetStore } from "@/lib/mock/store";

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
