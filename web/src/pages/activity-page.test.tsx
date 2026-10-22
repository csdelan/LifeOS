import { StrictMode } from "react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { toast } from "sonner";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { TooltipProvider } from "@/components/ui/tooltip";
import { mockActivityWorkouts, mockAuthorizeOura, setMockOura } from "@/lib/mock/activity-store";
import { resetStore } from "@/lib/mock/store";
import { createMockWriteClient } from "@/lib/mock/write-client";
import type { LifeOsWriteClient } from "@/lib/production-ui-types";
import { ActivityConnectedPage, ActivityPage } from "@/pages/activity-page";

vi.mock("sonner", () => ({
  toast: Object.assign(vi.fn(), { success: vi.fn(), error: vi.fn(), message: vi.fn() }),
}));

let writes: LifeOsWriteClient;

const router = vi.hoisted(() => ({
  search: {} as Record<string, string | undefined>,
  navigate: vi.fn(),
}));

vi.mock("@tanstack/react-router", () => ({
  useSearch: () => router.search,
  useNavigate: () => router.navigate,
}));

vi.mock("@/lib/client", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/client")>()),
  createWriteClient: () => writes,
}));

beforeEach(() => {
  resetStore();
  localStorage.clear();
  vi.clearAllMocks();
  router.search = {};
  writes = createMockWriteClient();
});

function renderPage(page = <ActivityPage />) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 0 } } });
  return render(
    <StrictMode>
      <QueryClientProvider client={client}>
        <TooltipProvider>{page}</TooltipProvider>
      </QueryClientProvider>
    </StrictMode>,
  );
}

const card = async (name: string) => within(await screen.findByRole("region", { name }));

