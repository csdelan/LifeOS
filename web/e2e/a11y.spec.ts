import AxeBuilder from "@axe-core/playwright";
import { expect, test } from "@playwright/test";

const routes = [
  "/",
  "/inbox",
  "/map",
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

test.describe("accessibility", () => {
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

  // States the routes above don't reach: an Established habit checked off on the Habits
  // page, and Today's Maintaining group open (REWARD-2 maintenance).
  test("axe serious/critical: maintained habits", async ({ page }) => {
    const scan = async () => {
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
});
