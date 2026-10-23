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

  test("edit and save a subject", async ({ page }) => {
    await page.goto("/map?selected=goal-lifeos");
    await expect(page.getByRole("heading", { name: /Ship LifeOS production UI/ })).toBeVisible();
    await page.getByRole("button", { name: "Edit" }).click();
    const description = page.getByLabel("Description");
    await description.fill("Polished across every interface.");
    await page.getByRole("button", { name: "Save" }).click();
    await expect(page.getByRole("button", { name: "Edit" })).toBeVisible({ timeout: 10_000 });
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

    // In-app navigation keeps the mock's state. The seeded Health history is 40 XP short of
    // level 10, where Apprentice's window is already met: today's 90 base XP (108 at ×1.2) reaches it.
    await page.getByRole("link", { name: "Habits" }).first().click();
    const mobility = page.getByRole("list", { name: "Building" }).getByRole("listitem").filter({ hasText: "Morning mobility" });
    await mobility.getByRole("button", { name: "Followed", exact: true }).click();
    await expect(page.getByText("You're now a Health Apprentice.")).toBeVisible({ timeout: 10_000 });

    await page.getByRole("link", { name: "Character" }).first().click();
    await expect(health.getByText("Level 10", { exact: true })).toBeVisible({ timeout: 10_000 });
    await expect(health.getByText("Apprentice", { exact: true })).toBeVisible();
    await expect(health.getByRole("img", { name: "Today: 90 of 60 base XP, a successful day" })).toBeVisible();
    const ranks = page.getByRole("region", { name: "Health ranks" }).getByRole("listitem");
    await expect(ranks.nth(1)).toHaveAttribute("aria-current", "step");
  });

  test("Character Reports compare Health over a longer period than its history", async ({ page }) => {
    await beforeActivityXp(page);
    await page.goto("/character");
    await page.getByRole("tab", { name: "Reports" }).click();
    await expect(page).toHaveURL(/view=reports/);
    const consistency = (label: string) => page.getByRole("list", { name: `Consistency, last ${label}` });
    await expect(consistency("30 days")).toContainText("80% · all 30 days", { timeout: 10_000 });

    await page.getByRole("button", { name: "3 months" }).click();
    await expect(consistency("3 months")).toContainText("77% · 44 of 90 days", { timeout: 10_000 });
    await page.getByRole("combobox", { name: "Compare with" }).selectOption("30");
    await expect(consistency("3 months")).toContainText("30 days: 80% · all 30 days", { timeout: 10_000 });
    await expect(page.getByRole("heading", { name: "By domain, last 3 months" })).toBeVisible();
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

    // Three more with Log again: four drinks take 20% off the day.
    for (let i = 0; i < 3; i++) {
      await page.getByRole("button", { name: "Log food", exact: true }).click();
      await sheet.getByRole("button", { name: "Log again: IPA · drink · 200 cal" }).click();
      await expect(sheet.getByRole("switch", { name: "Alcoholic drink" })).toBeChecked();
      await sheet.getByRole("button", { name: "Log", exact: true }).click();
      await expect(sheet).toBeHidden();
    }
    await expect(page.getByText("4 drinks · 20% off the day's diet XP")).toBeVisible({ timeout: 10_000 });

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
    await expect(page.getByText("5 drinks · 30% off the day's diet XP")).toBeVisible({ timeout: 10_000 });

    await entries.getByRole("button", { name: /^IPA ×5 / }).click();
    await page.getByRole("dialog", { name: "IPA ×5" }).getByRole("list", { name: "Each IPA" }).getByRole("button").first().click();
    const change = page.getByRole("dialog", { name: "Change IPA" });
    await change.getByRole("button", { name: "Delete" }).click();
    await page.getByRole("alertdialog").getByRole("button", { name: "Delete" }).click();
    await expect(change).toBeHidden();
    await expect(entries.getByRole("button", { name: /^IPA ×4 / })).toBeVisible({ timeout: 10_000 });
    await expect(page.getByText("4 drinks · 20% off the day's diet XP")).toBeVisible();

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
});
