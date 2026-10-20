import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { NewSubjectDialog } from "@/components/create/new-subject-dialog";
import { CreateActions } from "@/components/create/create-context";
import { SubjectDetail } from "@/components/detail/subject-detail";
import { TooltipProvider } from "@/components/ui/tooltip";
import { getState, resetStore } from "@/lib/mock/store";
import { createMockWriteClient } from "@/lib/mock/write-client";
import type { LifeOsWriteClient } from "@/lib/production-ui-types";

vi.mock("sonner", () => ({
  toast: Object.assign(vi.fn(), { success: vi.fn(), error: vi.fn(), message: vi.fn() }),
}));

vi.mock("@/lib/celebrate", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/celebrate")>()),
  celebrate: vi.fn(() => ({ confetti: false, sound: false })),
  chime: vi.fn(() => false),
}));

let writes: LifeOsWriteClient;

vi.mock("@/lib/client", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/client")>()),
  createWriteClient: () => writes,
}));

beforeEach(() => {
  resetStore();
  writes = createMockWriteClient();
});

afterEach(() => {
  vi.restoreAllMocks();
});

function withProviders(ui: React.ReactElement) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 0 } } });
  return render(
    <QueryClientProvider client={client}>
      <TooltipProvider>
        <CreateActions.Provider value={{ openNew: () => {}, openCapture: () => {}, openVoice: () => {} }}>
          {ui}
        </CreateActions.Provider>
      </TooltipProvider>
    </QueryClientProvider>,
  );
}

const attrsOf = (id: string) => JSON.parse(getState().details[id]?.attributes ?? "{}") as Record<string, unknown>;

