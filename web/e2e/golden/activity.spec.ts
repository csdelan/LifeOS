import { expect, test } from "@playwright/test";

test.describe("golden paths: Activity", () => {
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
});
