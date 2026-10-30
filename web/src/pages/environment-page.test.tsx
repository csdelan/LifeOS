import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { toast } from "sonner";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { TooltipProvider } from "@/components/ui/tooltip";
import { addDays, todayIso } from "@/lib/dates";
import { getState, resetStore } from "@/lib/mock/store";
import { clearMockEnvironment } from "@/lib/mock/environment-store";
import { EnvironmentPage } from "@/pages/environment-page";

vi.mock("sonner", () => ({
  toast: Object.assign(vi.fn(), { success: vi.fn(), error: vi.fn(), message: vi.fn() }),
}));

// jsdom has no canvas to shrink a photo with, so a picked photo goes as it is.
vi.mock("@/lib/photo", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/photo")>()),
  preparePhoto: async (file: File) => ({ blob: file, name: file.name, takenOn: null }),
}));

beforeEach(() => {
  // Midday, so yesterday has settled (days settle at 4 a.m.).
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(new Date(2026, 9, 25, 12, 0, 0));
  resetStore();
  localStorage.clear();
  vi.clearAllMocks();
});

afterEach(() => {
  vi.restoreAllMocks();
  vi.useRealTimers();
});

function renderPage() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 0 } } });
  return render(
    <QueryClientProvider client={client}>
      <TooltipProvider>
        <EnvironmentPage />
      </TooltipProvider>
    </QueryClientProvider>,
  );
}

describe("Environment page: the bar (environment plan §4.8)", () => {
  it("says the Home Score today needs to keep the level on track, rounded up", async () => {
    // 6,000 Environment XP: effort has earned level 37, and the seeded month of ratings holds 19.
    getState().ledger.push({
      id: "cleaned",
      occurredAt: new Date().toISOString(),
      entry: "earn",
      provenance: "derived",
      amounts: { environment_xp: 6_000 },
      trigger: "completed",
      awardKey: "completed:task-garage",
      subjectId: "task-garage",
      creditedOn: addDays(todayIso(), -2),
    });
    renderPage();

    expect(await screen.findByTestId("home-score-bar")).toHaveTextContent("· 39.6+ keeps level 19 on track");
    expect(screen.getByTestId("home-score")).toHaveTextContent("69.2");
    expect(await screen.findByText("The bar for your level")).toBeInTheDocument();
  });

  it("says level 1's bar before any Environment XP", async () => {
    renderPage();
    expect(await screen.findByTestId("home-score-bar")).toHaveTextContent("· 30.0+ keeps level 1 on track");
  });
});

describe("Environment page: photos (HOME-4, environment plan §8)", () => {
  it("shows a space's Then beside its Now, a box to add the first, and its shots to pick from", async () => {
    const user = userEvent.setup();
    renderPage();
    const office = await screen.findByRole("article", { name: "Office" });
    const photos = within(office).getByTestId("then-and-now");
    expect(within(photos).getByTestId("photo-then")).toHaveAccessibleName(/^Compare Office: Then · \w{3} \d+ · \d+\.\d$/);
    expect(within(photos).getByTestId("photo-now")).toHaveAccessibleName(/^Compare Office: Now · \w{3} \d+ · \d+\.\d$/);
    expect(photos).toHaveTextContent(/photo (today|yesterday|\d+ days ago) · 2 photos$/);

    const garage = screen.getByRole("article", { name: "Garage" });
    expect(within(garage).getByRole("button", { name: "Add photos of Garage: none yet" })).toHaveTextContent(
      "No photos of Garage yet",
    );

    // The Kitchen has two shots; its Counters has one photo, so Now waits for another.
    const kitchen = screen.getByRole("article", { name: "Kitchen" });
    const shots = within(kitchen).getByRole("group", { name: "Shot" });
    expect(within(shots).getByRole("button", { name: "Kitchen" })).toHaveAttribute("aria-pressed", "true");
    await user.click(within(shots).getByRole("button", { name: "Counters" }));
    expect(within(kitchen).getByTestId("photo-then")).toHaveAccessibleName(/^Compare Kitchen · Counters: Then · /);
    expect(
      within(kitchen).getByRole("button", { name: "Add a photo of Kitchen · Counters to compare" }),
    ).toHaveTextContent("Add a photo to compare");
  });

  it("opens the compare dialog on Now, and makes Now the baseline", async () => {
    const user = userEvent.setup();
    renderPage();
    const office = await screen.findByRole("article", { name: "Office" });
    await user.click(within(office).getByTestId("photo-now"));
    const dialog = await screen.findByRole("dialog", { name: "Office" });
    const left = within(dialog).getByTestId("compare-left");
    const right = within(dialog).getByTestId("compare-right");
    expect(within(left).getByRole("button", { name: /^Left side: Then · / })).toHaveAttribute("aria-pressed", "false");
    expect(within(right).getByRole("button", { name: /^Right side: Now · / })).toHaveAttribute("aria-pressed", "true");
    expect(left).toHaveTextContent(/Tidiness \d/);
    const strip = await within(dialog).findByTestId("filmstrip");
    expect(within(strip).getAllByRole("button")).toHaveLength(2);
    expect(within(strip).getAllByRole("button")[0]).toHaveAccessibleName(/ · baseline$/);

    await user.click(within(dialog).getByRole("button", { name: "Make this the baseline" }));
    expect(await within(dialog).findByRole("button", { name: "This is the baseline" })).toBeDisabled();
    expect(within(strip).getAllByRole("button")[1]).toHaveAccessibleName(/ · baseline$/);
    expect(within(strip).getAllByRole("button")[0]).not.toHaveAccessibleName(/baseline/);
  });
});

