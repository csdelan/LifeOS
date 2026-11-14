import AxeBuilder from "@axe-core/playwright";
import { expect, test } from "@playwright/test";
import { settled } from "../support";

test.describe("accessibility: the Character page", () => {
  // The Character page with Health's daily chart open, as a chart and as a table, in both
  // themes (next-themes keeps the choice under "theme"). Opening the page asks for the Oura
  // sync, whose activity XP toasts: reduced motion (no transitions) scans the toasts settled.
  for (const theme of ["dark", "light"] as const) {
    test(`axe serious/critical: /character with the daily chart, ${theme} theme`, async ({ page }) => {
      const scan = async () => {
        await settled(page);
        const results = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21aa"]).analyze();
        return results.violations.filter((v) => v.impact === "critical" || v.impact === "serious");
      };
      await page.emulateMedia({ reducedMotion: "reduce" });
      await page.addInitScript((t) => localStorage.setItem("theme", t), theme);
      await page.goto("/character");
      await expect(page.locator("html")).toHaveClass(new RegExp(theme));
      // Health's chart and Environment's (the Home Score against the bar). An opened chart's
      // button reads "Hide", so the first "Show" left is the next card's.
      const show = page.getByRole("button", { name: "Show the daily chart" });
      await expect(show).toHaveCount(2, { timeout: 10_000 });
      await show.first().click();
      await show.first().click();
      await expect(page.getByTestId("metric-column")).toHaveCount(60, { timeout: 10_000 });
      const chart = await scan();
      expect(chart, JSON.stringify(chart, null, 2)).toEqual([]);

      const tables = page.getByRole("button", { name: "Table", exact: true });
      await tables.nth(0).click();
      await tables.nth(1).click();
      await expect(page.getByRole("table", { name: /newest first/ })).toHaveCount(2);
      const table = await scan();
      expect(table, JSON.stringify(table, null, 2)).toEqual([]);
    });
  }

  // The Character page's Reports over 3 months: the rank chart (with the seeded exception day
  // hatched), then its table, in both themes, with any activity XP toast settled.
  for (const theme of ["dark", "light"] as const) {
    test(`axe serious/critical: /character?view=reports, ${theme} theme`, async ({ page }) => {
      const scan = async () => {
        await settled(page);
        const results = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21aa"]).analyze();
        return results.violations.filter((v) => v.impact === "critical" || v.impact === "serious");
      };
      await page.emulateMedia({ reducedMotion: "reduce" });
      await page.addInitScript((t) => {
        localStorage.setItem("theme", t);
        localStorage.setItem("lifeos.view.character.reports", JSON.stringify({ period: "90" }));
      }, theme);
      await page.goto("/character?view=reports");
      await expect(page.locator("html")).toHaveClass(new RegExp(theme));
      await expect(page.getByTestId("rank-chart")).toHaveAccessibleName(/^Rank over the last 3 months\./, {
        timeout: 10_000,
      });
      await expect(page.getByTestId("exception-band")).toHaveCount(1);
      const chart = await scan();
      expect(chart, JSON.stringify(chart, null, 2)).toEqual([]);

      await page.getByRole("button", { name: "Table", exact: true }).click();
      await expect(page.getByRole("region", { name: "Rank over the last 3 months, as a table" })).toBeVisible();
      const table = await scan();
      expect(table, JSON.stringify(table, null, 2)).toEqual([]);
    });
  }

  // The Mark a day as an exception dialog, with an earlier day's date field showing, in both themes.
  for (const theme of ["dark", "light"] as const) {
    test(`axe serious/critical: /character exception dialog, ${theme} theme`, async ({ page }) => {
      await page.emulateMedia({ reducedMotion: "reduce" });
      await page.addInitScript((t) => localStorage.setItem("theme", t), theme);
      await page.goto("/character");
      await expect(page.locator("html")).toHaveClass(new RegExp(theme));
      await page.getByRole("button", { name: "Mark a day as an exception" }).click({ timeout: 10_000 });
      const dialog = page.getByRole("dialog", { name: "Mark a day as an exception" });
      await dialog.getByRole("radio", { name: "An earlier day" }).check();
      await expect(dialog.getByLabel("Date")).toBeVisible();
      await dialog.getByRole("textbox", { name: "Why" }).fill("Sick with the flu");
      await settled(page);
      const results = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21aa"]).analyze();
      const blocking = results.violations.filter((v) => v.impact === "critical" || v.impact === "serious");
      expect(blocking, JSON.stringify(blocking, null, 2)).toEqual([]);
    });
  }
});
