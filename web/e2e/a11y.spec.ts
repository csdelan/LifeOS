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

  // The Character page with Health's daily chart open, as a chart and as a table, in both
  // themes (next-themes keeps the choice under "theme").
  for (const theme of ["dark", "light"] as const) {
    test(`axe serious/critical: /character with the daily chart, ${theme} theme`, async ({ page }) => {
      const scan = async () => {
        const results = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21aa"]).analyze();
        return results.violations.filter((v) => v.impact === "critical" || v.impact === "serious");
      };
      await page.addInitScript((t) => localStorage.setItem("theme", t), theme);
      await page.goto("/character");
      await expect(page.locator("html")).toHaveClass(new RegExp(theme));
      await page.getByRole("button", { name: "Show the daily chart" }).click();
      await expect(page.getByTestId("day-bar")).toHaveCount(30, { timeout: 10_000 });
      const chart = await scan();
      expect(chart, JSON.stringify(chart, null, 2)).toEqual([]);

      await page.getByRole("button", { name: "Table", exact: true }).click();
      await expect(page.getByRole("table", { name: /newest first/ })).toBeVisible();
      const table = await scan();
      expect(table, JSON.stringify(table, null, 2)).toEqual([]);
    });
  }

  // The Character page's Reports, over a period longer than Health's history (a hollow bar)
  // against a second period (a tick), in both themes.
  for (const theme of ["dark", "light"] as const) {
    test(`axe serious/critical: /character?view=reports compared, ${theme} theme`, async ({ page }) => {
      await page.addInitScript((t) => {
        localStorage.setItem("theme", t);
        localStorage.setItem("lifeos.view.character.reports", JSON.stringify({ period: "90", compare: "30" }));
      }, theme);
      await page.goto("/character?view=reports");
      await expect(page.locator("html")).toHaveClass(new RegExp(theme));
      await expect(page.getByRole("list", { name: "Consistency, last 3 months" })).toContainText("30 days: 80%", {
        timeout: 10_000,
      });
      const results = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21aa"]).analyze();
      const blocking = results.violations.filter((v) => v.impact === "critical" || v.impact === "serious");
      expect(blocking, JSON.stringify(blocking, null, 2)).toEqual([]);
    });
  }

  // The Food Log on a seeded day with entries and a fast, then the Log food sheet with a
  // score picked, in both themes. Opening the page settles diet XP, which stacks toasts, and
  // day changes fade buttons: reduced motion (no transitions, in index.css) scans them settled.
  for (const theme of ["dark", "light"] as const) {
    test(`axe serious/critical: /food with entries and the log sheet, ${theme} theme`, async ({ page }) => {
      const scan = async () => {
        const results = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21aa"]).analyze();
        return results.violations.filter((v) => v.impact === "critical" || v.impact === "serious");
      };
      await page.emulateMedia({ reducedMotion: "reduce" });
      await page.addInitScript((t) => localStorage.setItem("theme", t), theme);
      await page.goto("/food");
      await expect(page.locator("html")).toHaveClass(new RegExp(theme));
      await expect(page.getByRole("heading", { name: "Today", level: 1 })).toBeVisible({ timeout: 10_000 });
      // Two days back holds the seeded 23-hour fast, black coffee and a salmon salad.
      await page.getByRole("button", { name: "Previous day" }).click();
      await page.getByRole("button", { name: "Previous day" }).click();
      await expect(page.getByRole("list", { name: /^Entries for / }).getByRole("listitem")).toHaveCount(2, {
        timeout: 10_000,
      });
      await expect(page.getByText(/^\d+ base → \d+ Health XP/)).toBeVisible();
      const day = await scan();
      expect(day, JSON.stringify(day, null, 2)).toEqual([]);

      await page.getByRole("button", { name: "Today", exact: true }).click();
      await expect(page.getByRole("heading", { name: "Today", level: 1 })).toBeVisible();
      await page.getByRole("button", { name: "Log food", exact: true }).click();
      const sheet = page.getByRole("dialog", { name: "Log food" });
      await sheet.getByRole("radio", { name: "3 · average" }).check();
      await expect(sheet.getByText(/^Average: /)).toBeVisible();
      const log = await scan();
      expect(log, JSON.stringify(log, null, 2)).toEqual([]);
    });
  }

  // The Food Log's Week (stacked bars) and Month (calendar) views, and the Table view, in both
  // themes, around two days back, where the seed logged its last day of food.
  for (const theme of ["dark", "light"] as const) {
    test(`axe serious/critical: /food week and month, ${theme} theme`, async ({ page }) => {
      const scan = async () => {
        const results = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21aa"]).analyze();
        return results.violations.filter((v) => v.impact === "critical" || v.impact === "serious");
      };
      const twoBack = new Date();
      twoBack.setDate(twoBack.getDate() - 2);
      const date = `${twoBack.getFullYear()}-${String(twoBack.getMonth() + 1).padStart(2, "0")}-${String(twoBack.getDate()).padStart(2, "0")}`;
      await page.emulateMedia({ reducedMotion: "reduce" });
      await page.addInitScript((t) => localStorage.setItem("theme", t), theme);
      await page.goto(`/food?view=week&date=${date}`);
      await expect(page.locator("html")).toHaveClass(new RegExp(theme));
      const week = page.getByRole("list", { name: "Diet XP each day, out of 40" });
      await expect(week.getByRole("button", { name: /: 46 Health XP paid/ })).toBeVisible({ timeout: 10_000 });
      const weekScan = await scan();
      expect(weekScan, JSON.stringify(weekScan, null, 2)).toEqual([]);

      await page.getByRole("group", { name: "View" }).getByRole("button", { name: "Month" }).click();
      const month = page.getByRole("group", { name: "Diet XP each day" });
      await expect(month.getByRole("button", { name: /: 46 Health XP paid/ })).toBeVisible({ timeout: 10_000 });
      // The week stays up, dimmed, while the month loads.
      await expect(page.locator("[aria-busy=true]")).toHaveCount(0, { timeout: 10_000 });
      const monthScan = await scan();
      expect(monthScan, JSON.stringify(monthScan, null, 2)).toEqual([]);

      await page.getByRole("button", { name: "Table", exact: true }).click();
      await expect(page.getByRole("table", { name: "Diet XP each day, oldest first" })).toBeVisible();
      const tableScan = await scan();
      expect(tableScan, JSON.stringify(tableScan, null, 2)).toEqual([]);
    });
  }

  // A food entry's photo (FOOD-5), in both themes: picked in the Log food sheet, as a
  // thumbnail on the entry, and full size in the viewer.
  for (const theme of ["dark", "light"] as const) {
    test(`axe serious/critical: /food with a photo, ${theme} theme`, async ({ page }) => {
      const scan = async () => {
        const results = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21aa"]).analyze();
        return results.violations.filter((v) => v.impact === "critical" || v.impact === "serious");
      };
      await page.emulateMedia({ reducedMotion: "reduce" });
      await page.addInitScript((t) => localStorage.setItem("theme", t), theme);
      await page.goto("/food");
      await expect(page.getByRole("heading", { name: "Today", level: 1 })).toBeVisible({ timeout: 10_000 });
      const png = Buffer.from(
        await page.evaluate(() => {
          const canvas = document.createElement("canvas");
          canvas.width = 800;
          canvas.height = 600;
          const g = canvas.getContext("2d")!;
          g.fillStyle = "#15803d";
          g.fillRect(0, 0, 800, 600);
          return canvas.toDataURL("image/png").split(",")[1];
        }),
        "base64",
      );

      await page.getByRole("button", { name: "Log food", exact: true }).click();
      const sheet = page.getByRole("dialog", { name: "Log food" });
      await sheet.getByLabel("Name").fill("Green salad");
      await sheet.getByRole("radio", { name: "5 · very healthy" }).check();
      await sheet.getByLabel("Calories").fill("300");
      const chooser = page.waitForEvent("filechooser");
      await sheet.getByRole("button", { name: "Choose photo" }).click();
      await (await chooser).setFiles({ name: "salad.png", mimeType: "image/png", buffer: png });
      await expect(sheet.getByRole("button", { name: "Remove photo" })).toBeVisible();
      const picked = await scan();
      expect(picked, JSON.stringify(picked, null, 2)).toEqual([]);

      await sheet.getByRole("button", { name: "Log", exact: true }).click();
      await expect(sheet).toBeHidden();
      const thumbnail = page.getByRole("button", { name: "Show the photo of Green salad full size" });
      await expect(thumbnail.locator("img")).toBeVisible({ timeout: 10_000 });
      const gallery = await scan();
      expect(gallery, JSON.stringify(gallery, null, 2)).toEqual([]);

      await thumbnail.click();
      await expect(page.getByRole("dialog", { name: "Green salad" }).getByRole("img")).toBeVisible();
      const viewer = await scan();
      expect(viewer, JSON.stringify(viewer, null, 2)).toEqual([]);
    });
  }

  // The Food Log on a phone: Log food sits at the bottom, and the sheet rises from there.
  test("axe serious/critical: /food on a phone with the log sheet open", async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 812 });
    await page.emulateMedia({ reducedMotion: "reduce" });
    await page.goto("/food");
    await expect(page.getByRole("heading", { name: "Today", level: 1 })).toBeVisible({ timeout: 10_000 });
    await page.getByRole("button", { name: "Log food", exact: true }).click();
    const sheet = page.getByRole("dialog", { name: "Log food" });
    await expect(sheet).toBeVisible();
    const box = await sheet.boundingBox();
    expect(box && box.y + box.height).toBeGreaterThan(800);
    const results = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21aa"]).analyze();
    const blocking = results.violations.filter((v) => v.impact === "critical" || v.impact === "serious");
    expect(blocking, JSON.stringify(blocking, null, 2)).toEqual([]);
  });

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
