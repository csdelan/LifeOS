import { expect, test } from "@playwright/test";
import { expectAccessible } from "../support";

test.describe("accessibility: Activity", () => {
  // The Activity page (ACTIVITY-1..4): the steps, sleep and exercise charts, then the Tables and
  // the year of weeks with the workouts not counted, then a workout's sheet and a night's, in
  // both themes.
  for (const theme of ["dark", "light"] as const) {
    test(`axe serious/critical: /activity with charts, tables and a workout, ${theme} theme`, async ({ page }) => {
      await page.emulateMedia({ reducedMotion: "reduce" });
      await page.addInitScript((t) => localStorage.setItem("theme", t), theme);
      await page.goto("/activity");
      await expect(page.locator("html")).toHaveClass(new RegExp(theme));
      const steps = page.getByRole("region", { name: "Steps" });
      const sleep = page.getByRole("region", { name: "Sleep and readiness", exact: true });
      const exercise = page.getByRole("region", { name: "Exercise" });
      await expect(steps.getByRole("group", { name: /^Steps by day/ })).toBeVisible();
      await expect(sleep.getByRole("list", { name: "Stages" })).toBeVisible();
      await expect(sleep.getByRole("group", { name: /^Sleep score by day/ })).toBeVisible();
      await expect(exercise.getByRole("group", { name: /^Minutes of counted exercise/ })).toBeVisible();
      await expect(page.getByText(/^Synced from Oura /)).toBeVisible();
      // A bar read out, so the readout's tooltip is scanned too.
      await steps.getByRole("group", { name: /^Steps by day/ }).focus();
      await expectAccessible(page);

      await steps.getByRole("button", { name: "Table" }).click();
      await expect(steps.getByRole("table")).toBeVisible();
      await sleep.getByRole("button", { name: "Table" }).click();
      await expect(sleep.getByRole("table")).toBeVisible();
      await exercise.getByRole("button", { name: "Table" }).click();
      await exercise.getByRole("button", { name: /^Show \d+ not counted$/ }).click();
      await expect(exercise.getByText(/^Not counted: /).first()).toBeVisible();
      await expectAccessible(page);

      await steps.getByRole("button", { name: "1 year" }).click();
      await steps.getByRole("button", { name: "Chart" }).click();
      await expect(steps.getByRole("group", { name: /^Average steps a day by week/ })).toBeVisible();
      // The last range stays up, dimmed, while the year loads: scan it settled.
      await expect(steps.locator("[aria-busy=true]")).toHaveCount(0);
      await expectAccessible(page);

      await exercise.getByTestId("workout-row").first().click();
      const sheet = page.getByRole("dialog");
      await sheet.getByRole("textbox", { name: "Note" }).fill("Steady");
      await sheet.getByRole("textbox", { name: "Note" }).press("Enter");
      await expect(sheet.getByText("Saved")).toBeVisible();
      await expectAccessible(page);

      // A night in full, with a nap: the contributor meters and the stages.
      await page.keyboard.press("Escape");
      await expect(sheet).toBeHidden();
      await sleep.getByTestId("metric-row").nth(4).getByRole("button").click();
      await expect(page.getByRole("dialog").getByRole("heading", { name: "A nap" })).toBeVisible();
      await expect(page.getByRole("dialog").getByRole("meter", { name: "HRV balance" })).toBeVisible();
      await expectAccessible(page);
    });
  }

  // Tags from the Oura app (ACTIVITY-6): the Tags card with its counts and every day shown, and
  // a day's sheet with its tags, in both themes.
  for (const theme of ["dark", "light"] as const) {
    test(`axe serious/critical: /activity tags and a day's tags, ${theme} theme`, async ({ page }) => {
      await page.emulateMedia({ reducedMotion: "reduce" });
      await page.addInitScript((t) => localStorage.setItem("theme", t), theme);
      await page.goto("/activity");
      await expect(page.locator("html")).toHaveClass(new RegExp(theme));
      const tags = page.getByRole("region", { name: "Tags", exact: true });
      await expect(tags.getByTestId("tag-counts")).toBeVisible();
      await expect(page.getByText(/^Synced from Oura /)).toBeVisible();
      await tags.getByRole("button", { name: /^Show \d+ more days$/ }).click();
      await expect(tags.getByTestId("tag-comment").first()).toBeVisible();
      await expectAccessible(page);

      await tags.getByTestId("tag-day").nth(2).getByRole("button", { name: /, open the day$/ }).click();
      const sheet = page.getByRole("dialog");
      await expect(sheet.getByRole("list", { name: "Tags from the Oura app" })).toBeVisible();
      await expect(sheet.getByRole("heading", { name: "The night" })).toBeVisible();
      await expectAccessible(page);
    });
  }

  // The Activity page on a phone: one column, and a workout's sheet and last night's rising from
  // the bottom.
  test("axe serious/critical: /activity on a phone with a workout and last night open", async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 812 });
    await page.emulateMedia({ reducedMotion: "reduce" });
    await page.goto("/activity");
    await expect(page.getByRole("region", { name: "Steps" }).getByRole("group", { name: /^Steps by day/ })).toBeVisible();
    const width = await page.evaluate(() => document.documentElement.scrollWidth);
    expect(width).toBeLessThanOrEqual(375);
    await expectAccessible(page);

    await page.getByRole("region", { name: "Exercise" }).getByTestId("workout-row").first().click();
    const sheet = page.getByRole("dialog");
    await expect(sheet.getByRole("textbox", { name: "Note" })).toBeVisible();
    const box = await sheet.boundingBox();
    expect(box && box.y + box.height).toBeGreaterThan(800);
    await expectAccessible(page);

    await page.keyboard.press("Escape");
    await expect(sheet).toBeHidden();
    await page.getByRole("region", { name: "Today", exact: true }).getByRole("button", { name: "Last night in full" }).click();
    const night = page.getByRole("dialog", { name: "Today" });
    await expect(night.getByRole("list", { name: "Readiness contributors" })).toBeVisible();
    const nightBox = await night.boundingBox();
    expect(nightBox && nightBox.y + nightBox.height).toBeGreaterThan(800);
    await expectAccessible(page);

    // The Today card a day back: its arrows on their own row, the page still one phone wide.
    await page.keyboard.press("Escape");
    await expect(night).toBeHidden();
    await page.getByRole("region", { name: "Today", exact: true }).getByRole("button", { name: "Previous day" }).click();
    const yesterday = page.getByRole("region", { name: "Yesterday", exact: true });
    await expect(yesterday.getByRole("region", { name: "Health XP yesterday" })).toBeVisible();
    for (const name of ["Previous day", "Today", "Next day"]) {
      const box = await yesterday.getByRole("button", { name, exact: true }).boundingBox();
      expect(box && box.height).toBeGreaterThanOrEqual(44);
    }
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(375);
    await expectAccessible(page);
  });
});
