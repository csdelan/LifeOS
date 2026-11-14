import { expect, test } from "@playwright/test";
import { expectAccessible } from "../support";

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
      // The Map's graph is scanned on its own, laid out and with its covers, in covers.spec.ts.
      await expectAccessible(page, { exclude: ".react-flow" });
    });
  }

  // Repeating Tasks (TASKS-3): the Tasks page's Repeating view with a repeating task open,
  // its history listed, then one of its repetitions with Mark missed, in both themes.
  for (const theme of ["dark", "light"] as const) {
    test(`axe serious/critical: /tasks repeating view and a repetition, ${theme} theme`, async ({ page }) => {
      await page.emulateMedia({ reducedMotion: "reduce" });
      await page.addInitScript((t) => localStorage.setItem("theme", t), theme);
      await page.goto("/tasks");
      await expect(page.locator("html")).toHaveClass(new RegExp(theme));
      await page.getByRole("combobox", { name: "Status" }).click();
      await page.getByRole("option", { name: "Repeating", exact: true }).click();
      await page.getByRole("button", { name: /Take out trash/ }).click();
      const repeats = page.getByRole("region", { name: "Repeats" });
      await expect(repeats.getByRole("list", { name: "Repetitions" })).toBeVisible();
      await expectAccessible(page);

      await repeats.getByRole("list", { name: "Repetitions" }).getByRole("button").first().click();
      await expect(repeats.getByRole("button", { name: "Mark missed" })).toBeVisible();
      await expectAccessible(page);
    });
  }

  // States the routes above don't reach: an Established habit checked off on the Habits
  // page, and Today's Maintaining group open (REWARD-2 maintenance).
  test("axe serious/critical: maintained habits", async ({ page }) => {
    await page.goto("/habits");
    await page.getByRole("button", { name: "Ready to establish Morning mobility" }).click();
    await page.getByRole("alertdialog").getByRole("button", { name: "Establish" }).click();
    const graduated = page.getByRole("region", { name: "Established" });
    await graduated.getByRole("button", { name: "Morning mobility: followed today" }).click();
    await expect(graduated.getByText("Done today")).toBeVisible();
    await expectAccessible(page);

    await page.getByRole("link", { name: "Focus" }).first().click();
    await page.getByRole("button", { name: /Maintaining/ }).click();
    await expect(page.getByRole("list", { name: "Maintaining" }).getByText("Done today")).toBeVisible();
    await expectAccessible(page);
  });

  test("axe serious/critical: a task's timer, and Complete asking first", async ({ page }) => {
    await page.emulateMedia({ reducedMotion: "reduce" });
    await page.goto("/browse?type=Task&selected=task-shoes");
    const timer = page.getByRole("region", { name: "Timer", exact: true });
    await timer.getByRole("button", { name: "Start" }).click();
    await expect(timer.getByText("Running", { exact: true })).toBeVisible();
    await timer.getByRole("button", { name: "Complete" }).click();
    await expect(page.getByRole("alertdialog")).toBeVisible();
    await expectAccessible(page);
  });
});
