import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { NewSubjectDialog } from "@/components/create/new-subject-dialog";
import { ApiRequestError } from "@/lib/live/http";
import { createMockWriteClient } from "@/lib/mock/write-client";
import type { LifeOsWriteClient, SubjectType } from "@/lib/production-ui-types";
import type { CreateParent } from "@/components/create/new-subject-dialog";

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
});