describe("Activity page (activity plan §3.7)", () => {
  it("shows today, steps and exercise, and syncs once on opening", async () => {
    const sync = vi.spyOn(writes, "syncActivity");
    renderPage();

    const today = await card("Today");
    expect(today.getByText("Steps so far")).toBeInTheDocument();
    expect(today.getByRole("meter", { name: "Steps toward the goal" })).toHaveAttribute(
      "aria-valuetext",
      expect.stringMatching(/^[\d,]+ steps so far, /),
    );
    expect(today.getByText("Last night")).toBeInTheDocument();
    expect(today.getByText("Sleep")).toBeInTheDocument();

    const steps = await card("Steps");
    expect(await steps.findByRole("group", { name: /^Steps by day, last 30 days/ })).toBeInTheDocument();
    expect(steps.getByText("7-day average", { selector: "dt" })).toBeInTheDocument();
    expect(steps.getByText("Best day")).toBeInTheDocument();

    const exercise = await card("Exercise");
    expect(await exercise.findByText("Minutes per week")).toBeInTheDocument();
    expect(exercise.getByRole("heading", { name: /^This week/ })).toBeInTheDocument();

    await waitFor(() => expect(sync).toHaveBeenCalledTimes(1));
    expect(sync).toHaveBeenCalledWith(false);
    expect(await screen.findByText("Synced from Oura just now")).toBeInTheDocument();
  });

  it("syncs again on Sync now, forced", async () => {
    const sync = vi.spyOn(writes, "syncActivity");
    renderPage();
    await waitFor(() => expect(sync).toHaveBeenCalledTimes(1));
    // Sync now waits for the sync on opening to finish.
    const button = await screen.findByRole("button", { name: "Sync now" });
    await waitFor(() => expect(button).toBeEnabled());
    await userEvent.click(button);
    await waitFor(() => expect(sync).toHaveBeenLastCalledWith(true));
  });

  it("says a sync that failed, and keeps the page", async () => {
    vi.spyOn(writes, "syncActivity").mockRejectedValue(new Error("Oura is unavailable right now."));
    renderPage();
    expect(await screen.findByRole("alert")).toHaveTextContent("Couldn't sync with Oura: Oura is unavailable right now.");
    expect(await card("Steps")).toBeTruthy();
  });

  it("switches steps to 90 days, remembers it, and lists every day in the Table", async () => {
    const view = renderPage();
    const steps = await card("Steps");
    await userEvent.click(steps.getByRole("button", { name: "90 days" }));
    expect(await steps.findByRole("group", { name: /^Steps by day, last 90 days/ })).toBeInTheDocument();

    await userEvent.click(steps.getByRole("button", { name: "Table" }));
    const table = await steps.findByRole("table");
    await waitFor(() => expect(within(table).getAllByTestId("metric-row")).toHaveLength(90));
    expect(within(table).getAllByTestId("metric-row")[0]).toHaveTextContent(/^Today[\d,]+ so far/);

    view.unmount();
    renderPage();
    expect(await (await card("Steps")).findByRole("group", { name: /last 90 days/ })).toBeInTheDocument();
  });

  it("shows a year as a bar a week", async () => {
    renderPage();
    const steps = await card("Steps");
    await userEvent.click(steps.getByRole("button", { name: "1 year" }));
    expect(await steps.findByRole("group", { name: /^Average steps a day by week, last year/ })).toBeInTheDocument();
    expect(steps.getByText("This week, so far")).toBeInTheDocument();
  });

  it("reads a day in words with the arrow keys", async () => {
    renderPage();
    const steps = await card("Steps");
    const chart = await steps.findByRole("group", { name: /^Steps by day/ });
    chart.focus();
    const readout = document.getElementById(chart.getAttribute("aria-describedby")!)!;
    await waitFor(() => expect(readout).toHaveTextContent(/^Today: [\d,]+ steps so far, /));
    await userEvent.keyboard("{ArrowLeft}");
    expect(readout.textContent).toMatch(/^\w{3}, \w{3} \d+: ([\d,]+ steps, |no steps recorded)/);
    expect(readout.textContent).toMatch(/7-day average [\d,]+$/);
  });

  it("shows the workouts not counted, each with its reason", async () => {
    renderPage();
    const exercise = await card("Exercise");
    const notCounted = mockActivityWorkouts().workouts.filter((w) => !w.counted).length;
    const before = (await exercise.findAllByTestId("workout-row")).length;
    expect(exercise.queryByText(/^Not counted:/)).not.toBeInTheDocument();

    const show = exercise.getByRole("button", { name: `Show ${notCounted} not counted` });
    expect(show).toHaveAttribute("aria-expanded", "false");
    await userEvent.click(show);
    expect(exercise.getAllByTestId("workout-row")).toHaveLength(before + notCounted);
    expect(exercise.getAllByText("Not counted: A walk: it's in your steps").length).toBeGreaterThan(0);
    expect(exercise.getAllByText("Not counted: Detected by Oura").length).toBeGreaterThan(0);
    expect(exercise.getByRole("button", { name: `Hide ${notCounted} not counted` })).toHaveAttribute(
      "aria-expanded",
      "true",
    );
  });

  it("opens a workout with every field, and saves a note on Enter that shows under the row", async () => {
    renderPage();
    const exercise = await card("Exercise");
    const run = (await exercise.findAllByTestId("workout-row")).find((r) => !within(r).queryByTestId("workout-note"))!;
    await userEvent.click(run);

    const sheet = await screen.findByRole("dialog");
    expect(within(sheet).getByText("Counts as exercise.")).toBeInTheDocument();
    expect(within(sheet).getByText("Source")).toBeInTheDocument();
    const box = within(sheet).getByRole("textbox", { name: "Note" });
    expect(box).toHaveAttribute("placeholder", "Add a note");
    expect(box).not.toHaveFocus();

    await userEvent.type(box, "Easy pace, felt strong");
    expect(within(sheet).getByText("22/140")).toBeInTheDocument();
    await userEvent.keyboard("{Enter}");
    expect(await within(sheet).findByText("Saved")).toBeInTheDocument();

    await userEvent.keyboard("{Escape}");
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    expect(await exercise.findByText("Easy pace, felt strong")).toBeInTheDocument();
  });

  it("clears a note, and says why a save was refused", async () => {
    renderPage();
    const exercise = await card("Exercise");
    await userEvent.click(await exercise.findByText("Hill repeats, left knee fine"));
    const sheet = await screen.findByRole("dialog");
    const box = within(sheet).getByRole("textbox", { name: "Note" });
    expect(box).toHaveValue("Hill repeats, left knee fine");

    await userEvent.click(within(sheet).getByRole("button", { name: "Clear" }));
    expect(await within(sheet).findByText("Saved")).toBeInTheDocument();
    expect(box).toHaveValue("");
    await waitFor(() => expect(exercise.queryByText("Hill repeats, left knee fine")).not.toBeInTheDocument());

    vi.spyOn(writes, "setWorkoutNote").mockRejectedValueOnce(new Error("A workout note is one line: take out the line break."));
    await userEvent.type(box, "Again{Enter}");
    expect(await within(sheet).findByRole("alert")).toHaveTextContent("one line");
    expect(box).toHaveAttribute("aria-invalid", "true");
  });

  it("saves what was typed when the sheet closes without a blur", async () => {
    const save = vi.spyOn(writes, "setWorkoutNote");
    renderPage();
    const exercise = await card("Exercise");
    await userEvent.click((await exercise.findAllByTestId("workout-row"))[0]);
    const sheet = await screen.findByRole("dialog");
    await userEvent.type(within(sheet).getByRole("textbox", { name: "Note" }), "Typed then closed");
    await userEvent.click(within(sheet).getByRole("button", { name: "Close" }));
    await waitFor(() => expect(save).toHaveBeenCalledWith(expect.any(String), "Typed then closed"));
    expect(save).toHaveBeenCalledTimes(1);
  });

  it("offers Connect Oura before connecting, says what it reads, and doesn't sync", async () => {
    setMockOura({ state: "not_connected", connected: false, lastSyncedAt: null });
    const sync = vi.spyOn(writes, "syncActivity");
    renderPage();

    const connect = await card("Connect Oura");
    expect(connect.getByText(/It's read-only/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Sync now" })).not.toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Steps" })).not.toBeInTheDocument();

    await userEvent.click(connect.getByRole("button", { name: "Connect Oura" }));
    await waitFor(() =>
      expect(router.navigate).toHaveBeenCalledWith({
        to: "/activity/connected",
        search: { code: "mock-code", state: expect.stringMatching(/^mock-state-/) },
      }),
    );
    expect(sync).not.toHaveBeenCalled();
  });

  it("says what the server is missing when Oura isn't set up", async () => {
    setMockOura({ state: "not_configured", configured: false, connected: false, lastSyncedAt: null });
    renderPage();
    const connect = await card("Connect Oura");
    expect(connect.getByText(/Oura__ClientId/)).toBeInTheDocument();
    expect(connect.queryByRole("button")).not.toBeInTheDocument();
  });

  it("asks to reconnect, keeping what was synced", async () => {
    setMockOura({ state: "reconnect", needsReconnect: true, reconnectReason: "Oura refused the stored sign-in" });
    const sync = vi.spyOn(writes, "syncActivity");
    renderPage();
    const banner = await screen.findByRole("alert");
    expect(banner).toHaveTextContent("Oura needs reconnecting: Oura refused the stored sign-in.");
    expect(within(banner).getByRole("button", { name: "Reconnect Oura" })).toBeInTheDocument();
    expect(await card("Steps")).toBeTruthy();
    expect(sync).not.toHaveBeenCalled();
  });
});

describe("Returning from Oura (/activity/connected)", () => {
  it("connects once with the code and state, then opens the Activity page", async () => {
    setMockOura({ state: "not_connected", connected: false });
    const back = new URL(mockAuthorizeOura("http://localhost/activity/connected"));
    router.search = { code: back.searchParams.get("code")!, state: back.searchParams.get("state")! };
    const connect = vi.spyOn(writes, "connectOura");
    renderPage(<ActivityConnectedPage />);

    expect(screen.getByRole("status")).toHaveTextContent("Connecting Oura…");
    await waitFor(() => expect(router.navigate).toHaveBeenCalledWith({ to: "/activity", replace: true }));
    expect(connect).toHaveBeenCalledTimes(1);
    expect(toast.success).toHaveBeenCalledWith("Oura connected");
  });

  it("says a refused sign-in in words, with Try again", async () => {
    router.search = { error: "access_denied" };
    const connect = vi.spyOn(writes, "connectOura");
    renderPage(<ActivityConnectedPage />);
    expect(await screen.findByRole("alert")).toHaveTextContent("You didn't allow LifeOS to read Oura");
    expect(screen.getByRole("button", { name: "Try again" })).toBeInTheDocument();
    expect(connect).not.toHaveBeenCalled();
  });

  it("says when the state wasn't one LifeOS started", async () => {
    router.search = { code: "mock-code", state: "someone-else" };
    renderPage(<ActivityConnectedPage />);
    expect(await screen.findByRole("alert")).toHaveTextContent("wasn't started here");
  });
});