describe("bounty in the New dialog", () => {
  it("starts from the type default and saves only what changed", async () => {
    const setBounty = vi.spyOn(writes, "setBounty");
    const onOpenChange = vi.fn();
    withProviders(<NewSubjectDialog open onOpenChange={onOpenChange} initialType="Task" />);

    fireEvent.change(screen.getByLabelText("Title"), { target: { value: "Fix the gate" } });
    expect(await screen.findByText("When completed")).toBeInTheDocument();
    expect(screen.getByText("Pays 2 Coins · 10 Freedom · 10 XP")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Change" }));
    expect(screen.getByLabelText(/XP/)).toHaveValue(10);
    fireEvent.change(screen.getByLabelText(/Karma/), { target: { value: "3" } });
    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
    expect(setBounty).toHaveBeenCalledTimes(1);
    expect(setBounty.mock.calls[0][0]).toEqual({
      subject: expect.any(String),
      trigger: "completed",
      amounts: { coins: 2, freedom: 10, gems: 0, karma: 3, xp: 10, resolve: 0 },
    });
  });

  it("writes no bounty when it's left on the default", async () => {
    const setBounty = vi.spyOn(writes, "setBounty");
    const onOpenChange = vi.fn();
    withProviders(<NewSubjectDialog open onOpenChange={onOpenChange} initialType="Task" />);

    fireEvent.change(screen.getByLabelText("Title"), { target: { value: "Oil the hinges" } });
    fireEvent.click(await screen.findByRole("button", { name: "Change" }));
    fireEvent.change(screen.getByLabelText(/XP/), { target: { value: "12" } });
    fireEvent.click(screen.getByRole("button", { name: "Use the default" }));
    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
    expect(setBounty).not.toHaveBeenCalled();
  });

  it("refuses a fraction before creating anything", async () => {
    const create = vi.spyOn(writes, "newSubject");
    withProviders(<NewSubjectDialog open onOpenChange={() => {}} initialType="Task" />);

    fireEvent.change(screen.getByLabelText("Title"), { target: { value: "Paint the fence" } });
    fireEvent.click(await screen.findByRole("button", { name: "Change" }));
    fireEvent.change(screen.getByLabelText(/Coins/), { target: { value: "1.5" } });
    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    const alerts = await screen.findAllByRole("alert");
    expect(alerts.some((a) => a.textContent === "A bounty pays a whole number of Coins from zero up.")).toBe(true);
    expect(create).not.toHaveBeenCalled();
  });
});

describe("bounty and establishing in the detail pane", () => {
  it("edits a bounty alone: Save turns on and the price is stored", async () => {
    const user = userEvent.setup();
    withProviders(<SubjectDetail subjectId="task-shoes" />);
    await screen.findByRole("heading", { name: /Buy race shoes/ });
    expect(screen.getAllByText("Pays 2 Coins · 10 Freedom · 10 XP").length).toBeGreaterThan(0);

    await user.click(screen.getByRole("button", { name: "Edit" }));
    const save = screen.getByRole("button", { name: "Save" });
    expect(save).toBeDisabled();
    await user.click(screen.getByRole("button", { name: "Change" }));
    fireEvent.change(screen.getByLabelText(/Karma/), { target: { value: "3" } });
    await waitFor(() => expect(save).toBeEnabled());
    await user.click(save);

    await waitFor(() =>
      expect(attrsOf("task-shoes").bounty).toEqual({ completed: { coins: 2, freedom: 10, karma: 3, xp: 10 } }),
    );
    expect(await screen.findByRole("button", { name: "Edit" })).toBeInTheDocument();
  });

  it("suggests establishing a habit that met its target, and confirming graduates it", async () => {
    const user = userEvent.setup();
    withProviders(<SubjectDetail subjectId="habit-mobility" />);
    await screen.findByRole("heading", { name: /Morning mobility/ });

    await user.click(await screen.findByRole("button", { name: "Ready to establish Morning mobility" }));
    const dialog = await screen.findByRole("alertdialog");
    expect(dialog).toHaveTextContent(/over the last 60 days · target 80%/);
    await waitFor(() => expect(dialog).toHaveTextContent("pays 25 Coins · 60 Freedom · 1 Gems · 100 XP"));
    await user.click(within(dialog).getByRole("button", { name: "Establish" }));

    await waitFor(() =>
      expect(getState().subjects.find((s) => s.id === "habit-mobility")?.status).toBe("Established"),
    );
    expect(await screen.findByText(/graduated from daily tracking/)).toBeInTheDocument();
    expect(getState().ledger.at(-1)).toMatchObject({ entry: "earn", trigger: "established" });
  });

  it("edits a habit's establish target, saving only the part that changed", async () => {
    const setTarget = vi.spyOn(writes, "setHabitTarget");
    const user = userEvent.setup();
    withProviders(<SubjectDetail subjectId="habit-inbox" />);
    expect(await screen.findByText(/over the last 60 days · target 80%/)).toBeInTheDocument();
    expect(screen.queryByLabelText("Target percent of days followed")).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Edit" }));
    const save = screen.getByRole("button", { name: "Save" });
    const percent = screen.getByLabelText("Target percent of days followed");
    expect(percent).toHaveValue(80);
    expect(screen.getByLabelText("Target window in days")).toHaveValue(60);
    expect(save).toBeDisabled();

    fireEvent.change(percent, { target: { value: "70" } });
    await waitFor(() => expect(save).toBeEnabled());
    await user.click(save);

    await waitFor(() => expect(attrsOf("habit-inbox").establish_target).toEqual({ min_adherence: 0.7 }));
    expect(setTarget).toHaveBeenCalledTimes(1);
    expect(setTarget.mock.calls[0][0]).toEqual({ habit: "habit-inbox", minAdherence: 0.7 });
    expect(await screen.findByText(/over the last 60 days · target 70%/)).toBeInTheDocument();
  });

  it("says why a target can't be saved and writes nothing", async () => {
    const setTarget = vi.spyOn(writes, "setHabitTarget");
    const user = userEvent.setup();
    withProviders(<SubjectDetail subjectId="habit-inbox" />);
    await screen.findByText(/over the last 60 days · target 80%/);

    await user.click(screen.getByRole("button", { name: "Edit" }));
    fireEvent.change(screen.getByLabelText("Target window in days"), { target: { value: "0" } });
    expect(await screen.findByRole("alert")).toHaveTextContent(
      "The window is a whole number of days from 1 to 3650.",
    );
    await user.click(screen.getByRole("button", { name: "Save" }));

    expect(setTarget).not.toHaveBeenCalled();
    expect(attrsOf("habit-inbox").establish_target).toBeUndefined();
    expect(screen.getByRole("button", { name: "Save" })).toBeInTheDocument();
  });
});
