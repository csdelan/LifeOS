import AxeBuilder from "@axe-core/playwright";
import { expect, test, type Page } from "@playwright/test";

// The page starts dark and next-themes switches it to light after it mounts. Under reduced
// motion every element still takes a 0.01ms transition, and the colour reaches each level of
// the page a frame after its parent, so for a moment deep text sits halfway between the
// themes. Wait until no transition is running before a scan, or axe reads that moment.
async function settled(page: Page) {
  await expect
    .poll(() =>
      page.evaluate(
        () => document.getAnimations().filter((a) => a.effect?.getComputedTiming().endTime !== Infinity).length,
      ),
    )
    .toBe(0);
}

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
      await expect(page.getByTestId("day-bar")).toHaveCount(60, { timeout: 10_000 });
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

  // The Food Log on a seeded day with entries and a fast, then the Log food sheet with a
  // score picked, in both themes. Opening the page settles diet XP, which stacks toasts, and
  // day changes fade buttons: reduced motion (no transitions, in index.css) scans them settled.
  for (const theme of ["dark", "light"] as const) {
    test(`axe serious/critical: /food with entries and the log sheet, ${theme} theme`, async ({ page }) => {
      const scan = async () => {
        await settled(page);
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

      // A half point (food-log plan §8): picked, then logged, so the gallery shows its tile.
      await sheet.getByRole("radio", { name: "3.5 · average to good" }).check();
      await expect(sheet.getByText("Average to good: halfway between 3 and 4.")).toBeVisible();
      const half = await scan();
      expect(half, JSON.stringify(half, null, 2)).toEqual([]);
      await sheet.getByLabel("Name").fill("Turkey wrap");
      await sheet.getByLabel("Calories").fill("550");
      await sheet.getByRole("button", { name: "Log", exact: true }).click();
      await expect(sheet).toBeHidden();
      await expect(page.getByRole("list", { name: "Entries for today" })).toContainText("3.5 · average to good", {
        timeout: 10_000,
      });
      const gallery = await scan();
      expect(gallery, JSON.stringify(gallery, null, 2)).toEqual([]);
    });
  }

  // Drinks (FOOD-6), in both themes: the Log food sheet with Alcoholic drink on (the score
  // greyed out at 1), then today with three drinks (one combined card, the cut and the streak),
  // the card's list of copies (FOOD-7), and the week and month with them counted.
  for (const theme of ["dark", "light"] as const) {
    test(`axe serious/critical: /food with drinks, ${theme} theme`, async ({ page }) => {
      const scan = async () => {
        await settled(page);
        const results = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21aa"]).analyze();
        return results.violations.filter((v) => v.impact === "critical" || v.impact === "serious");
      };
      await page.emulateMedia({ reducedMotion: "reduce" });
      await page.addInitScript((t) => localStorage.setItem("theme", t), theme);
      await page.goto("/food");
      await expect(page.locator("html")).toHaveClass(new RegExp(theme));
      await expect(page.getByRole("heading", { name: "Today", level: 1 })).toBeVisible({ timeout: 10_000 });

      const sheet = page.getByRole("dialog", { name: "Log food" });
      await page.getByRole("button", { name: "Log food", exact: true }).click();
      await sheet.getByLabel("Name").fill("Lager");
      await sheet.getByRole("switch", { name: "Alcoholic drink" }).click();
      await sheet.getByLabel("Calories").fill("150");
      await expect(sheet.getByText("A drink always scores 1.")).toBeVisible();
      const sheetScan = await scan();
      expect(sheetScan, JSON.stringify(sheetScan, null, 2)).toEqual([]);
      await sheet.getByRole("button", { name: "Log", exact: true }).click();
      await expect(sheet).toBeHidden();
      for (let i = 0; i < 2; i++) {
        await page.getByRole("button", { name: "Log food", exact: true }).click();
        await sheet.getByRole("button", { name: /^Log again: Lager/ }).click();
        await sheet.getByRole("button", { name: "Log", exact: true }).click();
        await expect(sheet).toBeHidden();
      }
      await expect(page.getByText("3 drinks · 30% off the day's diet XP")).toBeVisible({ timeout: 10_000 });
      const dayScan = await scan();
      expect(dayScan, JSON.stringify(dayScan, null, 2)).toEqual([]);

      // The three show as one card (FOOD-7), which lists its copies.
      await page.getByRole("button", { name: /^Lager ×3 / }).click();
      const copies = page.getByRole("dialog", { name: "Lager ×3" });
      await expect(copies.getByRole("button", { name: "Have another" })).toBeVisible();
      const copiesScan = await scan();
      expect(copiesScan, JSON.stringify(copiesScan, null, 2)).toEqual([]);
      await page.keyboard.press("Escape");
      await expect(copies).toBeHidden();

      await page.getByRole("group", { name: "View" }).getByRole("button", { name: "Week" }).click();
      await expect(page.getByRole("definition").filter({ hasText: /^3 drinks$/ })).toBeVisible({ timeout: 10_000 });
      const weekScan = await scan();
      expect(weekScan, JSON.stringify(weekScan, null, 2)).toEqual([]);

      await page.getByRole("group", { name: "View" }).getByRole("button", { name: "Month" }).click();
      await expect(page.getByRole("group", { name: "Diet XP each day" }).getByRole("button", { name: /3 drinks$/ })).toBeVisible({
        timeout: 10_000,
      });
      await expect(page.locator("[aria-busy=true]")).toHaveCount(0, { timeout: 10_000 });
      const monthScan = await scan();
      expect(monthScan, JSON.stringify(monthScan, null, 2)).toEqual([]);
    });
  }

  // The Food Log's Week (stacked bars) and Month (calendar) views, and the Table view, in both
  // themes, around two days back, where the seed logged its last day of food.
  for (const theme of ["dark", "light"] as const) {
    test(`axe serious/critical: /food week and month, ${theme} theme`, async ({ page }) => {
      const scan = async () => {
        await settled(page);
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
        await settled(page);
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
      await page.keyboard.press("Escape");
      await expect(page.getByRole("dialog", { name: "Green salad" })).toBeHidden();

      // The entry's dialog: Change or delete, and Have another, which carries the photo.
      await page.getByRole("button", { name: /^Green salad, .+: change, delete or have another$/ }).click();
      const entry = page.getByRole("dialog", { name: "Green salad" });
      await expect(entry.getByRole("button", { name: "Change or delete" })).toBeVisible();
      const entryScan = await scan();
      expect(entryScan, JSON.stringify(entryScan, null, 2)).toEqual([]);
      await entry.getByRole("button", { name: "Have another" }).click();
      await expect(sheet.getByText(/^The last one's photo comes with it\./)).toBeVisible();
      await expect(sheet.getByRole("button", { name: "Show the photo of Green salad full size" }).locator("img")).toBeVisible({
        timeout: 10_000,
      });
      const carried = await scan();
      expect(carried, JSON.stringify(carried, null, 2)).toEqual([]);
    });
  }

  // A cover across the top of the detail panel, with its credit and menu, then the cover
  // picker with its photos, in both themes (COVER-1, COVER-2).
  for (const theme of ["dark", "light"] as const) {
    test(`axe serious/critical: a cover banner and the cover picker, ${theme} theme`, async ({ page }) => {
      const scan = async () => {
        await settled(page);
        const results = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21aa"]).analyze();
        return results.violations.filter((v) => v.impact === "critical" || v.impact === "serious");
      };
      await page.emulateMedia({ reducedMotion: "reduce" });
      await page.addInitScript((t) => localStorage.setItem("theme", t), theme);
      await page.goto("/map?selected=goal-lifeos");
      await expect(page.locator("html")).toHaveClass(new RegExp(theme));
      const banner = page.getByTestId("cover-banner");
      await expect(banner).toContainText("Photo by Mina Cho on Unsplash", { timeout: 10_000 });
      const shown = await scan();
      expect(shown, JSON.stringify(shown, null, 2)).toEqual([]);

      await banner.getByRole("button", { name: "Cover options" }).click();
      await page.getByRole("menuitem", { name: "Change cover" }).click();
      const picker = page.getByRole("dialog", { name: "Choose a cover" });
      await picker.getByRole("searchbox", { name: "Search Unsplash" }).fill("anything at all");
      await picker.getByRole("button", { name: "Search", exact: true }).click();
      await expect(picker.getByRole("button", { name: /^Use the photo by / })).toHaveCount(6);
      const picking = await scan();
      expect(picking, JSON.stringify(picking, null, 2)).toEqual([]);
    });
  }

  // Covers on cards (COVER-3): Vision's statement cards and goal thumbnails, Today's focus
  // strip and row thumbnails, and the Map graph's covered nodes, in both themes.
  for (const theme of ["dark", "light"] as const) {
    test(`axe serious/critical: covers on Vision, Today and the Map graph, ${theme} theme`, async ({ page }) => {
      const scan = async () => {
        await settled(page);
        const results = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21aa"]).analyze();
        return results.violations.filter((v) => v.impact === "critical" || v.impact === "serious");
      };
      await page.emulateMedia({ reducedMotion: "reduce" });
      await page.addInitScript((t) => localStorage.setItem("theme", t), theme);

      await page.goto("/vision");
      await expect(page.locator("html")).toHaveClass(new RegExp(theme));
      await expect(page.getByText("Photo by Ines Vale on Unsplash")).toBeVisible({ timeout: 10_000 });
      await expect(page.locator('[title="Photo by Mina Cho on Unsplash"]')).toBeVisible();
      const vision = await scan();
      expect(vision, JSON.stringify(vision, null, 2)).toEqual([]);

      await page.goto("/");
      await expect(page.getByText("Photo by Mina Cho on Unsplash")).toBeVisible({ timeout: 10_000 });
      await expect(page.locator('[title="Photo by Sam Okafor on Unsplash"]')).toBeVisible();
      const today = await scan();
      expect(today, JSON.stringify(today, null, 2)).toEqual([]);

      await page.goto("/map?view=graph");
      await expect(page.locator(".lifeos-node.has-cover")).toHaveCount(3, { timeout: 20_000 });
      const graph = await scan();
      expect(graph, JSON.stringify(graph, null, 2)).toEqual([]);
    });
  }

  // The same cards on a phone.
  test("axe serious/critical: covers on Vision and Today on a phone", async ({ page }) => {
    const scan = async () => {
      await settled(page);
      const results = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21aa"]).analyze();
      return results.violations.filter((v) => v.impact === "critical" || v.impact === "serious");
    };
    await page.setViewportSize({ width: 375, height: 812 });
    await page.emulateMedia({ reducedMotion: "reduce" });

    await page.goto("/vision");
    const card = page.locator("article", { has: page.getByRole("heading", { name: "Steward of my health" }) });
    await expect(card.getByText("Photo by Ines Vale on Unsplash")).toBeVisible({ timeout: 10_000 });
    // A 128 px square on the card's left edge on a phone, filling the card's height.
    const photo = (await card.getByTestId("cover-image").boundingBox())!;
    const box = (await card.boundingBox())!;
    expect(photo.width).toBe(128);
    expect(photo.x).toBe(box.x);
    expect(photo.height).toBeGreaterThanOrEqual(photo.width);
    expect(photo.height).toBe(box.height);
    const vision = await scan();
    expect(vision, JSON.stringify(vision, null, 2)).toEqual([]);

    await page.goto("/");
    await expect(page.getByText("Photo by Mina Cho on Unsplash")).toBeVisible({ timeout: 10_000 });
    const today = await scan();
    expect(today, JSON.stringify(today, null, 2)).toEqual([]);
  });

  // On a phone the detail is a sheet: the banner, repositioning it, and the picker.
  test("axe serious/critical: a cover banner, repositioning and the picker on a phone", async ({ page }) => {
    const scan = async () => {
      await settled(page);
      const results = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21aa"]).analyze();
      return results.violations.filter((v) => v.impact === "critical" || v.impact === "serious");
    };
    await page.setViewportSize({ width: 375, height: 812 });
    await page.emulateMedia({ reducedMotion: "reduce" });
    await page.goto("/map?selected=proj-web");
    const banner = page.getByTestId("cover-banner");
    await expect(banner).toContainText("Photo by Sam Okafor on Unsplash", { timeout: 10_000 });
    const shown = await scan();
    expect(shown, JSON.stringify(shown, null, 2)).toEqual([]);

    await banner.getByRole("button", { name: "Cover options" }).click();
    await page.getByRole("menuitem", { name: "Reposition" }).click();
    await expect(page.getByRole("slider", { name: /Cover position/ })).toBeFocused();
    const moving = await scan();
    expect(moving, JSON.stringify(moving, null, 2)).toEqual([]);
    await page.getByRole("button", { name: "Cancel", exact: true }).click();

    await banner.getByRole("button", { name: "Cover options" }).click();
    await page.getByRole("menuitem", { name: "Change cover" }).click();
    const picker = page.getByRole("dialog", { name: "Choose a cover" });
    await expect(picker.getByRole("button", { name: /^Use the photo by / }).first()).toBeVisible({ timeout: 10_000 });
    const picking = await scan();
    expect(picking, JSON.stringify(picking, null, 2)).toEqual([]);
  });

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

  // The Activity page (ACTIVITY-1..4): the steps, sleep and exercise charts, then the Tables and
  // the year of weeks with the workouts not counted, then a workout's sheet and a night's, in
  // both themes.
  for (const theme of ["dark", "light"] as const) {
    test(`axe serious/critical: /activity with charts, tables and a workout, ${theme} theme`, async ({ page }) => {
      const scan = async () => {
        await settled(page);
        const results = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21aa"]).analyze();
        return results.violations.filter((v) => v.impact === "critical" || v.impact === "serious");
      };
      await page.emulateMedia({ reducedMotion: "reduce" });
      await page.addInitScript((t) => localStorage.setItem("theme", t), theme);
      await page.goto("/activity");
      await expect(page.locator("html")).toHaveClass(new RegExp(theme));
      const steps = page.getByRole("region", { name: "Steps" });
      const sleep = page.getByRole("region", { name: "Sleep and readiness", exact: true });
      const exercise = page.getByRole("region", { name: "Exercise" });
      await expect(steps.getByRole("group", { name: /^Steps by day/ })).toBeVisible({ timeout: 10_000 });
      await expect(sleep.getByRole("list", { name: "Stages" })).toBeVisible();
      await expect(sleep.getByRole("group", { name: /^Sleep score by day/ })).toBeVisible();
      await expect(exercise.getByRole("group", { name: /^Minutes of counted exercise/ })).toBeVisible();
      await expect(page.getByText(/^Synced from Oura /)).toBeVisible();
      // A bar read out, so the readout's tooltip is scanned too.
      await steps.getByRole("group", { name: /^Steps by day/ }).focus();
      const charts = await scan();
      expect(charts, JSON.stringify(charts, null, 2)).toEqual([]);

      await steps.getByRole("button", { name: "Table" }).click();
      await expect(steps.getByRole("table")).toBeVisible();
      await sleep.getByRole("button", { name: "Table" }).click();
      await expect(sleep.getByRole("table")).toBeVisible();
      await exercise.getByRole("button", { name: "Table" }).click();
      await exercise.getByRole("button", { name: /^Show \d+ not counted$/ }).click();
      await expect(exercise.getByText(/^Not counted: /).first()).toBeVisible();
      const tables = await scan();
      expect(tables, JSON.stringify(tables, null, 2)).toEqual([]);

      await steps.getByRole("button", { name: "1 year" }).click();
      await steps.getByRole("button", { name: "Chart" }).click();
      await expect(steps.getByRole("group", { name: /^Average steps a day by week/ })).toBeVisible();
      // The last range stays up, dimmed, while the year loads: scan it settled.
      await expect(steps.locator("[aria-busy=true]")).toHaveCount(0);
      const year = await scan();
      expect(year, JSON.stringify(year, null, 2)).toEqual([]);

      await exercise.getByTestId("workout-row").first().click();
      const sheet = page.getByRole("dialog");
      await sheet.getByRole("textbox", { name: "Note" }).fill("Steady");
      await sheet.getByRole("textbox", { name: "Note" }).press("Enter");
      await expect(sheet.getByText("Saved")).toBeVisible();
      const workout = await scan();
      expect(workout, JSON.stringify(workout, null, 2)).toEqual([]);

      // A night in full, with a nap: the contributor meters and the stages.
      await page.keyboard.press("Escape");
      await expect(sheet).toBeHidden();
      await sleep.getByTestId("metric-row").nth(4).getByRole("button").click();
      await expect(page.getByRole("dialog").getByRole("heading", { name: "A nap" })).toBeVisible();
      await expect(page.getByRole("dialog").getByRole("meter", { name: "HRV balance" })).toBeVisible();
      const night = await scan();
      expect(night, JSON.stringify(night, null, 2)).toEqual([]);
    });
  }

  // Tags from the Oura app (ACTIVITY-6): the Tags card with its counts and every day shown, and
  // a day's sheet with its tags, in both themes.
  for (const theme of ["dark", "light"] as const) {
    test(`axe serious/critical: /activity tags and a day's tags, ${theme} theme`, async ({ page }) => {
      const scan = async () => {
        await settled(page);
        const results = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21aa"]).analyze();
        return results.violations.filter((v) => v.impact === "critical" || v.impact === "serious");
      };
      await page.emulateMedia({ reducedMotion: "reduce" });
      await page.addInitScript((t) => localStorage.setItem("theme", t), theme);
      await page.goto("/activity");
      await expect(page.locator("html")).toHaveClass(new RegExp(theme));
      const tags = page.getByRole("region", { name: "Tags", exact: true });
      await expect(tags.getByTestId("tag-counts")).toBeVisible({ timeout: 10_000 });
      await expect(page.getByText(/^Synced from Oura /)).toBeVisible();
      await tags.getByRole("button", { name: /^Show \d+ more days$/ }).click();
      await expect(tags.getByTestId("tag-comment").first()).toBeVisible();
      const card = await scan();
      expect(card, JSON.stringify(card, null, 2)).toEqual([]);

      await tags.getByTestId("tag-day").nth(2).getByRole("button", { name: /, open the day$/ }).click();
      const sheet = page.getByRole("dialog");
      await expect(sheet.getByRole("list", { name: "Tags from the Oura app" })).toBeVisible();
      await expect(sheet.getByRole("heading", { name: "The night" })).toBeVisible();
      const day = await scan();
      expect(day, JSON.stringify(day, null, 2)).toEqual([]);
    });
  }

  // The Activity page on a phone: one column, and a workout's sheet and last night's rising from
  // the bottom.
  test("axe serious/critical: /activity on a phone with a workout and last night open", async ({ page }) => {
    const scan = async () => {
      await settled(page);
      const results = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21aa"]).analyze();
      return results.violations.filter((v) => v.impact === "critical" || v.impact === "serious");
    };
    await page.setViewportSize({ width: 375, height: 812 });
    await page.emulateMedia({ reducedMotion: "reduce" });
    await page.goto("/activity");
    await expect(page.getByRole("region", { name: "Steps" }).getByRole("group", { name: /^Steps by day/ })).toBeVisible({
      timeout: 10_000,
    });
    const width = await page.evaluate(() => document.documentElement.scrollWidth);
    expect(width).toBeLessThanOrEqual(375);
    const phone = await scan();
    expect(phone, JSON.stringify(phone, null, 2)).toEqual([]);

    await page.getByRole("region", { name: "Exercise" }).getByTestId("workout-row").first().click();
    const sheet = page.getByRole("dialog");
    await expect(sheet.getByRole("textbox", { name: "Note" })).toBeVisible();
    const box = await sheet.boundingBox();
    expect(box && box.y + box.height).toBeGreaterThan(800);
    const open = await scan();
    expect(open, JSON.stringify(open, null, 2)).toEqual([]);

    await page.keyboard.press("Escape");
    await expect(sheet).toBeHidden();
    await page.getByRole("region", { name: "Today", exact: true }).getByRole("button", { name: "Last night in full" }).click();
    const night = page.getByRole("dialog", { name: "Today" });
    await expect(night.getByRole("list", { name: "Readiness contributors" })).toBeVisible();
    const nightBox = await night.boundingBox();
    expect(nightBox && nightBox.y + nightBox.height).toBeGreaterThan(800);
    const lastNight = await scan();
    expect(lastNight, JSON.stringify(lastNight, null, 2)).toEqual([]);

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
    const dayBack = await scan();
    expect(dayBack, JSON.stringify(dayBack, null, 2)).toEqual([]);
  });

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

  // The Environment page (HOME-1..3): the Home Score, Fix next, the space cards, the chart with a
  // day read out, then its table, the walk-through sheet and a card's quick check, in both themes.
  for (const theme of ["dark", "light"] as const) {
    test(`axe serious/critical: /environment with the walk-through, ${theme} theme`, async ({ page }) => {
      const scan = async () => {
        await settled(page);
        const results = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21aa"]).analyze();
        return results.violations.filter((v) => v.impact === "critical" || v.impact === "serious");
      };
      await page.emulateMedia({ reducedMotion: "reduce" });
      await page.addInitScript((t) => localStorage.setItem("theme", t), theme);
      await page.goto("/environment");
      await expect(page.locator("html")).toHaveClass(new RegExp(theme));
      await expect(page.getByTestId("home-score")).toHaveText(/^\d+\.\d$/, { timeout: 10_000 });
      const chart = page.getByRole("group", { name: /^Home Score by day/ });
      await expect(chart).toBeVisible();
      await chart.focus();
      const home = await scan();
      expect(home, JSON.stringify(home, null, 2)).toEqual([]);

      const byDay = page.getByRole("region", { name: "Home Score by day" });
      await byDay.getByRole("button", { name: "Table" }).click();
      await expect(byDay.getByRole("table")).toBeVisible();
      const table = await scan();
      expect(table, JSON.stringify(table, null, 2)).toEqual([]);

      await page.getByRole("button", { name: "Walk-through" }).click();
      const sheet = page.getByRole("dialog", { name: "Walk-through" });
      await sheet.getByRole("radio", { name: "3.5 · some clutter to a few things" }).check();
      await expect(sheet.getByText(/^Between 3/)).toBeVisible();
      const walk = await scan();
      expect(walk, JSON.stringify(walk, null, 2)).toEqual([]);

      await page.keyboard.press("Escape");
      await expect(sheet).toBeHidden();
      await page.getByRole("button", { name: "Rate Kitchen", exact: true }).click();
      const quick = page.getByRole("dialog", { name: "Rate Kitchen" });
      await quick.getByRole("switch", { name: "Same as last time" }).click();
      await quick.getByRole("button", { name: "Rate Aesthetics too" }).click();
      await expect(quick.getByRole("group", { name: /^Aesthetics/ })).toBeVisible();
      const check = await scan();
      expect(check, JSON.stringify(check, null, 2)).toEqual([]);
    });
  }

  // The Environment page on a phone: Walk-through sits at the bottom, and the sheet rises from there.
  test("axe serious/critical: /environment on a phone with the walk-through open", async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 812 });
    await page.emulateMedia({ reducedMotion: "reduce" });
    await page.goto("/environment");
    await expect(page.getByTestId("home-score")).toHaveText(/^\d+\.\d$/, { timeout: 10_000 });
    await page.getByRole("button", { name: "Walk-through" }).click();
    const sheet = page.getByRole("dialog", { name: "Walk-through" });
    await expect(sheet).toBeVisible();
    const box = await sheet.boundingBox();
    expect(box && box.y + box.height).toBeGreaterThan(800);
    await settled(page);
    const results = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21aa"]).analyze();
    const blocking = results.violations.filter((v) => v.impact === "critical" || v.impact === "serious");
    expect(blocking, JSON.stringify(blocking, null, 2)).toEqual([]);
  });

  // Photos (HOME-4): the compare dialog with Change day open and Remove photo asking, the add
  // photos sheet with a photo picked, a held photo in the walk-through beside the Kitchen's
  // baseline, and the spaces editor's shots, in both themes.
  for (const theme of ["dark", "light"] as const) {
    test(`axe serious/critical: /environment photos, ${theme} theme`, async ({ page }) => {
      const scan = async () => {
        await settled(page);
        const results = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21aa"]).analyze();
        return results.violations.filter((v) => v.impact === "critical" || v.impact === "serious");
      };
      await page.emulateMedia({ reducedMotion: "reduce" });
      await page.addInitScript((t) => localStorage.setItem("theme", t), theme);
      await page.goto("/environment");
      await expect(page.locator("html")).toHaveClass(new RegExp(theme));
      await expect(page.getByTestId("home-score")).toHaveText(/^\d+\.\d$/, { timeout: 10_000 });
      const png = Buffer.from(
        await page.evaluate(() => {
          const canvas = document.createElement("canvas");
          canvas.width = 800;
          canvas.height = 600;
          const g = canvas.getContext("2d")!;
          g.fillStyle = "#64748b";
          g.fillRect(0, 0, 800, 600);
          return canvas.toDataURL("image/png").split(",")[1];
        }),
        "base64",
      );

      await page.getByRole("article", { name: "Office" }).getByTestId("photo-then").click();
      const compare = page.getByRole("dialog", { name: "Office" });
      await expect(compare.getByTestId("filmstrip")).toBeVisible();
      await compare.getByRole("button", { name: "Change day" }).click();
      await expect(compare.getByLabel("Day")).toBeVisible();
      await compare.getByRole("button", { name: "Remove photo" }).click();
      await expect(compare.getByRole("button", { name: "Keep it" })).toBeVisible();
      const dialog = await scan();
      expect(dialog, JSON.stringify(dialog, null, 2)).toEqual([]);
      await page.keyboard.press("Escape");
      await expect(compare).toBeHidden();

      await page.getByRole("button", { name: "Add photos of Kitchen" }).click();
      const add = page.getByRole("dialog", { name: "Add photos of Kitchen" });
      const chooser = page.waitForEvent("filechooser");
      await add.getByRole("button", { name: "Choose photos" }).click();
      await (await chooser).setFiles({ name: "IMG_7001.png", mimeType: "image/png", buffer: png });
      await expect(add.getByTestId("pending-photo")).toHaveCount(1);
      const sheet = await scan();
      expect(sheet, JSON.stringify(sheet, null, 2)).toEqual([]);
      await page.keyboard.press("Escape");
      await expect(add).toBeHidden();

      await page.getByRole("button", { name: "Walk-through" }).click();
      const walk = page.getByRole("dialog", { name: "Walk-through" });
      await walk.getByRole("button", { name: /^Kitchen/ }).click();
      const held = page.waitForEvent("filechooser");
      await walk.getByRole("button", { name: "Choose photo of Kitchen · Counters" }).click();
      await (await held).setFiles({ name: "IMG_7002.png", mimeType: "image/png", buffer: png });
      await expect(walk.getByRole("img", { name: "New photo of Kitchen · Counters" })).toBeVisible();
      const walkThrough = await scan();
      expect(walkThrough, JSON.stringify(walkThrough, null, 2)).toEqual([]);
      await page.keyboard.press("Escape");
      await expect(walk).toBeHidden();

      const editor = page.getByRole("region", { name: "Manage spaces" });
      await editor.getByRole("button", { name: "Shots of Kitchen" }).click();
      await expect(editor.getByTestId("shots-editor")).toBeVisible();
      await editor.getByRole("button", { name: "Remove the shot Counters" }).click();
      await expect(editor.getByRole("group", { name: "Remove Counters?" })).toBeVisible();
      const shots = await scan();
      expect(shots, JSON.stringify(shots, null, 2)).toEqual([]);
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
});
