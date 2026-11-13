import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { SubjectDetail } from "@/components/detail/subject-detail";
import { CreateActions } from "@/components/create/create-context";
import { TooltipProvider } from "@/components/ui/tooltip";
import { getState, resetStore } from "@/lib/mock/store";

vi.mock("sonner", () => ({
  toast: Object.assign(vi.fn(), { success: vi.fn(), error: vi.fn(), message: vi.fn() }),
}));

/** How many attribute writes the panel sent, and a description the "kernel" refuses. */
const sent = vi.hoisted(() => ({ setAttributes: 0, refuse: "Refuse this description" }));

vi.mock("@/lib/mock/write-client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/mock/write-client")>();
  return {
    ...actual,
    createMockWriteClient: (...args: Parameters<typeof actual.createMockWriteClient>) => {
      const client = actual.createMockWriteClient(...args);
      return {
        ...client,
        async setAttributes(subject: string, attrs: Record<string, string>) {
          sent.setAttributes += 1;
          if (Object.values(attrs).includes(sent.refuse)) throw new Error("The kernel refused that description.");
          return client.setAttributes(subject, attrs);
        },
      };
    },
  };
});

function renderDetail(id: string) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 0 } } });
  return render(
    <QueryClientProvider client={client}>
      <TooltipProvider>
        <CreateActions.Provider value={{ openNew: () => {}, openCapture: () => {}, openVoice: () => {} }}>
          <SubjectDetail subjectId={id} />
        </CreateActions.Provider>
      </TooltipProvider>
    </QueryClientProvider>,
  );
}

async function editDescription(text: string) {
  const user = userEvent.setup();
  renderDetail("goal-lifeos");
  await screen.findByRole("heading", { name: /Ship LifeOS production UI/i }, { timeout: 4000 });
  await user.click(screen.getByRole("button", { name: "Edit" }));
  const description = screen.getByLabelText("Description");
  await user.clear(description);
  await user.type(description, text);
  const save = screen.getByRole("button", { name: "Save" });
  await waitFor(() => expect(save).toBeEnabled());
  return { user, save };
}

describe("SubjectDetail Save (code review CR60)", () => {
  beforeEach(() => {
    resetStore();
    sent.setAttributes = 0;
  });

  it("runs one save at a time, so a double-click writes once", async () => {
    const { user, save } = await editDescription("Hardened for every interface.");

    await user.dblClick(save);

    expect(await screen.findByRole("button", { name: "Edit" }, { timeout: 4000 })).toBeInTheDocument();
    expect(sent.setAttributes).toBe(1);
    expect(getState().details["goal-lifeos"]?.scope).toBe("Hardened for every interface.");
  });

  it("holds Save and Cancel while the save runs", async () => {
    const { user, save } = await editDescription("Hardened for every interface.");

    await user.click(save);

    expect(save).toBeDisabled();
    expect(screen.getByRole("button", { name: "Cancel" })).toBeDisabled();
    expect(await screen.findByRole("button", { name: "Edit" }, { timeout: 4000 })).toBeInTheDocument();
  });

  it("says a refusal beside Save and stays open on what was typed", async () => {
    const { user, save } = await editDescription(sent.refuse);

    await user.click(save);

    expect(await screen.findByRole("alert")).toHaveTextContent("The kernel refused that description.");
    expect(screen.getByLabelText("Description")).toHaveValue(sent.refuse);
    expect(screen.getByRole("button", { name: "Save" })).toBeEnabled();
  });
});
