import AxeBuilder from "@axe-core/playwright";
import { expect, test } from "@playwright/test";
import { settled } from "../support";

const routes = [
  "/",
  "/inbox",
  "/map",
  "/browse",
  "/tasks",
  "/habits",
  "/rewards",
  "/rewards?view=history",
  "/vision",
  "/reviews",
  "/people",
  "/areas",
  "/gallery",
] as const;

test.describe("accessibility: every page, habits and tasks", () => {
  for (const route of routes) {
    test(`axe serious/critical: ${route}`, async ({ page }) => {
      await page.goto(route);
      await page.waitForLoadState("networkidle");
      const results = await new AxeBuilder({ page })
        .withTags(["wcag2a", "wcag2aa", "wcag21aa"])
        .exclude(".react-flow")
        .analyze();
      const blocking = results.violations.filter(
        (v) => v.impact === "critical" || v.impact === "serious",
      );
      expect(blocking, JSON.stringify(blocking, null, 2)).toEqual([]);
    });
  }

  // Repeating Tasks (TASKS-3): the Tasks page's Repeating view with a repeating task open,
  // its history listed, then one of its repetitions with Mark missed, in both themes.
  for (const theme of ["dark", "light"] as const) {
    test(`axe serious/critical: /tasks repeating view and a repetition, ${theme} theme`, async ({ page }) => {
      const scan = async () => {
        await settled(page);
        const results = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21aa"]).analyze();
        return results.violations.filter((v) => v.impact === "critical" || v.impact === "serious");
      };
      await page.emulateMedia({ reducedMotion: "reduce" });
      await page.addInitScript((t) => localStorage.setItem("theme", t), theme);
      await page.goto("/tasks");
      await expect(page.locator("html")).toHaveClass(new RegExp(theme));
      await page.getByRole("combobox", { name: "Status" }).click();
      await page.getByRole("option", { name: "Repeating", exact: true }).click();
      await page.getByRole("button", { name: /Take out trash/ }).click();
      const repeats = page.getByRole("region", { name: "Repeats" });
      await expect(repeats.getByRole("list", { name: "Repetitions" })).toBeVisible({ timeout: 10_000 });
      const series = await scan();
      expect(series, JSON.stringify(series, null, 2)).toEqual([]);

      await repeats.getByRole("list", { name: "Repetitions" }).getByRole("button").first().click();
      await expect(repeats.getByRole("button", { name: "Mark missed" })).toBeVisible({ timeout: 10_000 });
      const repetition = await scan();
      expect(repetition, JSON.stringify(repetition, null, 2)).toEqual([]);
    });
  }

  // States the routes above don't reach: an Established habit checked off on the Habits
  // page, and Today's Maintaining group open (REWARD-2 maintenance).
  test("axe serious/critical: maintained habits", async ({ page }) => {
    const scan = async () => {
      await settled(page);
      const results = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21aa"]).analyze();
      return results.violations.filter((v) => v.impact === "critical" || v.impact === "serious");
    };
    await page.goto("/habits");
    await page.getByRole("button", { name: "Ready to establish Morning mobility" }).click();
    await page.getByRole("alertdialog").getByRole("button", { name: "Establish" }).click();
    const graduated = page.getByRole("region", { name: "Established" });
    await graduated.getByRole("button", { name: "Morning mobility: followed today" }).click();
    await expect(graduated.getByText("Done today")).toBeVisible({ timeout: 10_000 });
    const habits = await scan();
    expect(habits, JSON.stringify(habits, null, 2)).toEqual([]);

    await page.getByRole("link", { name: "Focus" }).first().click();
    await page.getByRole("button", { name: /Maintaining/ }).click();
    await expect(page.getByRole("list", { name: "Maintaining" }).getByText("Done today")).toBeVisible();
    const today = await scan();
    expect(today, JSON.stringify(today, null, 2)).toEqual([]);
  });

  test("axe serious/critical: a task's timer, and Complete asking first", async ({ page }) => {
    await page.emulateMedia({ reducedMotion: "reduce" });
    await page.goto("/browse?type=Task&selected=task-shoes");
    const timer = page.getByRole("region", { name: "Timer", exact: true });
    await timer.getByRole("button", { name: "Start" }).click();
    await expect(timer.getByText("Running", { exact: true })).toBeVisible();
    await timer.getByRole("button", { name: "Complete" }).click();
    await expect(page.getByRole("alertdialog")).toBeVisible();
    await settled(page);
    const results = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21aa"]).analyze();
    const serious = results.violations.filter((v) => v.impact === "critical" || v.impact === "serious");
    expect(serious, JSON.stringify(serious, null, 2)).toEqual([]);
  });
});
