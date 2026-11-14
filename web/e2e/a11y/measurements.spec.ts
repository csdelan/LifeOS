import { expect, test } from "@playwright/test";
import { discardSheet, expectAccessible } from "../support";

test.describe("accessibility: Measurements", () => {
  // The Measurements page (MEASURE-1, MEASURE-2): the Weight card with the chart focused and a
  // day read out, then the table, the Log weight sheet with a problem showing, the goal dialog
  // and the ask before a delete, in both themes.
  for (const theme of ["dark", "light"] as const) {
    test(`axe serious/critical: /measurements with the chart, table and sheet, ${theme} theme`, async ({ page }) => {
      await page.emulateMedia({ reducedMotion: "reduce" });
      await page.addInitScript((t) => localStorage.setItem("theme", t), theme);
      await page.goto("/measurements");
      await expect(page.locator("html")).toHaveClass(new RegExp(theme));
      await expect(page.getByTestId("weight-trend")).toHaveText(/^\d+\.\d lb$/);
      const chart = page.getByRole("group", { name: /^Weight by day/ });
      await chart.focus();
      await page.keyboard.press("ArrowLeft");
      await expectAccessible(page);

      await page.getByRole("group", { name: "Show as" }).getByRole("button", { name: "Table" }).click();
      await expect(page.getByRole("table")).toBeVisible();
      await expectAccessible(page);

      await page.getByRole("button", { name: "Log weight" }).click();
      const sheet = page.getByRole("dialog", { name: "Log weight" });
      await sheet.getByLabel("Weight (lb)").fill("192.45");
      await expect(sheet.getByText(/takes at most 1 decimal place/)).toBeVisible();
      await expectAccessible(page);
      await discardSheet(page);
      await expect(sheet).toBeHidden();

      await page.getByRole("button", { name: "Edit Weight" }).click();
      await expect(page.getByRole("dialog", { name: "Edit weight" })).toBeVisible();
      await expectAccessible(page);
      await page.keyboard.press("Escape");

      await page.getByRole("table").getByRole("button", { name: /^Delete / }).first().click();
      await expect(page.getByRole("alertdialog", { name: "Delete this weigh-in?" })).toBeVisible();
      await expectAccessible(page);
      await page.keyboard.press("Escape");

      // A weigh-in's own sheet, with its Delete.
      await page.getByRole("table").getByRole("button", { name: /^Change / }).first().click();
      await expect(page.getByRole("dialog", { name: "Change weigh-in" })).toBeVisible();
      await expectAccessible(page);
    });
  }

  // Any metric (MEASURE-3): blood pressure's row open on its two-line chart with a day read out,
  // then its table of pairs, its Log sheet, its Edit dialog, and Add a metric, in both themes.
  for (const theme of ["dark", "light"] as const) {
    test(`axe serious/critical: /measurements with blood pressure open, ${theme} theme`, async ({ page }) => {
      await page.emulateMedia({ reducedMotion: "reduce" });
      await page.addInitScript((t) => localStorage.setItem("theme", t), theme);
      await page.goto("/measurements");
      await expect(page.locator("html")).toHaveClass(new RegExp(theme));
      const bp = page
        .getByRole("list", { name: "Your metrics" })
        .getByTestId("metric-list-row")
        .filter({ has: page.getByRole("heading", { name: /^Blood pressure/ }) });
      await expect(bp.getByTestId("metric-row-flag")).toHaveText("High");
      await expectAccessible(page);

      await bp.getByRole("button", { name: /^Blood pressure/ }).click();
      const card = bp.getByRole("region", { name: "Blood pressure" });
      const chart = card.getByRole("group", { name: /^Blood pressure by day/ });
      await chart.focus();
      await page.keyboard.press("ArrowLeft");
      await expectAccessible(page);

      await card.getByRole("group", { name: "Show as" }).getByRole("button", { name: "Table" }).click();
      await expect(card.getByRole("table")).toBeVisible();
      await expectAccessible(page);

      await bp.getByRole("button", { name: "Log blood pressure" }).click();
      const sheet = page.getByRole("dialog", { name: "Log blood pressure" });
      await sheet.getByLabel("Systolic (mmHg)").fill("120");
      await sheet.getByLabel("Diastolic (mmHg)").fill("78.5");
      await expect(sheet.getByText(/takes a whole number/)).toBeVisible();
      await expectAccessible(page);
      await discardSheet(page);
      await expect(sheet).toBeHidden();

      await card.getByRole("button", { name: "Edit Blood pressure" }).click();
      await expect(page.getByRole("dialog", { name: "Edit blood pressure" })).toBeVisible();
      await expectAccessible(page);
      await page.keyboard.press("Escape");

      await page.getByRole("button", { name: "Add a metric" }).click();
      const add = page.getByRole("dialog", { name: "Add a metric" });
      await add.getByLabel("Name").fill("Ferritin");
      await expect(add.getByText(/Give the metric a unit/)).toBeVisible();
      await expectAccessible(page);
    });
  }

  // Measurements on a phone: Log weight sits at the bottom, the sheet rises from there with a
  // number pad, and nothing scrolls sideways.
  test("axe serious/critical: /measurements on a phone with the Log weight sheet open", async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 812 });
    await page.emulateMedia({ reducedMotion: "reduce" });
    await page.goto("/measurements");
    await expect(page.getByTestId("weight-trend")).toHaveText(/^\d+\.\d lb$/);
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(375);
    await expectAccessible(page);

    // A row opened on a phone stays inside the screen.
    const bp = page
      .getByRole("list", { name: "Your metrics" })
      .getByTestId("metric-list-row")
      .filter({ has: page.getByRole("heading", { name: /^Blood pressure/ }) });
    await bp.getByRole("button", { name: /^Blood pressure/ }).click();
    await expect(bp.getByRole("region", { name: "Blood pressure" }).getByTestId("trend-chart")).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(375);
    await expectAccessible(page);

    await page.getByRole("button", { name: "Log weight" }).click();
    const sheet = page.getByRole("dialog", { name: "Log weight" });
    await expect(sheet).toBeVisible();
    await expect(sheet.getByLabel("Weight (lb)")).toHaveAttribute("inputmode", "decimal");
    const box = await sheet.boundingBox();
    expect(box && box.y + box.height).toBeGreaterThan(800);
    await expectAccessible(page);
  });

  // Lab results (MEASURE-4): the visit open on its results beside a test's card as a table,
  // then the visit sheet with a problem showing and a test added, and the ask before a delete,
  // in both themes.
  for (const theme of ["dark", "light"] as const) {
    test(`axe serious/critical: /measurements lab results and the visit sheet, ${theme} theme`, async ({ page }) => {
      await page.emulateMedia({ reducedMotion: "reduce" });
      await page.addInitScript((t) => localStorage.setItem("theme", t), theme);
      await page.goto("/measurements");
      await expect(page.locator("html")).toHaveClass(new RegExp(theme));
      const labs = page.getByRole("region", { name: "Lab results" });
      const visit = labs.getByTestId("lab-visit");
      await expect(visit).toHaveCount(1);
      await visit.getByRole("button", { name: /Quest Diagnostics/ }).click();
      await expect(visit.getByTestId("lab-visit-results")).toBeVisible();
      const glucose = labs
        .getByRole("list", { name: "CMP" })
        .getByTestId("metric-list-row")
        .filter({ has: page.getByRole("heading", { name: /^Glucose/ }) });
      await glucose.getByRole("button", { name: /^Glucose/ }).click();
      const card = glucose.getByRole("region", { name: "Glucose" });
      await card.getByRole("group", { name: "Show as" }).getByRole("button", { name: "Table" }).click();
      await expect(card.getByRole("table")).toBeVisible();
      await expectAccessible(page);

      await visit.getByRole("button", { name: "Edit visit" }).click();
      const sheet = page.getByRole("dialog", { name: "Edit lab visit" });
      await sheet.getByLabel(/^Glucose\s*mg\/dL$/).fill("104.5");
      await expect(sheet.getByText(/takes a whole number/)).toBeVisible();
      await sheet.getByRole("button", { name: "Add a test" }).click();
      await expectAccessible(page);

      await sheet.getByRole("button", { name: "Delete visit" }).click();
      await expect(page.getByRole("alertdialog", { name: "Delete this lab visit?" })).toBeVisible();
      await expectAccessible(page);
    });
  }

  // Lab results on a phone: a visit's results fit the screen, and the visit sheet rises from the
  // bottom with a number pad and its rows inside the screen.
  test("axe serious/critical: /measurements lab results on a phone with the visit sheet open", async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 812 });
    await page.emulateMedia({ reducedMotion: "reduce" });
    await page.goto("/measurements");
    const labs = page.getByRole("region", { name: "Lab results" });
    const visit = labs.getByTestId("lab-visit");
    await expect(visit).toHaveCount(1);
    await visit.getByRole("button", { name: /Quest Diagnostics/ }).click();
    const table = visit.getByTestId("lab-visit-results");
    await expect(table).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(375);
    // The results fit without scrolling sideways, eGFR's long unit wrapped under its number.
    expect(await table.evaluate((t) => t.parentElement!.scrollWidth <= t.parentElement!.clientWidth)).toBe(true);
    await expectAccessible(page);

    await labs.getByRole("button", { name: "Log a lab visit" }).click();
    const sheet = page.getByRole("dialog", { name: "Log a lab visit" });
    await expect(sheet).toBeVisible();
    await expect(sheet.getByLabel(/^Glucose\s*mg\/dL$/)).toHaveAttribute("inputmode", "decimal");
    const box = await sheet.boundingBox();
    expect(box && box.y + box.height).toBeGreaterThan(800);
    const right = await sheet.evaluate((el) =>
      Math.max(...[...el.querySelectorAll("input, button")].map((n) => n.getBoundingClientRect().right)),
    );
    expect(right).toBeLessThanOrEqual(375);
    await expectAccessible(page);
  });
});
