import { expect, test, type Page } from "@playwright/test";

/**
 * The mock pays activity XP from Oura from 2026-10-22 by the page's date (activity plan A9), and
 * the Food Log and Character pages ask for that sync on opening, so Health's numbers would move
 * with the real date. The paths that read them run on the day before activity XP started.
 */
async function beforeActivityXp(page: Page) {
  await page.clock.install({ time: new Date("2026-10-21T12:00:00") });
}

/** A 2400 x 1600 PNG, as a phone might take: the browser shrinks it to 1,200 px. */
async function bigPng(page: Page): Promise<Buffer> {
  return Buffer.from(
    await page.evaluate(() => {
      const canvas = document.createElement("canvas");
      canvas.width = 2400;
      canvas.height = 1600;
      const g = canvas.getContext("2d")!;
      g.fillStyle = "#c2410c";
      g.fillRect(0, 0, 2400, 1600);
      g.fillStyle = "#fde68a";
      g.beginPath();
      g.arc(1200, 800, 500, 0, Math.PI * 2);
      g.fill();
      return canvas.toDataURL("image/png").split(",")[1];
    }),
    "base64",
  );
}

test.describe("golden paths", () => {
  test("create via New dialog", async ({ page }) => {
    await page.goto("/");
    await page.getByRole("button", { name: "New" }).click();
    await expect(page.getByRole("dialog")).toBeVisible();
    await page.getByRole("textbox", { name: "Title" }).fill("I finish what I start");
    // An Identity Statement needs its statement (subject-forms).
    await page.getByRole("textbox", { name: "Statement" }).fill("I'm the type of person who ships.");
    await page.getByRole("button", { name: "Save" }).click();
    await expect(page.getByRole("dialog")).toBeHidden({ timeout: 10_000 });
  });

  test("inline create-child in Map", async ({ page }) => {
    await page.goto("/map");
    await expect(page.getByRole("tree", { name: "Alignment outline" })).toBeVisible();
    await page.getByRole("button", { name: "Run a half-marathon", exact: true }).click();
    await page.keyboard.press("Enter");
    const title = page.getByLabel(/New (project|task|goal)/i);
    await expect(title).toBeVisible();
    await title.fill("Track Sunday long runs");
    await page.getByRole("button", { name: "Add", exact: true }).click();
    await expect(page.getByRole("button", { name: "Track Sunday long runs", exact: true })).toBeVisible({
      timeout: 10_000,
    });
  });

  test("the Map hides finished work until Show asks for it", async ({ page }) => {
    await page.goto("/map");
    const outline = page.getByRole("tree", { name: "Alignment outline" });
    await expect(outline).toBeVisible();
    const expandAll = page.getByRole("switch", { name: "Expand all" });
    if ((await expandAll.getAttribute("aria-checked")) !== "true") await expandAll.click();
    // The active project shows (under each of its parents); its completed task stays hidden
    // until Show includes done steps.
    await expect(
      outline.getByRole("button", { name: "Production web UI — first pass", exact: true }).first(),
    ).toBeVisible();
    const done = outline.getByRole("button", { name: "Mirror production-ui-types.ts", exact: true });
    await expect(done).toHaveCount(0);
    await expect(page.getByTestId("map-hidden-count")).toHaveText("1 finished hidden");

    await page.getByRole("combobox", { name: "Show" }).click();
    await page.getByRole("option", { name: "Open + done steps" }).click();
    await expect(done.first()).toBeVisible();
    await expect(page.getByTestId("map-hidden-count")).toHaveCount(0);

    // The choice is remembered.
    await page.goto("/map");
    await expect(done.first()).toBeVisible();
  });

  test("edit and save a subject", async ({ page }) => {
    await page.goto("/map?selected=goal-lifeos");
    await expect(page.getByRole("heading", { name: /Ship LifeOS production UI/ })).toBeVisible();
    await page.getByRole("button", { name: "Edit" }).click();
    const description = page.getByLabel("Description");
    await description.fill("Polished across every interface.");
    await page.getByRole("button", { name: "Save" }).click();
    await expect(page.getByRole("button", { name: "Edit" })).toBeVisible({ timeout: 10_000 });
  });

  test("a repeat pattern is saved in the kernel's shape and read back", async ({ page }) => {
    await page.goto("/map?selected=cmt-inbox");
    await expect(page.getByRole("heading", { name: /Reach Inbox Zero each weekday/ })).toBeVisible();
    await expect(page.getByText("Weekdays", { exact: true })).toBeVisible();
    await page.getByRole("button", { name: "Edit" }).click();
    const days = page.getByRole("group", { name: "On these days" });
    for (const day of ["Monday", "Tuesday", "Thursday", "Friday"]) {
      await days.getByRole("button", { name: day }).click();
    }
    await expect(days.getByRole("button", { name: "Wednesday" })).toHaveAttribute("aria-pressed", "true");
    await page.getByRole("button", { name: "Save" }).click();
    await expect(page.getByRole("button", { name: "Edit" })).toBeVisible({ timeout: 10_000 });
    await expect(page.getByRole("tabpanel", { name: "Overview" }).getByText("Every Wednesday", { exact: true })).toBeVisible();

    await page.getByRole("button", { name: "Edit" }).click();
    await page.getByRole("group", { name: "Repeat" }).getByRole("button", { name: "Doesn't repeat" }).click();
    await page.getByRole("button", { name: "Save" }).click();
    await expect(page.getByRole("button", { name: "Edit" })).toBeVisible({ timeout: 10_000 });
    await expect(page.getByRole("tabpanel", { name: "Overview" }).getByText("Every Wednesday", { exact: true })).toBeHidden();
  });

  test("a repeating task: make one, finish today's, the next appears, and mark a late one missed", async ({
    page,
  }) => {
    // Wednesday 21 October 2026: a weekly Wednesday repeat from the 14th has a late one and today's.
    await page.clock.install({ time: new Date("2026-10-21T12:00:00") });
    await page.addInitScript(() => localStorage.removeItem("lifeos.view.tasks"));
    await page.goto("/tasks");
    await page.getByRole("textbox", { name: "New task title" }).fill("Water plants");
    await page.getByRole("button", { name: "More…" }).click();
    const dialog = page.getByRole("dialog");
    await expect(dialog).toBeVisible();
    await dialog.getByRole("group", { name: "Repeat" }).getByRole("button", { name: "Weekly" }).click();
    await expect(
      dialog.getByRole("group", { name: "On these days" }).getByRole("button", { name: "Wednesday" }),
    ).toHaveAttribute("aria-pressed", "true");
    // A repeating Task's dates come from its repetitions, so its own due date is hidden.
    await expect(dialog.getByLabel("Due", { exact: true })).toHaveCount(0);
    await dialog.getByLabel("Repeats from").fill("2026-10-14");
    await dialog.getByRole("button", { name: "Save" }).click();
    await expect(dialog).toBeHidden({ timeout: 10_000 });

    const group = (name: string) => page.locator("section", { has: page.getByRole("heading", { name }) });
    const overdue = group(/^Overdue/).getByRole("button", { name: /Water plants/ });
    const today = group(/^Today/).getByRole("button", { name: /Water plants/ });
    await expect(overdue).toContainText("Every Wednesday", { timeout: 10_000 });
    await expect(overdue).toContainText("7 days overdue");
    await expect(today).toContainText("Every Wednesday");

    // Finish today's: it pays, and next Wednesday's is made.
    await today.click();
    const detail = page.locator("aside").filter({ has: page.getByRole("button", { name: "Close panel" }) });
    const repeats = detail.getByRole("region", { name: "Repeats" });
    await expect(repeats).toContainText("Every Wednesday");
    await expect(repeats.getByRole("list", { name: "Repetitions" }).getByRole("listitem")).toHaveCount(2);
    await detail.getByRole("button", { name: "Edit" }).click();
    await detail.getByRole("combobox", { name: "Status" }).click();
    await page.getByRole("option", { name: "Completed", exact: true }).click();
    await detail.getByRole("button", { name: "Save" }).click();
    await expect(detail.getByRole("button", { name: "Edit" })).toBeVisible({ timeout: 10_000 });
    await expect(group(/^Upcoming/).getByRole("button", { name: /Water plants/ })).toContainText("Oct 28", {
      timeout: 10_000,
    });

    // Close the late one myself: missed, not paid, and gone from the working view.
    await overdue.click();
    await expect(detail.getByRole("region", { name: "Timer" })).toBeVisible();
    await expect(repeats.getByRole("listitem").filter({ hasText: "This one" })).toContainText("due Oct 14");
    await repeats.getByRole("button", { name: "Mark missed" }).click();
    await expect(page.getByText("Marked missed")).toBeVisible({ timeout: 10_000 });
    await expect(group(/^Overdue/).getByRole("button", { name: /Water plants/ })).toHaveCount(0);

    // The Repeating view lists the task itself, with how it's gone.
    await page.getByRole("combobox", { name: "Status" }).first().click();
    await page.getByRole("option", { name: "Repeating", exact: true }).click();
    const series = group(/^Repeating/).getByRole("button", { name: /Water plants/ });
    await expect(series).toContainText("1 of 2 done");
    await expect(series).toContainText("Oct 28");
    await series.click();
    await expect(repeats.getByRole("button", { name: "Stop repeating" })).toBeVisible();
    // The task itself isn't timed; its repetitions are.
    await expect(detail.getByRole("region", { name: "Timer" })).toHaveCount(0);
  });

  test("time a task, stop for now and start again, then Complete only after it asks", async ({ page }) => {
    await page.goto("/browse?type=Task&selected=task-shoes");
    await expect(page.getByRole("heading", { name: "Buy race shoes" })).toBeVisible({ timeout: 10_000 });
    const timer = page.getByRole("region", { name: "Timer", exact: true });
    await timer.getByRole("button", { name: "Start" }).click();
    await expect(timer.getByText("Running", { exact: true })).toBeVisible();

    // Stop ends the session and leaves the task open.
    await timer.getByRole("button", { name: "Stop" }).click();
    await timer.getByRole("button", { name: "Start" }).click();
    await expect(timer.getByText("Running", { exact: true })).toBeVisible();

    // Complete closes it, so it asks first; Cancel changes nothing.
    await timer.getByRole("button", { name: "Complete" }).click();
    const ask = page.getByRole("alertdialog", { name: "Complete Buy race shoes?" });
    await expect(ask).toContainText("use Stop instead");
    await ask.getByRole("button", { name: "Cancel" }).click();
    await expect(timer.getByText("Running", { exact: true })).toBeVisible();
    await timer.getByRole("button", { name: "Complete" }).click();
    await ask.getByRole("button", { name: "Complete" }).click();

    // A closed task isn't timed until it's reopened.
    await expect(timer.getByText("Reopen the task to time it.")).toBeVisible({ timeout: 10_000 });
    await expect(timer.getByRole("button", { name: "Start" })).toHaveCount(0);
  });

  test("a repeating task is one node on the Map, and a review counts its weeks", async ({ page }) => {
    await page.clock.install({ time: new Date("2026-10-21T12:00:00") });
    await page.goto("/map");
    const outline = page.getByRole("tree", { name: "Alignment outline" });
    await expect(outline).toBeVisible();
    const expandAll = page.getByRole("switch", { name: "Expand all" });
    if ((await expandAll.getAttribute("aria-checked")) !== "true") await expandAll.click();
    // Saturday's shop serves the Sunday dinner project: once, not once a week.
    const title = outline.getByRole("button", { name: "Shop for Sunday dinner", exact: true });
    await expect(title).toHaveCount(1);
    const shop = page.locator('[data-map-node="task-dinner-shop"]');
    await expect(shop).toContainText("Every Saturday");
    await expect(shop).toContainText("Repeating");
    await title.click();
    await expect(page.getByRole("region", { name: "Repeats" }).getByRole("button", { name: "Stop repeating" })).toBeVisible({
      timeout: 10_000,
    });

    // The week before: the trash and the shop each done once.
    await page.goto("/reviews?kind=weekly");
    await page.getByRole("button", { name: "Previous" }).click();
    const repeating = page.locator("div", { has: page.getByRole("heading", { name: "Repeating tasks" }) }).last();
    await expect(repeating).toContainText("Take out trash — 1 of 1 done", { timeout: 10_000 });
    await expect(repeating).toContainText("Shop for Sunday dinner — 1 of 1 done");
  });

  test("Inbox keyboard triage: arrows, Enter, Drop confirm", async ({ page }) => {
    await page.goto("/inbox");
    await expect(page.getByRole("heading", { name: "Inbox" })).toBeVisible();
    await page.keyboard.press("ArrowDown");
    await page.getByRole("button", { name: "Drop" }).focus();
    await page.keyboard.press("Enter");
    await expect(page.getByRole("alertdialog")).toBeVisible();
    await page.getByRole("button", { name: "Drop" }).last().click();
    await expect(page.getByRole("alertdialog")).toBeHidden({ timeout: 10_000 });
  });

  test("graph node click opens detail peek", async ({ page }) => {
    await page.goto("/map?view=graph");
    const node = page.getByText("Craftsman of systems", { exact: false }).first();
    await expect(node).toBeVisible({ timeout: 20_000 });
    await node.click();
    await expect(page.getByRole("heading", { name: /Craftsman of systems/ })).toBeVisible({
      timeout: 10_000,
    });
  });

  test("quick capture lands in Inbox", async ({ page }) => {
    await page.goto("/inbox");
    await expect(page.getByRole("heading", { name: "Inbox" })).toBeVisible();
    await page.getByRole("button", { name: "Capture", exact: true }).click();
    const dialog = page.getByRole("dialog", { name: "Capture" });
    await expect(dialog).toBeVisible();
    const note = dialog.getByPlaceholder("A note…");
    await note.fill("Standing desk trial this week.");
    await expect(note).toHaveValue("Standing desk trial this week.");
    await dialog.getByRole("button", { name: "Capture" }).click();
    await expect(page.getByText("Captured to Inbox")).toBeVisible({ timeout: 10_000 });
    await expect(page.getByText("Standing desk trial this week.")).toBeVisible({
      timeout: 10_000,
    });
  });

  test("redeem a catalog reward with a note", async ({ page }) => {
    await page.goto("/rewards");
    const freedom = page.getByRole("listitem", { name: /^Freedom: / });
    await expect(freedom).toHaveAccessibleName("Freedom: 60");
    // Movie night costs more Freedom than the wallet holds: blocked, with the reason.
    await expect(page.getByRole("button", { name: "Redeem Movie night" })).toBeDisabled();
    await expect(page.getByText("You have 60 Freedom; this needs 120.")).toBeVisible();

    await page.getByRole("button", { name: "Redeem Episode of a show" }).click();
    const dialog = page.getByRole("dialog", { name: "Redeem Episode of a show" });
    await expect(dialog).toBeVisible();
    await dialog.getByLabel(/What's it for/).fill("Season finale, part two");
    await dialog.getByRole("button", { name: "Redeem", exact: true }).click();

    await expect(page.getByTestId("confetti")).toBeAttached();
    await expect(page.getByText("Redeemed Episode of a show: 45 Freedom")).toBeVisible({ timeout: 10_000 });
    await expect(dialog).toBeHidden();
    await expect(freedom).toHaveAccessibleName("Freedom: 15");
    await expect(page.getByText("Episode of a show — Season finale, part two")).toBeVisible();
  });

  test("earn on a habit day, then establish a habit that met its target", async ({ page }) => {
    await page.goto("/habits");
    const inbox = page.getByRole("list", { name: "Building" }).getByRole("listitem").filter({ hasText: "Inbox Zero pass" });
    await expect(inbox.getByText("Pays 5 Freedom · 5 Life XP a day")).toBeAttached();
    await inbox.getByRole("button", { name: "Followed", exact: true }).click();
    await expect(page.getByText("Earned +5 Freedom +5 Life XP")).toBeVisible({ timeout: 10_000 });

    await page.getByRole("button", { name: "Ready to establish Morning mobility" }).click();
    const confirm = page.getByRole("alertdialog", { name: "Establish Morning mobility?" });
    await expect(confirm).toContainText("pays 60 Freedom · 1 Gems · 100 Life XP");
    await confirm.getByRole("button", { name: "Establish" }).click();

    await expect(page.getByText("Earned +60 Freedom +1 Gems +100 Life XP")).toBeVisible({ timeout: 10_000 });
    const graduated = page.getByRole("region", { name: "Established" });
    await expect(graduated.getByText("Morning mobility")).toBeVisible();
    await expect(page.getByRole("list", { name: "Building" })).not.toContainText("Morning mobility");

    // Maintained, not tracked: a one-tap check-off, nothing missed, and a second tap undoes it.
    const mobility = graduated.getByRole("listitem").filter({ hasText: "Morning mobility" });
    await expect(mobility.getByText("Not logged today")).toBeVisible();
    await expect(mobility.getByRole("button", { name: /not followed/i })).toHaveCount(0);
    const checkOff = mobility.getByRole("button", { name: "Morning mobility: followed today" });
    await checkOff.click();
    await expect(mobility.getByText("Done today")).toBeVisible({ timeout: 10_000 });
    await expect(checkOff).toHaveAttribute("aria-pressed", "true");
    await checkOff.click();
    await expect(mobility.getByText("Not logged today")).toBeVisible({ timeout: 10_000 });
  });

  test("Today keeps established habits in a collapsed Maintaining group", async ({ page }) => {
    await page.goto("/habits");
    await page.getByRole("button", { name: "Ready to establish Morning mobility" }).click();
    await page
      .getByRole("alertdialog", { name: "Establish Morning mobility?" })
      .getByRole("button", { name: "Establish" })
      .click();
    await expect(page.getByRole("region", { name: "Established" }).getByText("Morning mobility")).toBeVisible({
      timeout: 10_000,
    });

    // In-app navigation keeps the mock's state.
    await page.getByRole("link", { name: "Focus" }).first().click();
    const toggle = page.getByRole("button", { name: /Maintaining/ });
    await expect(toggle).toHaveText(/0 of 1 done today/);
    await expect(toggle).toHaveAttribute("aria-expanded", "false");
    await toggle.click();
    const group = page.getByRole("list", { name: "Maintaining" });
    await group.getByRole("button", { name: "Morning mobility: followed today" }).click();
    await expect(group.getByText("Done today")).toBeVisible({ timeout: 10_000 });
    await expect(toggle).toHaveText(/1 of 1 done today/);
  });

  test("a habit day earned today shows in the rewards history, and opens its habit", async ({ page }) => {
    await page.goto("/habits");
    const inbox = page.getByRole("list", { name: "Building" }).getByRole("listitem").filter({ hasText: "Inbox Zero pass" });
    await inbox.getByRole("button", { name: "Followed", exact: true }).click();
    await expect(page.getByText("Earned +5 Freedom +5 Life XP")).toBeVisible({ timeout: 10_000 });

    // In-app navigation keeps the mock's state.
    await page.getByRole("link", { name: "Rewards" }).first().click();
    await page.getByRole("button", { name: "See all history" }).click();
    await expect(page).toHaveURL(/view=history/);

    const today = page.getByRole("button", { name: /\(today\): earned / });
    await today.click();
    await expect(today).toHaveAttribute("aria-pressed", "true");
    const entries = page.getByRole("list", { name: /^Entries for / });
    const entry = entries.getByRole("button", { name: "Inbox Zero pass" });
    await expect(entry.first()).toBeVisible({ timeout: 10_000 });
    await entry.first().click();
    await expect(page).toHaveURL(/selected=habit-inbox/);
    await expect(page.getByRole("heading", { name: /Inbox Zero pass/ }).last()).toBeVisible({ timeout: 10_000 });
  });

  test("following a habit day lifts Health a rank, celebrated, and the Character page shows it", async ({ page }) => {
    await beforeActivityXp(page);
    await page.goto("/character");
    const health = page.getByRole("region", { name: "Health", exact: true });
    await expect(health.getByText("Level 9", { exact: true })).toBeVisible({ timeout: 10_000 });
    await expect(health.getByText("Novice", { exact: true })).toBeVisible();

    // In-app navigation keeps the mock's state. The seeded Health history is 33 XP short of
    // level 10, and its last 90 days already support more: today's 90 base XP (108 at ×1.2) reaches it.
    await page.getByRole("link", { name: "Habits" }).first().click();
    const mobility = page.getByRole("list", { name: "Building" }).getByRole("listitem").filter({ hasText: "Morning mobility" });
    await mobility.getByRole("button", { name: "Followed", exact: true }).click();
    await expect(page.getByText("You're now a Health Apprentice.")).toBeVisible({ timeout: 10_000 });

    await page.getByRole("link", { name: "Character" }).first().click();
    await expect(health.getByText("Level 10", { exact: true })).toBeVisible({ timeout: 10_000 });
    await expect(health.getByText("Apprentice", { exact: true })).toBeVisible();
    await expect(health.getByRole("img", { name: "Today: 90 of 20 action XP, active" })).toBeVisible();
    const ranks = page.getByRole("region", { name: "Health ranks" }).getByRole("listitem");
    await expect(ranks.nth(1)).toHaveAttribute("aria-current", "step");
  });

  test("Character Reports show Health's rank over time and each period's figures", async ({ page }) => {
    await beforeActivityXp(page);
    await page.goto("/character");
    await page.getByRole("tab", { name: "Reports" }).click();
    await expect(page).toHaveURL(/view=reports/);
    const chart = page.getByTestId("rank-chart");
    await expect(chart).toHaveAccessibleName(/^Rank over the last 30 days\. Health: Novice throughout, best Novice\./, {
      timeout: 10_000,
    });
    // The seeded sick day, 14 days ago, is hatched.
    await expect(page.getByTestId("exception-band")).toHaveCount(1);

    await page.getByRole("button", { name: "3 months" }).click();
    await expect(chart).toHaveAccessibleName(/^Rank over the last 3 months\./, { timeout: 10_000 });
    await expect(page.getByRole("heading", { name: "By domain, last 3 months" })).toBeVisible();
    await page.getByRole("button", { name: "Table", exact: true }).click();
    await expect(page.getByRole("region", { name: "Rank over the last 3 months, as a table" })).toBeVisible();
  });

  test("mark today as an exception on the Character page, then unmark it", async ({ page }) => {
    await beforeActivityXp(page);
    await page.goto("/character");
    const section = page.getByRole("region", { name: "Exception days" });
    const marks = section.getByRole("list", { name: "Exception days, last 90 days" }).getByRole("listitem");
    await expect(marks).toHaveCount(1, { timeout: 10_000 });
    await expect(marks.first()).toContainText("Sick with a cold");

    await section.getByRole("button", { name: "Mark a day as an exception" }).click();
    const dialog = page.getByRole("dialog", { name: "Mark a day as an exception" });
    await expect(dialog.getByRole("radio", { name: "Today" })).toBeChecked();
    await dialog.getByRole("textbox", { name: "Why" }).fill("Flying home");
    await dialog.getByRole("button", { name: "Mark as exception" }).click();
    await expect(dialog).toBeHidden();

    await expect(marks).toHaveCount(2);
    await expect(marks.first()).toContainText("Flying home");
    const health = page.getByRole("region", { name: "Health", exact: true });
    await expect(
      health.getByRole("img", { name: "Today is an exception day: it doesn't count for or against you." }),
    ).toBeVisible();

    await marks.first().getByRole("button", { name: /^Unmark / }).click();
    await expect(marks).toHaveCount(1);
    await expect(health.getByRole("img", { name: /^Today: 0 of 20 action XP/ })).toBeVisible();
  });

  test("record a fast, log food and close the day for diet XP", async ({ page }) => {
    await beforeActivityXp(page);
    await page.goto("/food");
    await expect(page.getByRole("heading", { name: "Today", level: 1 })).toBeVisible({ timeout: 10_000 });

    // A 23-hour fast ending now: 7 base Health XP, 8 at the mock's ×1.2 today.
    await page.getByRole("button", { name: "Record a past fast" }).click();
    const fast = page.getByRole("dialog", { name: "Record a past fast" });
    const began = await page.evaluate(() => {
      const d = new Date(Date.now() - 23 * 3_600_000);
      const pad = (n: number) => String(n).padStart(2, "0");
      return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
    });
    await fast.getByLabel("Began").fill(began);
    await expect(fast.getByText("A fast of 23 h 00 m")).toBeVisible();
    await fast.getByRole("button", { name: "Save fast" }).click();
    await expect(page.getByText("Earned +8 Health XP")).toBeVisible({ timeout: 10_000 });
    await expect(page.getByRole("list", { name: "Fasts that ended this day" })).toContainText("23 h 00 m");

    // A very healthy 1,200-calorie entry: 28 for quality and 4 for calories, with the fast's 7.
    await page.getByRole("button", { name: "Log food", exact: true }).click();
    const sheet = page.getByRole("dialog", { name: "Log food" });
    await sheet.getByLabel("Name").fill("Salmon salad");
    await sheet.getByRole("radio", { name: "5 · very healthy" }).check();
    await sheet.getByLabel("Calories").fill("1200");
    await sheet.getByRole("button", { name: "Log", exact: true }).click();
    await expect(sheet).toBeHidden();
    await expect(page.getByText("On track for 39 of 40")).toBeVisible({ timeout: 10_000 });
    await expect(page.getByRole("list", { name: "Entries for today" }).getByRole("button", { name: /^Salmon salad/ })).toBeVisible();

    // Closing the day pays the food: 32 base, 38 at ×1.2.
    await page.getByRole("button", { name: "Close day" }).click();
    await expect(page.getByText("Earned +38 Health XP")).toBeVisible({ timeout: 10_000 });
    await expect(page.getByText("39 base → 46 Health XP (×1.2)")).toBeVisible();
  });

  test("Food Log: rate an entry 3.5 on a phone, see it pay halfway, and log it again", async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 812 });
    await beforeActivityXp(page);
    await page.goto("/food");
    await expect(page.getByRole("heading", { name: "Today", level: 1 })).toBeVisible({ timeout: 10_000 });

    // The halves sit in a row of smaller buttons, each under the gap between its neighbours.
    await page.getByRole("button", { name: "Log food", exact: true }).click();
    const sheet = page.getByRole("dialog", { name: "Log food" });
    // All three read at the same instant: the sheet is still sliding up, so boxes read one after
    // another can each be at a different point of the slide.
    const [three, half, four] = await sheet.evaluate((el) =>
      ["3 · average", "3.5 · average to good", "4 · good"].map((name) => {
        const r = el.querySelector(`input[aria-label="${name}"]`)!.closest("label")!.getBoundingClientRect();
        return { x: r.x, y: r.y, width: r.width, height: r.height };
      }),
    );
    expect(half.y).toBeGreaterThan(three.y + three.height - 1);
    expect(half.width).toBeGreaterThanOrEqual(44);
    expect(half.width).toBeLessThan(three.width);
    expect(Math.abs(half.x + half.width / 2 - (three.x + three.width + four.x) / 2)).toBeLessThan(2);

    // 1,200 calories at 3.5: 17.5 for quality and 4 for calories, on track for 22.
    await sheet.getByLabel("Name").fill("Turkey wrap");
    await sheet.getByRole("radio", { name: "3.5 · average to good" }).check();
    await expect(sheet.getByText("Average to good: halfway between 3 and 4.")).toBeVisible();
    await sheet.getByLabel("Calories").fill("1200");
    await sheet.getByRole("button", { name: "Log", exact: true }).click();
    await expect(sheet).toBeHidden();
    await expect(page.getByText("On track for 22 of 40")).toBeVisible({ timeout: 10_000 });
    const entries = page.getByRole("list", { name: "Entries for today" });
    await expect(entries.getByRole("button", { name: /^Turkey wrap, / })).toContainText("3.5 · average to good");

    // Log again keeps the half. On a phone the "Logged" toast sits over the Log food button
    // until it goes; a toast under the pointer waits, so the pointer moves off first.
    await page.mouse.move(0, 0);
    await expect(page.getByText("Logged Turkey wrap")).toBeHidden({ timeout: 10_000 });
    await page.getByRole("button", { name: "Log food", exact: true }).click();
    await sheet.getByRole("button", { name: "Log again: Turkey wrap · 3.5 · 1,200 cal" }).click();
    await expect(sheet.getByRole("radio", { name: "3.5 · average to good" })).toBeChecked();
  });

  test("Food Log: log drinks with the score locked, see the cut and the streak, and count them in the week", async ({
    page,
  }) => {
    await beforeActivityXp(page);
    await page.goto("/food");
    await expect(page.getByRole("heading", { name: "Today", level: 1 })).toBeVisible({ timeout: 10_000 });
    await expect(page.getByRole("region", { name: "Sobriety streak" })).toContainText(/\d+ days sober/);

    // An alcoholic drink scores 1, greyed out, and is typed as a drink.
    await page.getByRole("button", { name: "Log food", exact: true }).click();
    const sheet = page.getByRole("dialog", { name: "Log food" });
    await sheet.getByLabel("Name").fill("IPA");
    await sheet.getByRole("radio", { name: "4 · good" }).check();
    await sheet.getByRole("switch", { name: "Alcoholic drink" }).click();
    await expect(sheet.getByRole("radio", { name: "1 · junk" })).toBeChecked();
    await expect(sheet.getByRole("radio", { name: "4 · good" })).toBeDisabled();
    await expect(sheet.getByText("A drink always scores 1.")).toBeVisible();
    await expect(sheet.getByLabel("Meal type")).toHaveValue("");
    await expect(sheet.getByRole("option", { name: "Drink (it's alcoholic)" })).toBeAttached();
    await sheet.getByLabel("Calories").fill("200");
    await sheet.getByRole("button", { name: "Log", exact: true }).click();
    await expect(sheet).toBeHidden();
    const entries = page.getByRole("list", { name: "Entries for today" });
    await expect(entries.getByRole("button", { name: /^IPA \(alcoholic drink\), / })).toBeVisible({ timeout: 10_000 });
    await expect(page.getByRole("region", { name: "Sobriety streak" })).toContainText("A drink today");

    // Three more with Log again: four drinks take 40% off the day.
    for (let i = 0; i < 3; i++) {
      await page.getByRole("button", { name: "Log food", exact: true }).click();
      await sheet.getByRole("button", { name: "Log again: IPA · drink · 200 cal" }).click();
      await expect(sheet.getByRole("switch", { name: "Alcoholic drink" })).toBeChecked();
      await sheet.getByRole("button", { name: "Log", exact: true }).click();
      await expect(sheet).toBeHidden();
    }
    await expect(page.getByText("4 drinks · 40% off the day's diet XP")).toBeVisible({ timeout: 10_000 });

    // The four show as one card (FOOD-7). Have another adds a fifth; deleting a copy takes it back.
    const card = entries.getByRole("button", { name: /^IPA ×4 \(alcoholic drink\), .+: show the 4 entries$/ });
    await expect(card).toContainText("800 cal");
    await card.click();
    const copies = page.getByRole("dialog", { name: "IPA ×4" });
    await expect(copies.getByRole("list", { name: "Each IPA" }).getByRole("button")).toHaveCount(4);
    await copies.getByRole("button", { name: "Have another" }).click();
    await expect(sheet.getByLabel("Name")).toHaveValue("IPA");
    await expect(sheet.getByRole("switch", { name: "Alcoholic drink" })).toBeChecked();
    await expect(sheet.getByLabel("Calories")).toHaveValue("200");
    await sheet.getByRole("button", { name: "Log", exact: true }).click();
    await expect(sheet).toBeHidden();
    await expect(page.getByText("5 drinks · 50% off the day's diet XP")).toBeVisible({ timeout: 10_000 });

    await entries.getByRole("button", { name: /^IPA ×5 / }).click();
    await page.getByRole("dialog", { name: "IPA ×5" }).getByRole("list", { name: "Each IPA" }).getByRole("button").first().click();
    const change = page.getByRole("dialog", { name: "Change IPA" });
    await change.getByRole("button", { name: "Delete" }).click();
    await page.getByRole("alertdialog").getByRole("button", { name: "Delete" }).click();
    await expect(change).toBeHidden();
    await expect(entries.getByRole("button", { name: /^IPA ×4 / })).toBeVisible({ timeout: 10_000 });
    await expect(page.getByText("4 drinks · 40% off the day's diet XP")).toBeVisible();

    // The week counts them, and the streak as of today.
    await page.getByRole("group", { name: "View" }).getByRole("button", { name: "Week" }).click();
    const totals = page.getByRole("definition");
    await expect(totals.filter({ hasText: /^4 drinks$/ })).toBeVisible({ timeout: 10_000 });
    await expect(totals.filter({ hasText: /^Drink-free \d+ of \d+ days$/ })).toBeVisible();
    await expect(totals.filter({ hasText: /^A drink today · best \d+ days$/ })).toBeVisible();
    await page.getByRole("button", { name: "Table", exact: true }).click();
    await expect(page.getByRole("row", { name: /\(today\)/ })).toContainText(/4$/);
  });

  test("Food Log: switch to the week, step back a month, and open a day", async ({ page }) => {
    await beforeActivityXp(page);
    await page.goto("/food");
    await expect(page.getByRole("heading", { name: "Today", level: 1 })).toBeVisible({ timeout: 10_000 });

    await page.getByRole("group", { name: "View" }).getByRole("button", { name: "Week" }).click();
    await expect(page).toHaveURL(/view=week/);
    await expect(page.getByRole("heading", { name: /^Week of /, level: 1 })).toBeVisible();
    const days = page.getByRole("list", { name: "Diet XP each day, out of 40" }).getByRole("button");
    await expect(days).toHaveCount(7);
    await expect(page.getByRole("definition").filter({ hasText: /^\d+ of \d+$/ })).toBeVisible();

    // The month view holds every seeded day (from six days back); two days back paid 46.
    await page.getByRole("group", { name: "View" }).getByRole("button", { name: "Month" }).click();
    const twoBack = new Date();
    twoBack.setDate(twoBack.getDate() - 2);
    if (twoBack.getMonth() !== new Date().getMonth()) {
      await page.getByRole("button", { name: "Previous month" }).click();
    }
    const day = page.getByRole("group", { name: "Diet XP each day" }).getByRole("button", { name: /: 46 Health XP paid \(39 base\)/ });
    await expect(day).toBeVisible({ timeout: 10_000 });
    await day.click();

    await expect(page).not.toHaveURL(/view=/);
    await expect(page.getByText("39 base → 46 Health XP (×1.2)")).toBeVisible({ timeout: 10_000 });
    await expect(page.getByRole("list", { name: /^Entries for / }).getByRole("listitem")).toHaveCount(2);
  });

  test("Activity: steps over 90 days as a table, the workouts not counted, and a note on a run", async ({ page }) => {
    await page.goto("/activity");
    const steps = page.getByRole("region", { name: "Steps" });
    await expect(steps.getByRole("group", { name: /^Steps by day, last 30 days/ })).toBeVisible({ timeout: 10_000 });
    await expect(page.getByText(/^Synced from Oura /)).toBeVisible();

    // Steps: 90 days, then the Table, a row a day.
    await steps.getByRole("button", { name: "90 days" }).click();
    await expect(steps.getByRole("group", { name: /^Steps by day, last 90 days/ })).toBeVisible();
    await steps.getByRole("button", { name: "Table" }).click();
    await expect(steps.getByTestId("metric-row")).toHaveCount(90);
    await expect(steps.getByTestId("metric-row").first()).toContainText("Today");

    // Exercise: the workouts that don't count, each with its reason.
    const exercise = page.getByRole("region", { name: "Exercise" });
    await expect(exercise.getByTestId("workout-row").first()).toBeVisible();
    await expect(exercise.getByText(/^Not counted: /)).toHaveCount(0);
    await exercise.getByRole("button", { name: /^Show \d+ not counted$/ }).click();
    await expect(exercise.getByText("Not counted: A walk: it's in your steps").first()).toBeVisible();
    await expect(exercise.getByText("Not counted: Detected by Oura").first()).toBeVisible();

    // A note on a run, saved on Enter, under its row once the sheet closes.
    const run = exercise
      .getByTestId("workout-row")
      .filter({ hasText: "Running" })
      .filter({ hasNot: page.getByTestId("workout-note") })
      .first();
    await run.click();
    const sheet = page.getByRole("dialog");
    await expect(sheet.getByText("Counts as exercise.")).toBeVisible();
    await sheet.getByRole("textbox", { name: "Note" }).fill("Negative splits, felt easy");
    await sheet.getByRole("textbox", { name: "Note" }).press("Enter");
    await expect(sheet.getByText("Saved")).toBeVisible();
    await page.keyboard.press("Escape");
    await expect(sheet).toBeHidden();
    await expect(exercise.getByTestId("workout-note").filter({ hasText: "Negative splits, felt easy" })).toBeVisible();
  });

  test("Activity: tags from the Oura app counted, over 90 days, and a day's tags in its sheet", async ({ page }) => {
    await page.goto("/activity");
    const tags = page.getByRole("region", { name: "Tags", exact: true });
    await expect(tags.getByTestId("tag-counts")).toContainText(/Caffeine \d+ · Alcohol \d+/, { timeout: 10_000 });
    await expect(tags.getByTestId("tag-day")).toHaveCount(7);

    // 90 days: the few days of a cold a month back come into the counts.
    await tags.getByRole("button", { name: "90 days" }).click();
    await expect(tags.getByTestId("tag-counts")).toContainText("in the last 90 days");
    await expect(tags.getByTestId("tag-counts")).toContainText("Sick 1");
    await expect(tags.getByRole("button", { name: "90 days" })).toHaveAttribute("aria-pressed", "true");

    // A day from its heading: its tags in the sheet, earliest first, the comment under its tag.
    await tags.getByTestId("tag-day").nth(2).getByRole("button", { name: /, open the day$/ }).click();
    const sheet = page.getByRole("dialog");
    await expect(sheet.getByRole("heading", { name: "2 tags" })).toBeVisible();
    await expect(sheet.getByRole("list", { name: "Tags from the Oura app" }).getByTestId("day-tag")).toHaveText([
      /^Caffeine/,
      /^Alcohol.*Two IPAs with dinner$/,
    ]);
    await page.keyboard.press("Escape");
    await expect(sheet).toBeHidden();
  });

  test("Activity: last night, the sleep chart as Readiness, and a night in full with its contributors", async ({ page }) => {
    await page.goto("/activity");
    const sleep = page.getByRole("region", { name: "Sleep and readiness", exact: true });
    await expect(sleep.getByRole("list", { name: "Stages" })).toBeVisible({ timeout: 10_000 });
    await expect(sleep.getByRole("group", { name: /^Sleep score by day, last 30 days/ })).toBeVisible();

    // Switch the sleep chart to Readiness: Oura's lines at 70 and 85 stay.
    await sleep.getByRole("button", { name: "Readiness", exact: true }).click();
    await expect(sleep.getByRole("group", { name: /^Readiness by day, last 30 days/ })).toBeVisible();
    await expect(sleep.getByText("85 optimal")).toBeVisible();

    // A night from the Table, in full: the night, and both scores' contributors as meters.
    await sleep.getByRole("button", { name: "Table" }).click();
    await expect(sleep.getByTestId("metric-row")).toHaveCount(30);
    await sleep.getByTestId("metric-row").nth(2).getByRole("button").click();
    const sheet = page.getByRole("dialog");
    await expect(sheet.getByRole("heading", { name: "The night" })).toBeVisible();
    await expect(sheet.getByRole("list", { name: "Sleep score contributors" }).getByRole("meter")).toHaveCount(7);
    await expect(sheet.getByRole("meter", { name: "HRV balance" })).toBeVisible();
    await page.keyboard.press("Escape");
    await expect(sheet).toBeHidden();

    // Last night in full, from the Today card.
    await page.getByRole("region", { name: "Today", exact: true }).getByRole("button", { name: "Last night in full" }).click();
    await expect(page.getByRole("dialog", { name: "Today" }).getByText(/^Last night, ending this morning: /)).toBeVisible();
  });

  test("Activity: today's Health XP, earned once the sync pays it and projected until then", async ({ page }) => {
    // Sunday Oct 25, 10 a.m.: three days into activity XP, last night's sleep in.
    await page.clock.install({ time: new Date("2026-10-25T10:00:00") });
    await page.goto("/activity");

    // The sync on opening pays last night's sleep; today's steps and run wait for 4 a.m.
    const xp = page.getByRole("region", { name: "Health XP today" });
    await expect(xp.getByTestId("activity-xp-sleep")).toContainText("earned", { timeout: 10_000 });
    await expect(xp.getByTestId("activity-xp-steps")).toContainText(/\d+ of 25 projected/);
    await expect(xp.getByTestId("activity-xp-steps")).toContainText("Pays when the day ends, at 4 a.m.");
    await expect(xp.getByTestId("activity-xp-total")).toHaveText(/^\d+ Health XP: \d+ earned, \d+ projected$/);

    // A day back on the Today card: yesterday's Health XP, all of it earned; then Today again.
    await page.getByRole("region", { name: "Today", exact: true }).getByRole("button", { name: "Previous day" }).click();
    const yesterday = page.getByRole("region", { name: "Yesterday", exact: true });
    await expect(yesterday.getByRole("region", { name: "Health XP yesterday" }).getByTestId("activity-xp-total")).toHaveText(
      /^\d+ Health XP earned$/,
    );
    await yesterday.getByRole("button", { name: "Today", exact: true }).click();
    await expect(xp.getByTestId("activity-xp-total")).toHaveText(/^\d+ Health XP: \d+ earned, \d+ projected$/);

    // Each day's steps XP in the Table: projected today, earned the days before.
    const steps = page.getByRole("region", { name: "Steps" });
    await steps.getByRole("button", { name: "Table" }).click();
    await expect(steps.getByTestId("metric-row").first()).toContainText(/\d+ projected$/);
    await expect(steps.getByTestId("metric-row").nth(1)).not.toContainText("projected");
    await expect(steps.getByText("Steps XP")).toBeVisible();
  });

  test("Food Log: log an entry with a photo, see it full size, and remove it", async ({ page }) => {
    await beforeActivityXp(page);
    await page.goto("/food");
    await expect(page.getByRole("heading", { name: "Today", level: 1 })).toBeVisible({ timeout: 10_000 });
    const png = await bigPng(page);

    await page.getByRole("button", { name: "Log food", exact: true }).click();
    const sheet = page.getByRole("dialog", { name: "Log food" });
    await sheet.getByLabel("Name").fill("Grain bowl");
    await sheet.getByRole("radio", { name: "4 · good" }).check();
    await sheet.getByLabel("Calories").fill("650");
    const chooser = page.waitForEvent("filechooser");
    await sheet.getByRole("button", { name: "Choose photo" }).click();
    await (await chooser).setFiles({ name: "IMG_4021.png", mimeType: "image/png", buffer: png });
    await expect(sheet.getByRole("button", { name: "Show the photo of Grain bowl full size" })).toBeVisible();
    await expect(sheet.getByText("It's saved with the entry when you log it.")).toBeVisible();
    await sheet.getByRole("button", { name: "Log", exact: true }).click();
    await expect(sheet).toBeHidden();
    await expect(page.getByText("Photo added to Grain bowl")).toBeVisible({ timeout: 10_000 });

    // The entry's thumbnail opens the photo full size, shrunk to 1,200 px.
    const entries = page.getByRole("list", { name: "Entries for today" });
    await entries.getByRole("button", { name: "Show the photo of Grain bowl full size" }).click();
    const viewer = page.getByRole("dialog", { name: "Grain bowl" });
    const photo = viewer.getByRole("img", { name: "Photo of Grain bowl" });
    await expect(photo).toBeVisible();
    expect(await photo.evaluate((img: HTMLImageElement) => [img.naturalWidth, img.naturalHeight])).toEqual([1200, 800]);
    await page.keyboard.press("Escape");
    await expect(viewer).toBeHidden();

    // Remove photo saves at once; the entry stays. The card offers Change or delete and Have another.
    await entries.getByRole("button", { name: /^Grain bowl, .+: change, delete or have another$/ }).click();
    const picker = page.getByRole("dialog", { name: "Grain bowl" });
    await expect(picker.getByRole("button", { name: "Have another" })).toBeVisible();
    await picker.getByRole("button", { name: "Change or delete" }).click();
    const edit = page.getByRole("dialog", { name: "Change Grain bowl" });
    await edit.getByRole("button", { name: "Remove photo" }).click();
    await expect(page.getByText("Photo removed from Grain bowl")).toBeVisible({ timeout: 10_000 });
    await expect(edit.getByRole("button", { name: "Choose photo" })).toBeVisible();
    await page.keyboard.press("Escape");
    await expect(entries.getByRole("button", { name: /^Grain bowl/ })).toBeVisible();
    await expect(entries.getByRole("button", { name: /^Show the photo/ })).toHaveCount(0);
  });

  test("Food Log: Log again and Have another carry the photo, and each entry's photo is its own", async ({ page }) => {
    await beforeActivityXp(page);
    await page.goto("/food");
    await expect(page.getByRole("heading", { name: "Today", level: 1 })).toBeVisible({ timeout: 10_000 });
    const png = await bigPng(page);

    await page.getByRole("button", { name: "Log food", exact: true }).click();
    const sheet = page.getByRole("dialog", { name: "Log food" });
    await sheet.getByLabel("Name").fill("Sausage burrito");
    await sheet.getByRole("radio", { name: "2 · indulgent" }).check();
    await sheet.getByLabel("Calories").fill("650");
    const chooser = page.waitForEvent("filechooser");
    await sheet.getByRole("button", { name: "Choose photo" }).click();
    await (await chooser).setFiles({ name: "IMG_5120.png", mimeType: "image/png", buffer: png });
    await expect(sheet.getByRole("button", { name: "Show the photo of Sausage burrito full size" })).toBeVisible();
    await sheet.getByRole("button", { name: "Log", exact: true }).click();
    await expect(sheet).toBeHidden();
    await expect(page.getByText("Photo added to Sausage burrito")).toBeVisible({ timeout: 10_000 });
    // A toast under the pointer waits, and this one sits over Log food on a phone.
    await page.mouse.move(0, 0);
    await expect(page.getByText("Photo added to Sausage burrito")).toBeHidden({ timeout: 10_000 });

    // Log again fills in the photo too: the one already stored.
    await page.getByRole("button", { name: "Log food", exact: true }).click();
    await sheet.getByRole("button", { name: "Log again: Sausage burrito · 2 · 650 cal" }).click();
    await expect(sheet.getByRole("button", { name: "Show the photo of Sausage burrito full size" })).toBeVisible();
    await expect(sheet.getByText(/^The last one's photo comes with it\./)).toBeVisible();
    await sheet.getByRole("button", { name: "Log", exact: true }).click();
    await expect(sheet).toBeHidden();

    // The two show as one card. The copy has the photo; removing it leaves the first's.
    const entries = page.getByRole("list", { name: "Entries for today" });
    const card = entries.getByRole("button", { name: /^Sausage burrito ×2, / });
    await expect(card).toBeVisible({ timeout: 10_000 });
    await card.click();
    const copies = page.getByRole("dialog", { name: "Sausage burrito ×2" });
    const each = copies.getByRole("list", { name: "Each Sausage burrito" }).getByRole("button");
    await expect(each.last()).toContainText("Change");
    await each.last().click();
    const change = page.getByRole("dialog", { name: "Change Sausage burrito" });
    await expect(change.getByRole("button", { name: "Show the photo of Sausage burrito full size" })).toBeVisible();
    await change.getByRole("button", { name: "Remove photo" }).click();
    await expect(page.getByText("Photo removed from Sausage burrito")).toBeVisible({ timeout: 10_000 });
    await page.keyboard.press("Escape");
    await expect(change).toBeHidden();
    await expect(entries.getByRole("button", { name: "Show the photo of Sausage burrito full size" })).toBeVisible();

    // Have another starts from the last copy, with the card's photo since it has none.
    await card.click();
    await copies.getByRole("button", { name: "Have another" }).click();
    await expect(sheet.getByLabel("Name")).toHaveValue("Sausage burrito");
    await expect(sheet.getByRole("button", { name: "Show the photo of Sausage burrito full size" })).toBeVisible();
    await sheet.getByRole("button", { name: "Log", exact: true }).click();
    await expect(sheet).toBeHidden();
    await expect(entries.getByRole("button", { name: /^Sausage burrito ×3, / })).toBeVisible({ timeout: 10_000 });
  });

  test("Covers: add one to a goal from search, see its credit, reposition it with the keyboard, and remove it", async ({
    page,
  }) => {
    await page.goto("/map?selected=goal-half");
    await expect(page.getByRole("heading", { name: "Run a half-marathon" })).toBeVisible({ timeout: 10_000 });
    await page.getByRole("button", { name: "Add cover" }).click();

    const picker = page.getByRole("dialog", { name: "Choose a cover" });
    const search = picker.getByRole("searchbox", { name: "Search Unsplash" });
    await expect(search).toHaveValue("Run a half-marathon");
    await expect(picker.getByRole("button", { name: /^Use the photo by / })).toHaveCount(1);
    await search.fill("anything at all");
    await search.press("Enter");
    await expect(picker.getByRole("button", { name: /^Use the photo by / })).toHaveCount(6);
    await picker.getByRole("button", { name: "More photos" }).click();
    await expect(picker.getByRole("button", { name: /^Use the photo by / })).toHaveCount(8);
    await picker.getByRole("button", { name: /^Use the photo by Teo Marin/ }).click();
    await expect(picker).toBeHidden();

    const banner = page.getByTestId("cover-banner");
    await expect(banner).toContainText("Photo by Teo Marin on Unsplash");
    await expect(banner.getByRole("link", { name: "Unsplash" })).toHaveAttribute(
      "href",
      /utm_source=lifeos&utm_medium=referral/,
    );
    await expect(page.getByRole("button", { name: "Add cover" })).toBeHidden();

    await banner.getByRole("button", { name: "Cover options" }).click();
    await page.getByRole("menuitem", { name: "Reposition" }).click();
    const slider = page.getByRole("slider", { name: /Cover position/ });
    await expect(slider).toBeFocused();
    await page.keyboard.press("ArrowDown");
    await page.keyboard.press("ArrowDown");
    await expect(slider).toHaveAttribute("aria-valuenow", "40");
    await page.getByRole("button", { name: "Save position" }).click();
    await expect(slider).toBeHidden();
    await expect(banner.locator("img")).toHaveCSS("object-position", "50% 40%");

    await banner.getByRole("button", { name: "Cover options" }).click();
    await page.getByRole("menuitem", { name: "Remove cover" }).click();
    await expect(banner).toBeHidden();
    await expect(page.getByRole("button", { name: "Add cover" })).toBeVisible();
  });

  // The mock keeps its data in memory, so this moves between pages with the sidebar.
  test("Covers on cards: the Map graph, Today and Vision show a cover set or removed in the panel", async ({
    page,
  }) => {
    await page.goto("/map?view=graph");
    const half = page.getByTestId("rf__node-goal-half");
    const lifeos = page.getByTestId("rf__node-goal-lifeos");
    await expect(lifeos.locator(".lifeos-node")).toHaveClass(/has-cover/, { timeout: 20_000 });
    await expect(lifeos.locator(".lifeos-node__shell")).toHaveAttribute("title", "Photo by Mina Cho on Unsplash");
    await expect(half.locator(".lifeos-node")).not.toHaveClass(/has-cover/);

    // A cover picked in the panel shows on its node at once, with no new layout.
    await half.click();
    await expect(page.getByRole("heading", { name: "Run a half-marathon" })).toBeVisible({ timeout: 10_000 });
    await page.getByRole("button", { name: "Add cover" }).click();
    const picker = page.getByRole("dialog", { name: "Choose a cover" });
    await picker.getByRole("searchbox", { name: "Search Unsplash" }).fill("meadow");
    await picker.getByRole("button", { name: "Search", exact: true }).click();
    await picker.getByRole("button", { name: /^Use the photo by Lars Brandt/ }).click();
    await expect(picker).toBeHidden();
    await expect(half.locator(".lifeos-node")).toHaveClass(/has-cover/);
    await page.getByRole("button", { name: "Close panel" }).click();

    // And one removed leaves it.
    await lifeos.click();
    const banner = page.getByTestId("cover-banner");
    await expect(banner).toContainText("Photo by Mina Cho on Unsplash", { timeout: 10_000 });
    await banner.getByRole("button", { name: "Cover options" }).click();
    await page.getByRole("menuitem", { name: "Remove cover" }).click();
    await expect(banner).toBeHidden();
    await expect(lifeos.locator(".lifeos-node")).not.toHaveClass(/has-cover/);

    // Today: the focus (Ship LifeOS production UI) has lost its strip; the half-marathon's
    // row leads with its new cover, and the project keeps its own.
    await page.getByRole("link", { name: "Focus" }).click();
    const focus = page.locator("[data-slot=card]", { hasText: "Primary focus" });
    await expect(focus).toContainText("Ship LifeOS production UI");
    await expect(focus.getByTestId("cover-image")).toHaveCount(0);
    await expect(
      page.getByRole("button", { name: /Run a half-marathon/ }).getByTestId("cover-image"),
    ).toHaveAttribute("title", "Photo by Lars Brandt on Unsplash");
    await expect(
      page.getByRole("button", { name: /Production web UI/ }).getByTestId("cover-image"),
    ).toHaveAttribute("title", "Photo by Sam Okafor on Unsplash");

    // Vision: the statement's card keeps its photo and credit; the goals follow the panel.
    await page.getByRole("link", { name: "Vision" }).click();
    const steward = page.locator("article", { has: page.getByRole("heading", { name: "Steward of my health" }) });
    await expect(steward.getByTestId("cover-image")).toBeVisible();
    await expect(steward).toContainText("Photo by Ines Vale on Unsplash");
    await expect(steward.getByRole("link", { name: "Ines Vale" })).toHaveAttribute("target", "_blank");
    await expect(
      page.getByRole("button", { name: /Run a half-marathon/ }).getByTestId("cover-image"),
    ).toHaveAttribute("title", "Photo by Lars Brandt on Unsplash");
    await expect(
      page.getByRole("button", { name: /Ship LifeOS production UI/ }).getByTestId("cover-image"),
    ).toHaveCount(0);
    await steward.getByRole("button", { name: "Steward of my health" }).click();
    await expect(page.getByTestId("cover-banner")).toContainText("Photo by Ines Vale on Unsplash", {
      timeout: 10_000,
    });
  });

  test("Environment: a walk-through rates two spaces in one save and lifts the Home Score, then a quick check", async ({
    page,
  }) => {
    await page.goto("/");
    await page.getByRole("link", { name: "Environment" }).first().click();
    await expect(page.getByRole("heading", { name: "Your home", level: 1 })).toBeVisible();
    const score = page.getByTestId("home-score");
    await expect(score).toHaveText(/^\d+\.\d$/, { timeout: 10_000 });
    const before = Number(await score.textContent());
    await expect(page.getByTestId("space-card")).toHaveCount(9);
    await expect(page.getByTestId("fix-next").getByRole("listitem")).toHaveCount(3);

    // The garage was skipped last Sunday, so the walk-through starts there, open.
    await page.getByRole("button", { name: "Walk-through" }).click();
    const sheet = page.getByRole("dialog", { name: "Walk-through" });
    const spaces = sheet.getByTestId("walk-through-space");
    await expect(spaces).toHaveCount(9);
    await expect(spaces.first().getByRole("button", { name: /^Garage/ })).toHaveAttribute("aria-expanded", "true");
    await sheet.getByRole("radio", { name: "5 · put away" }).check();
    await expect(sheet.getByText("Everything put away, surfaces clear.")).toBeVisible();
    await sheet.getByRole("radio", { name: "4.5 · clean to spotless" }).check();
    await expect(sheet.getByText("Between 4 (clean, light dust or smudges) and 5 (freshly cleaned, spotless).")).toBeVisible();
    await sheet.getByRole("radio", { name: "3 · plain" }).check();

    // Opening the kitchen closes the garage; its header says what it will save.
    await sheet.getByRole("button", { name: /^Kitchen/ }).click();
    await expect(sheet.getByRole("button", { name: /^Garage/ })).toContainText(
      "New: tidiness 5, cleanliness 4.5, aesthetics 3",
    );
    await sheet.getByRole("radio", { name: "5 · put away" }).check();
    await sheet.getByLabel("Note").fill("Counters clear");
    await expect(sheet.getByText("2 of 9 spaces rated")).toBeVisible();
    await sheet.getByRole("button", { name: "Save", exact: true }).click();
    await expect(sheet).toBeHidden();
    await expect(page.getByText("Rated 2 spaces")).toBeVisible();
    await expect.poll(async () => Number(await score.textContent()), { timeout: 10_000 }).toBeGreaterThan(before);
    const garage = page.getByRole("article", { name: "Garage" });
    await expect(garage).toContainText("rated today");

    // A card's Rate is a quick check of that space: Tidiness and Cleanliness, Aesthetics a tap away.
    await page.getByRole("button", { name: "Rate Office", exact: true }).click();
    const quick = page.getByRole("dialog", { name: "Rate Office" });
    await expect(quick.getByRole("group", { name: /^Tidiness/ })).toBeVisible();
    await expect(quick.getByRole("group", { name: /^Aesthetics/ })).toHaveCount(0);
    await expect(quick.getByRole("button", { name: "Rate Aesthetics too" })).toBeVisible();
    await quick.getByRole("group", { name: /^Cleanliness/ }).getByRole("radio", { name: "5 · spotless" }).check();
    await quick.getByRole("button", { name: "Save", exact: true }).click();
    await expect(quick).toBeHidden();
    await expect(page.getByText("Rated Office")).toBeVisible();
    await expect(page.getByRole("article", { name: "Office" })).toContainText("rated today");
  });

  test("Environment: a photo in the walk-through becomes Now, and the compare dialog makes it the baseline", async ({
    page,
  }) => {
    await page.goto("/environment");
    await expect(page.getByTestId("home-score")).toHaveText(/^\d+\.\d$/, { timeout: 10_000 });
    const png = await bigPng(page);
    const office = page.getByRole("article", { name: "Office" });
    await expect(office.getByTestId("then-and-now")).toContainText("2 photos");

    // Office's panel shows its baseline to line the new photo up with.
    await page.getByRole("button", { name: "Walk-through" }).click();
    const sheet = page.getByRole("dialog", { name: "Walk-through" });
    await sheet.getByRole("button", { name: /^Office/ }).click();
    await expect(sheet.getByRole("img", { name: /^Office's baseline, / })).toBeVisible();
    await expect(sheet.getByText(/^Line the new photo up with Then, /)).toBeVisible();
    const chooser = page.waitForEvent("filechooser");
    await sheet.getByRole("button", { name: "Choose photo of Office" }).click();
    await (await chooser).setFiles({ name: "IMG_6001.png", mimeType: "image/png", buffer: png });
    await expect(sheet.getByRole("img", { name: "New photo of Office" })).toBeVisible();
    await expect(sheet.getByText("New photo, dated with the walk-through.")).toBeVisible();
    await expect(sheet.getByRole("button", { name: /^Office/ })).toContainText("1 photo");
    await expect(sheet.getByText("Nothing rated yet · 1 photo to add")).toBeVisible();
    await sheet.getByRole("button", { name: "Save", exact: true }).click();
    await expect(sheet).toBeHidden();
    await expect(page.getByText("Added a photo of Office")).toBeVisible({ timeout: 10_000 });
    await expect(office.getByTestId("then-and-now")).toContainText("photo today · 3 photos");

    // Now is today's photo, shrunk to 1,200 px; the compare dialog opens on it, Then beside it.
    await office.getByTestId("photo-now").click();
    const compare = page.getByRole("dialog", { name: "Office" });
    await expect(compare.getByRole("button", { name: /^Right side: Now · / })).toHaveAttribute("aria-pressed", "true");
    await expect(compare.getByRole("button", { name: /^Left side: Then · / })).toBeVisible();
    await expect(compare.getByTestId("filmstrip").getByRole("button")).toHaveCount(3);
    const now = compare.getByTestId("compare-right").locator("img");
    await expect.poll(() => now.evaluate((img: HTMLImageElement) => img.naturalWidth)).toBe(1200);
    await compare.getByRole("button", { name: "Make this the baseline" }).click();
    await expect(page.getByText("Made the baseline")).toBeVisible({ timeout: 10_000 });
    await expect(compare.getByRole("button", { name: "This is the baseline" })).toBeDisabled();
    await page.keyboard.press("Escape");
    await expect(compare).toBeHidden();
    // Then and Now are the same photo now, so the card asks for another to compare.
    await expect(office.getByRole("button", { name: "Add a photo of Office to compare" })).toBeVisible();
  });

  test("Environment: photos from two spots added at once, the second starting its own shot", async ({ page }) => {
    await page.goto("/environment");
    await expect(page.getByTestId("home-score")).toHaveText(/^\d+\.\d$/, { timeout: 10_000 });
    const png = await bigPng(page);
    const garage = page.getByRole("article", { name: "Garage" });
    await garage.getByRole("button", { name: "Add photos of Garage", exact: true }).click();
    const sheet = page.getByRole("dialog", { name: "Add photos of Garage" });
    const chooser = page.waitForEvent("filechooser");
    await sheet.getByRole("button", { name: "Choose photos" }).click();
    await (await chooser).setFiles([
      { name: "IMG_8001.png", mimeType: "image/png", buffer: png },
      { name: "IMG_8002.png", mimeType: "image/png", buffer: png },
    ]);
    const rows = sheet.getByTestId("pending-photo");
    await expect(rows).toHaveCount(2);
    await rows.nth(1).getByLabel("Shot").selectOption({ label: "New shot…" });
    await rows.nth(1).getByLabel("New shot's name").fill("Workbench");
    await sheet.getByRole("button", { name: "Add 2 photos" }).click();
    await expect(sheet).toBeHidden({ timeout: 10_000 });
    await expect(page.getByText("Added 2 photos of Garage and the shot Workbench")).toBeVisible();

    // Each spot is its own shot with its own Then, picked on the card.
    const shots = garage.getByRole("group", { name: "Shot" });
    await expect(shots.getByRole("button")).toHaveText(["Garage", "Workbench"]);
    await expect(garage.getByRole("button", { name: "Add a photo of Garage to compare" })).toBeVisible();
    await shots.getByRole("button", { name: "Workbench" }).click();
    await expect(garage.getByTestId("photo-then")).toHaveAccessibleName(/^Compare Garage · Workbench: Then · /);
  });

  test("Environment: add a space, change its weight, and archive it out of the score", async ({ page }) => {
    await page.goto("/environment");
    const editor = page.getByRole("region", { name: "Manage spaces" });
    await expect(editor.getByTestId("space-row")).toHaveCount(9, { timeout: 10_000 });
    const add = editor.getByRole("heading", { name: "Add a space" }).locator("..");
    await add.getByLabel("Name").fill("Office");
    await add.getByRole("button", { name: "Add space" }).click();
    await expect(add.getByRole("alert")).toHaveText("You already have a space called 'Office'.");
    await add.getByLabel("Name").fill("Attic");
    // With spaces already there, what weight and pace mean is in ⓘ tips, not words.
    await expect(add.getByRole("button", { name: "About weight" })).toBeVisible();
    await add.getByLabel("Weight", { exact: true }).fill("2");
    await add.getByRole("group", { name: "Pace" }).getByRole("button", { name: "1", exact: true }).click();
    await add.getByRole("button", { name: "Add space" }).click();
    await expect(editor.getByTestId("space-row")).toHaveCount(10);
    // One line per space: weight, pace and share in their own columns.
    const attic = editor.getByTestId("space-row").filter({ hasText: "Attic" });
    await expect(attic.getByTestId("space-weight")).toHaveText("2");
    await expect(attic.getByTestId("space-pace")).toHaveText("1");
    await expect(attic.getByTestId("space-share")).toHaveText(/%$/);

    await editor.getByRole("button", { name: "Change Attic" }).click();
    // The row's form comes before Add a space's.
    await editor.getByLabel("Weight", { exact: true }).first().fill("4");
    await editor.getByRole("button", { name: "Save", exact: true }).click();
    await expect(attic.getByTestId("space-weight")).toHaveText("4");
    await expect(attic.getByTestId("space-pace")).toHaveText("1");

    // Not rated yet: its card says so, and it doesn't count.
    await expect(page.getByRole("article", { name: "Attic" })).toContainText("not rated yet");
    await editor.getByRole("button", { name: "Archive Attic" }).click();
    await expect(page.getByRole("article", { name: "Attic" })).toHaveCount(0);
    await expect(editor.getByRole("list", { name: "Archived spaces" })).toContainText("Attic");
    await editor.getByRole("button", { name: "Restore Attic" }).click();
    await expect(page.getByRole("article", { name: "Attic" })).toBeVisible();
  });

  test("Browse: pick Decisions, filter by status, find one from the search box, and make a new one", async ({
    page,
  }) => {
    await page.goto("/browse");
    const filters = page.getByRole("complementary", { name: "Browse filters" });
    await filters.getByRole("button", { name: /^Decision\s*2$/ }).click();
    const list = page.getByRole("list", { name: "Decision list" });
    await expect(list.getByRole("button")).toHaveCount(2);

    await filters.getByRole("checkbox", { name: "Implementing" }).click();
    await expect(list.getByRole("button")).toHaveCount(1);
    await list.getByRole("button", { name: /React \+ Vite for Production UI/ }).click();
    await expect(page.getByRole("heading", { name: "React + Vite for Production UI" })).toBeVisible();

    // Coming back keeps the type, the open item and the filters.
    await page.goto("/");
    await page.getByRole("navigation", { name: "Primary" }).getByRole("link", { name: "Browse" }).click();
    await expect(filters.getByRole("checkbox", { name: "Implementing" })).toBeChecked();
    await expect(page.getByRole("heading", { name: "React + Vite for Production UI" })).toBeVisible();

    // The search box finds the other Decision, though it's past the first twenty items.
    await page.keyboard.press("Control+k");
    await page.getByRole("combobox").fill("blazor");
    await page.getByRole("option", { name: /Blazor for Production UI/ }).click();
    await expect(page).toHaveURL(/\/browse\?.*selected=dec-blazor/);
    await expect(page.getByRole("heading", { name: "Blazor for Production UI" })).toBeVisible();
    await expect(page.getByText("“Blazor for Production UI” is open, but the filters hide it in the list.")).toBeVisible();
    await page.getByRole("button", { name: "Show it" }).click();
    await expect(list.getByRole("button", { name: /Blazor for Production UI/ })).toHaveAttribute("aria-current", "true");

    // A new Decision made from here is selected in the list.
    await page.getByRole("button", { name: "New Decision" }).click();
    const dialog = page.getByRole("dialog", { name: "New Decision" });
    await dialog.getByRole("textbox", { name: "Title" }).fill("Use SQLite for local storage");
    await dialog.getByRole("button", { name: "Save" }).click();
    await expect(dialog).toBeHidden({ timeout: 10_000 });
    await expect(list.getByRole("button", { name: /Use SQLite for local storage/ })).toHaveAttribute(
      "aria-current",
      "true",
    );
    await expect(page.getByRole("heading", { name: "Use SQLite for local storage" })).toBeVisible();
  });

  test("Decisions: record one from a Problem, create its resulting work, add context, and replace it", async ({
    page,
  }) => {
    // Recorded from a Problem, a Decision shows the Problem as its Context before Save.
    await page.goto("/browse?type=Problem&selected=prob-sleep");
    await expect(page.getByRole("heading", { name: "How do I sleep through the night?" })).toBeVisible({
      timeout: 10_000,
    });
    await page.getByRole("button", { name: "Record decision" }).click();
    const newDecision = page.getByRole("dialog", { name: "New Decision" });
    await expect(newDecision.getByRole("group", { name: "Context" })).toContainText("How do I sleep through the night?");
    await newDecision.getByRole("textbox", { name: "Title" }).fill("No caffeine after noon");
    await newDecision.getByRole("button", { name: "Save" }).click();
    await expect(newDecision).toBeHidden({ timeout: 10_000 });

    const filters = page.getByRole("complementary", { name: "Browse filters" });
    await filters.getByRole("button", { name: /^Decision\s*3$/ }).click();
    const list = page.getByRole("list", { name: "Decision list" });
    await list.getByRole("button", { name: /No caffeine after noon/ }).click();
    await expect(page.getByRole("heading", { name: "No caffeine after noon" })).toBeVisible();
    await page.getByRole("tab", { name: "Relationships" }).click();
    const context = page.getByRole("region", { name: "Context" });
    await expect(context).toContainText("How do I sleep through the night?");

    // Resulting work: pick the type, then the normal Task form with the link shown before Save.
    await page.getByRole("button", { name: "Resulting work" }).click();
    await page.getByRole("menuitem", { name: "Task" }).click();
    const newTask = page.getByRole("dialog", { name: "New Task" });
    await expect(newTask.getByRole("group", { name: "Resulting work of" })).toContainText("No caffeine after noon");
    await newTask.getByRole("textbox", { name: "Title" }).fill("Switch to decaf after lunch");
    await newTask.getByRole("button", { name: "Save" }).click();
    await expect(newTask).toBeHidden({ timeout: 10_000 });
    const work = page.getByRole("region", { name: "Resulting work" });
    await expect(work.getByRole("button", { name: "Switch to decaf after lunch" })).toBeVisible();
    // Making its work doesn't move the Decision: it's still Open.
    await expect(list.getByRole("button", { name: /No caffeine after noon/ })).toContainText("Open");

    // An existing Idea added to Context later.
    await page.getByRole("combobox", { name: "Target type" }).click();
    await page.getByRole("option", { name: "Idea", exact: true }).click();
    await page.getByRole("combobox", { name: "Target subject" }).click();
    await page.getByRole("option", { name: "Wind-down without screens" }).click();
    await page.getByRole("button", { name: "Link", exact: true }).click();
    await expect(context).toContainText("Wind-down without screens");

    // Reversed: a replacement Decision supersedes it, and it becomes Cancelled with its title kept.
    await page.getByRole("button", { name: "Replacement" }).click();
    await expect(newDecision.getByRole("group", { name: "Replaces" })).toContainText("No caffeine after noon");
    await expect(newDecision.getByRole("group", { name: "Replaces" })).toContainText("Saving marks it Cancelled");
    await newDecision.getByRole("textbox", { name: "Title" }).fill("No caffeine after 10am");
    await newDecision.getByRole("button", { name: "Save" }).click();
    await expect(newDecision).toBeHidden({ timeout: 10_000 });
    await expect(page.getByRole("heading", { name: "No caffeine after noon" })).toBeVisible();
    await expect(list.getByRole("button", { name: /No caffeine after noon/ })).toContainText("Cancelled");
    const replacedBy = page.getByRole("region", { name: "Replaced by" });
    await replacedBy.getByRole("button", { name: "No caffeine after 10am" }).click();
    await expect(page.getByRole("heading", { name: "No caffeine after 10am" })).toBeVisible();
    await page.getByRole("tab", { name: "Relationships" }).click();
    await expect(page.getByRole("region", { name: "Replaces" })).toContainText("No caffeine after noon");
  });

  test("theme toggle switches the document class", async ({ page }) => {
    await page.goto("/");
    const html = page.locator("html");
    const before = await html.getAttribute("class");
    await page.getByRole("button", { name: /Switch to (light|dark) theme/ }).click();
    await expect(html).not.toHaveAttribute("class", before ?? "");
  });

  test("mobile nav opens and closes", async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 812 });
    await page.goto("/");
    await page.getByRole("button", { name: "Open navigation" }).click();
    await expect(page.getByRole("dialog", { name: "Navigation" })).toBeVisible();
    await page.getByRole("link", { name: "Inbox" }).click();
    await expect(page).toHaveURL(/inbox/);
    await expect(page.getByRole("dialog", { name: "Navigation" })).toBeHidden();
  });

  // An agent's sign-in request (agent-access plan §4.4), with the mock's pretend request from
  // Claude. Claude's callback is answered here, so the paths never leave the test.
  test("approve an agent's consent request", async ({ page }) => {
    await page.route("https://claude.ai/**", (route) => route.fulfill({ body: "back at the agent" }));
    await page.goto("/oauth/consent?authorization_id=mock-1");
    await expect(page.getByRole("heading", { name: "Connect Claude to LifeOS?" })).toBeVisible();
    await expect(page.getByRole("navigation")).toHaveCount(0);
    // The mock's Claude is connecting again, so the page offers to replace its earlier client.
    await expect(page.getByRole("checkbox", { name: "Replace the earlier connection" })).toBeChecked();
    await page.getByRole("button", { name: "Approve" }).click();
    await expect(page).toHaveURL(/^https:\/\/claude\.ai\/api\/mcp\/auth_callback\?code=mock-code/);
  });

  test("deny an agent's consent request", async ({ page }) => {
    await page.route("https://claude.ai/**", (route) => route.fulfill({ body: "back at the agent" }));
    await page.goto("/oauth/consent?authorization_id=mock-1");
    await page.getByRole("button", { name: "Deny" }).click();
    await expect(page).toHaveURL(/auth_callback\?error=access_denied/);
  });

  // Connected agents (agent-access plan §4.5), with the mock's Claude (allowed) and a helper
  // Supabase lists but LifeOS doesn't allow (fix 3).
  test("allow an agent, read its calls, and revoke it", async ({ page }) => {
    await page.goto("/people");
    const helper = page.getByRole("article", { name: "Research helper" });
    await expect(helper.getByText("Not allowed")).toBeVisible();
    await helper.getByRole("button", { name: "Allow" }).click();
    await page.getByRole("alertdialog").getByRole("button", { name: "Allow" }).click();
    await expect(helper.getByText("Connected")).toBeVisible();

    const claude = page.getByRole("article", { name: "Claude" });
    await claude.getByRole("button", { name: "Recent calls" }).click();
    await expect(claude.getByRole("list", { name: "Recent calls, newest first" }).getByText("life_snapshot")).toBeVisible();
    await claude.getByRole("button", { name: "Revoke" }).click();
    await page.getByRole("alertdialog").getByRole("button", { name: "Revoke" }).click();
    await expect(claude.getByText("Revoked", { exact: true })).toBeVisible();
  });

  test("an expired consent request says to start again", async ({ page }) => {
    await page.goto("/oauth/consent?authorization_id=mock-expired");
    await expect(page.getByRole("heading", { name: "This request can't be found" })).toBeVisible();
    await page.goto("/oauth/consent");
    await expect(page.getByRole("heading", { name: "This link has no request in it" })).toBeVisible();
  });
});