describe("Environment page: sorting photos into shots (HOME-4)", () => {
  it("makes a photo from another spot its own shot, as that shot's baseline", async () => {
    const user = userEvent.setup();
    renderPage();
    const kitchen = await screen.findByRole("article", { name: "Kitchen" });
    await user.click(within(kitchen).getByTestId("photo-now"));
    const dialog = await screen.findByRole("dialog", { name: "Kitchen" });
    const strip = await within(dialog).findByTestId("filmstrip");
    expect(within(strip).getAllByRole("button")).toHaveLength(3);

    await user.click(within(dialog).getByRole("button", { name: "Make this its own shot" }));
    await user.type(within(dialog).getByLabelText("Name the new shot"), "Sink");
    await user.click(within(dialog).getByRole("button", { name: "Make shot" }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Moved to its own shot, Kitchen · Sink"));
    expect(toast.success).toHaveBeenCalledTimes(1);
    await waitFor(() => expect(within(strip).getAllByRole("button")).toHaveLength(2));
    await user.keyboard("{Escape}");

    // The Sink is the Kitchen's third shot, its one photo its Then.
    const shots = within(kitchen).getByRole("group", { name: "Shot" });
    expect(within(shots).getAllByRole("button").map((b) => b.textContent)).toEqual(["Kitchen", "Counters", "Sink"]);
    await user.click(within(shots).getByRole("button", { name: "Sink" }));
    expect(within(kitchen).getByTestId("photo-then")).toHaveAccessibleName(/^Compare Kitchen · Sink: Then · /);
    expect(within(kitchen).getByRole("button", { name: "Add a photo of Kitchen · Sink to compare" })).toBeVisible();

    // A shot with one photo has nothing to split off.
    await user.click(within(shots).getByRole("button", { name: "Counters" }));
    await user.click(within(kitchen).getByTestId("photo-then"));
    const counters = await screen.findByRole("dialog", { name: "Kitchen · Counters" });
    await within(counters).findByTestId("filmstrip");
    expect(within(counters).queryByRole("button", { name: "Make this its own shot" })).toBeNull();
  });

  it("adds photos from new spots as new shots, a name two photos share made once", async () => {
    // Node's own createObjectURL refuses jsdom's files; a browser's takes them.
    vi.spyOn(URL, "createObjectURL").mockReturnValue("blob:picked");
    vi.spyOn(URL, "revokeObjectURL").mockImplementation(() => {});
    const user = userEvent.setup();
    renderPage();
    const kitchen = await screen.findByRole("article", { name: "Kitchen" });
    await user.click(within(kitchen).getByRole("button", { name: "Add photos of Kitchen" }));
    const sheet = await screen.findByRole("dialog", { name: "Add photos of Kitchen" });
    const jpeg = (name: string) => new File([new Uint8Array([0xff, 0xd8, 0xff, 1])], name, { type: "image/jpeg" });
    fireEvent.change(within(sheet).getByTestId("add-photos-input"), {
      target: { files: [jpeg("IMG_1.jpg"), jpeg("IMG_2.jpg"), jpeg("IMG_3.jpg")] },
    });
    await waitFor(() => expect(within(sheet).getAllByTestId("pending-photo")).toHaveLength(3));
    const rows = within(sheet).getAllByTestId("pending-photo");

    // Every row picks its shot, the one shot a space starts with included, or a new one.
    const shot = within(rows[0]).getByLabelText("Shot");
    expect(within(shot).getAllByRole("option").map((o) => o.textContent)).toEqual(["Kitchen", "Counters", "New shot…"]);
    await user.selectOptions(shot, "New shot…");
    expect(within(sheet).getByRole("button", { name: "Add 3 photos" })).toBeDisabled();
    expect(sheet).toHaveTextContent("Name each new shot to add the photos.");
    await user.type(within(rows[0]).getByLabelText("New shot's name"), "Sink");
    await user.selectOptions(within(rows[1]).getByLabelText("Shot"), "New shot…");
    await user.type(within(rows[1]).getByLabelText("New shot's name"), "sink ");
    await user.selectOptions(within(rows[2]).getByLabelText("Shot"), "New shot…");
    await user.type(within(rows[2]).getByLabelText("New shot's name"), "counters");
    expect(rows[2]).toHaveTextContent("Kitchen has that shot already: the photo goes there.");

    await user.click(within(sheet).getByRole("button", { name: "Add 3 photos" }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Added 3 photos of Kitchen and the shot Sink"));
    expect(toast.success).toHaveBeenCalledTimes(1);
    const shots = within(kitchen).getByRole("group", { name: "Shot" });
    await waitFor(() =>
      expect(within(shots).getAllByRole("button").map((b) => b.textContent)).toEqual(["Kitchen", "Counters", "Sink"]),
    );
    await user.click(within(shots).getByRole("button", { name: "Sink" }));
    expect(within(kitchen).getByTestId("then-and-now")).toHaveTextContent(/2 photos$/);
    await user.click(within(shots).getByRole("button", { name: "Counters" }));
    expect(within(kitchen).getByTestId("then-and-now")).toHaveTextContent(/2 photos$/);
  });
});

describe("Environment page: Manage spaces (HOME-1, Chris's \"Environment Page cleanup\")", () => {
  it("is a table, one line per space, with weight, pace and share in columns and small buttons", async () => {
    renderPage();
    const table = await screen.findByRole("table", { name: "Spaces in the Home Score" });
    const headers = within(table).getAllByRole("columnheader").map((h) => h.textContent?.trim());
    expect(headers).toEqual(["Space", "Weight", "Pace", "% of score", "Actions"]);
    const rows = within(table).getAllByTestId("space-row");
    expect(rows).toHaveLength(9);

    const bedroom = within(table).getByRole("rowheader", { name: /^Bedroom/ }).closest("tr")!;
    expect(within(bedroom).getByTestId("space-weight")).toHaveTextContent("2");
    expect(within(bedroom).getByTestId("space-pace")).toHaveTextContent("1");
    expect(within(bedroom).getByTestId("space-share")).toHaveTextContent(/^\d+(\.\d)?%$/);
    for (const name of ["Shots of Bedroom", "Change Bedroom", "Archive Bedroom"]) {
      expect(within(bedroom).getByRole("button", { name })).toBeInTheDocument();
    }
  });

  it("says what weight and pace mean in tips once there are spaces, not in words", async () => {
    renderPage();
    const editor = await screen.findByRole("region", { name: "Manage spaces" });
    await within(editor).findByRole("table");
    expect(within(editor).queryByText(/its pace, how fast it gets messy/)).not.toBeInTheDocument();
    expect(within(editor).queryByText(/^Pace: how fast/)).not.toBeInTheDocument();
    // The weight help is still read with the field, out of sight.
    const weight = within(editor).getByLabelText("Weight", { selector: "input" });
    expect(weight).toHaveAccessibleDescription(/how much the space counts/);
    expect(within(editor).getAllByRole("button", { name: "About weight" }).length).toBeGreaterThan(0);

    await userEvent.hover(within(editor).getAllByRole("button", { name: "About pace" })[0]);
    expect(await screen.findByRole("tooltip")).toHaveTextContent(/how fast Tidiness and Cleanliness fade/);
  });

  it("says what weight and pace mean in words on the first view, before any space", async () => {
    clearMockEnvironment();
    renderPage();
    const editor = await screen.findByRole("region", { name: "Manage spaces" });
    expect(await within(editor).findByText(/^Weight 1–10: how much the space counts/)).toBeVisible();
    expect(within(editor).getByText(/^Pace: how fast Tidiness and Cleanliness fade/)).toBeVisible();
    expect(within(editor).queryByRole("button", { name: "About weight" })).not.toBeInTheDocument();
    expect(within(editor).queryByRole("table")).not.toBeInTheDocument();
  });

  it("changes a space in place: its row becomes the form, and Save puts the line back", async () => {
    renderPage();
    const table = await screen.findByRole("table", { name: "Spaces in the Home Score" });
    await userEvent.click(within(table).getByRole("button", { name: "Change Garage" }));
    const weight = within(table).getByLabelText("Weight", { selector: "input" });
    await userEvent.clear(weight);
    await userEvent.type(weight, "3");
    await userEvent.click(within(table).getByRole("button", { name: "Save" }));
    const garage = (await within(table).findByRole("rowheader", { name: /^Garage/ })).closest("tr")!;
    expect(within(garage).getByTestId("space-weight")).toHaveTextContent("3");
  });
});
