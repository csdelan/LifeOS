import { expect, test } from "@playwright/test";

test.describe("golden paths: Measurements", () => {
  // Measurements (MEASURE-1, MEASURE-2, measurements plan §4.7): the mock seeds six weeks of
  // weigh-ins ending yesterday, so the page's date is fixed to keep "Today" and the goal steady.
  test("Measurements: log a weigh-in, see it on the chart, then change and delete it in the table", async ({ page }) => {
    await page.clock.install({ time: new Date("2026-11-07T08:30:00") });
    await page.goto("/");
    await page.getByRole("link", { name: "Measurements" }).click();
    await expect(page).toHaveURL(/\/measurements$/);
    await expect(page.getByRole("heading", { name: "Your measurements" })).toBeVisible();

    await expect(page.getByTestId("weight-trend")).toHaveText(/^\d+\.\d lb$/, { timeout: 10_000 });
    await expect(page.getByTestId("weight-goal")).toHaveText(/^185\.0 lb by .+ at [\d.]+ lb a week, from \d+ weigh-ins\.$/);
    const chart = page.getByRole("group", { name: /^Weight by day, last 90 days/ });
    const dots = chart.getByTestId("trend-dot");
    await expect(dots.first()).toBeVisible();
    const before = await dots.count();
    await expect(chart.getByTestId("trend-target")).toBeVisible();

    await page.getByRole("button", { name: "Log weight" }).click();
    const sheet = page.getByRole("dialog", { name: "Log weight" });
    await expect(sheet.getByLabel("Weight (lb)")).toBeFocused();
    await sheet.getByLabel("Weight (lb)").fill("192.45");
    await expect(sheet.getByText("Weight takes at most 1 decimal place; got 192.45.")).toBeVisible();
    await sheet.getByLabel("Weight (lb)").fill("192.4");
    await sheet.getByLabel("Note (optional)").fill("after a run");
    await sheet.getByRole("button", { name: "Log", exact: true }).click();
    await expect(page.getByText("Logged 192.4 lb")).toBeVisible();
    await expect(sheet).toBeHidden();
    await expect(page.getByTestId("weight-last")).toHaveText(/^Last weigh-in 192\.4 lb · Today, 8:30 AM$/);
    await expect(dots).toHaveCount(before + 1);

    // Today's dot reads out from the keyboard.
    await chart.focus();
    await page.keyboard.press("End");
    await expect(page.getByText(/^Today: 192\.4 lb, trend \d+\.\d lb$/)).toBeAttached();

    await page.getByRole("group", { name: "Show as" }).getByRole("button", { name: "Table" }).click();
    const table = page.getByRole("table", { name: "Weight weigh-ins, last 90 days, newest first" });
    const newest = table.getByTestId("metric-row").first();
    await expect(newest).toContainText("Today, 8:30 AM");
    await expect(newest).toContainText("after a run");
    await newest.getByRole("button", { name: /^Change 192\.4 lb/ }).click();
    const change = page.getByRole("dialog", { name: "Change weigh-in" });
    await expect(change.getByLabel("Weight (lb)")).toHaveValue("192.4");
    await change.getByLabel("Weight (lb)").fill("192.0");
    await change.getByRole("button", { name: "Save" }).click();
    await expect(page.getByText("Saved 192.0 lb")).toBeVisible();
    await expect(newest).toContainText("192.0 lb");

    await newest.getByRole("button", { name: /^Delete 192\.0 lb/ }).click();
    const ask = page.getByRole("alertdialog", { name: "Delete this weigh-in?" });
    await ask.getByRole("button", { name: "Delete" }).click();
    await expect(page.getByText("Deleted the reading")).toBeVisible();
    await expect(table.getByTestId("metric-row").first()).not.toContainText("Today");
    await expect(page.getByTestId("weight-last")).toHaveText(/^Last weigh-in \d+\.\d lb · Yesterday, /);
  });

  test("Measurements: change the goal, and narrow the chart to 30 days", async ({ page }) => {
    await page.clock.install({ time: new Date("2026-11-07T08:30:00") });
    await page.goto("/measurements");
    await expect(page.getByTestId("weight-goal")).toHaveText(/^185\.0 lb by /, { timeout: 10_000 });
    await page.getByRole("button", { name: "Edit Weight" }).click();
    const dialog = page.getByRole("dialog", { name: "Edit weight" });
    await dialog.getByLabel("Goal (lb), optional").fill("180");
    await dialog.getByRole("button", { name: "Save" }).click();
    await expect(dialog).toBeHidden();
    await expect(page.getByTestId("weight-goal")).toHaveText(/^180\.0 lb by /);
    await expect(page.getByRole("list", { name: "Legend" })).toContainText("Goal 180.0 lb");

    const ninety = await page.getByTestId("trend-dot").count();
    await page.getByRole("group", { name: "Range" }).getByRole("button", { name: "30 days" }).click();
    await expect(page.getByRole("group", { name: /^Weight by day, last 30 days/ })).toBeVisible();
    await expect.poll(() => page.getByTestId("trend-dot").count()).toBeLessThan(ninety);
    // The range is remembered.
    await page.reload();
    await expect(page.getByRole("group", { name: /^Weight by day, last 30 days/ })).toBeVisible({ timeout: 10_000 });
  });

  // Any metric (MEASURE-3, measurements plan §4.8): the mock seeds blood pressure (its newest
  // pair, two days ago, high on systolic) and Waist under Weight's card.
  test("Measurements: log blood pressure from its row, open its card, and read both lines", async ({ page }) => {
    await page.clock.install({ time: new Date("2026-11-07T08:30:00") });
    await page.goto("/measurements");
    const list = page.getByRole("list", { name: "Your metrics" });
    const bp = list.getByTestId("metric-list-row").filter({ has: page.getByRole("heading", { name: /^Blood pressure/ }) });
    await expect(bp.getByTestId("metric-row-value")).toHaveText("121/78 mmHg", { timeout: 10_000 });
    await expect(bp.getByTestId("metric-row-flag")).toHaveText("High");

    await bp.getByRole("button", { name: "Log blood pressure" }).click();
    const sheet = page.getByRole("dialog", { name: "Log blood pressure" });
    await expect(sheet.getByLabel("Systolic (mmHg)")).toBeFocused();
    await sheet.getByLabel("Systolic (mmHg)").fill("116");
    await sheet.getByLabel("Diastolic (mmHg)").fill("74");
    await sheet.getByLabel("Note (optional)").fill("left arm");
    await sheet.getByRole("button", { name: "Log", exact: true }).click();
    await expect(page.getByText("Logged 116/74 mmHg")).toBeVisible();
    await expect(bp.getByTestId("metric-row-value")).toHaveText("116/74 mmHg");
    await expect(bp.getByTestId("metric-row-flag")).toHaveCount(0);
    await expect(bp.getByTestId("metric-row-when")).toHaveText("Today, 8:30 AM");

    const toggle = bp.getByRole("button", { name: /^Blood pressure/ });
    await toggle.click();
    await expect(toggle).toHaveAttribute("aria-expanded", "true");
    const card = bp.getByRole("region", { name: "Blood pressure" });
    await expect(card.getByTestId("bp-range")).toHaveText("Normal: systolic 90–119, diastolic 60–79 mmHg");
    const chart = card.getByRole("group", { name: /^Blood pressure by day, last 90 days/ });
    await expect(chart.getByTestId("trend-band")).toHaveCount(2);
    await chart.focus();
    await page.keyboard.press("End");
    await expect(page.getByText(/^Today: 116\/74 mmHg, trend \d+\/\d+ mmHg$/)).toBeAttached();

    await card.getByRole("group", { name: "Show as" }).getByRole("button", { name: "Table" }).click();
    const table = card.getByRole("table", { name: "Blood pressure readings, last 90 days, newest first" });
    await expect(table.getByTestId("metric-row").first()).toContainText("left arm");
    // The open row is remembered.
    await page.reload();
    await expect(page.getByRole("region", { name: "Blood pressure" })).toBeVisible({ timeout: 10_000 });
  });

  test("Measurements: add a metric of your own, give it a normal range, then archive and restore it", async ({ page }) => {
    await page.clock.install({ time: new Date("2026-11-07T08:30:00") });
    await page.goto("/measurements");
    await page.getByRole("button", { name: "Add a metric" }).click();
    const add = page.getByRole("dialog", { name: "Add a metric" });
    await expect(add.getByRole("button", { name: /^Add Body fat/ })).toBeVisible();
    await add.getByLabel("Name").fill("Resting heart rate");
    await add.getByLabel("Unit").fill("bpm");
    await add.getByRole("group", { name: "Decimal places" }).getByRole("button", { name: "0" }).click();
    await add.getByRole("button", { name: "Add Resting heart rate" }).click();
    await expect(add).toBeHidden();

    const list = page.getByRole("list", { name: "Your metrics" });
    const rhr = list.getByTestId("metric-list-row").filter({ has: page.getByRole("heading", { name: /^Resting heart rate/ }) });
    const card = rhr.getByRole("region", { name: "Resting heart rate" });
    await expect(card.getByText("Nothing logged yet.")).toBeVisible();

    // The row's Log, and the empty card's own: either opens the sheet.
    await rhr.getByRole("button", { name: "Log resting heart rate" }).first().click();
    const sheet = page.getByRole("dialog", { name: "Log resting heart rate" });
    await sheet.getByLabel("Resting heart rate (bpm)").fill("64");
    await sheet.getByRole("button", { name: "Log", exact: true }).click();
    await expect(page.getByText("Logged 64 bpm")).toBeVisible();
    await expect(rhr.getByTestId("metric-row-value")).toHaveText("64 bpm");

    await card.getByRole("button", { name: "Edit Resting heart rate" }).click();
    const edit = page.getByRole("dialog", { name: "Edit resting heart rate" });
    await expect(edit.getByLabel("Unit")).toBeDisabled();
    await edit.getByLabel("High end").fill("60");
    await edit.getByRole("button", { name: "Save" }).click();
    await expect(edit).toBeHidden();
    await expect(card.getByTestId(/-range$/)).toHaveText("Normal: up to 60 bpm");
    await expect(rhr.getByTestId("metric-row-flag")).toHaveText("High");

    await card.getByRole("button", { name: "Edit Resting heart rate" }).click();
    await page.getByRole("dialog", { name: "Edit resting heart rate" }).getByRole("button", { name: "Archive resting heart rate" }).click();
    const archived = page.getByRole("list", { name: "Archived metrics" });
    await expect(archived).toContainText("Resting heart rate");
    await expect(rhr).toHaveCount(0);
    await archived.getByRole("button", { name: "Restore Resting heart rate" }).click();
    await expect(rhr).toBeVisible();
    await expect(page.getByRole("list", { name: "Archived metrics" })).toHaveCount(0);
  });

  // Lab results (MEASURE-4, measurements plan §4.9): the mock seeds one visit 120 days back with
  // 21 results; Glucose, LDL and non-HDL cholesterol are high against the ranges printed beside them.
  test("Measurements: log a lab visit with a test of your own and its report, open it, then delete it", async ({
    page,
  }) => {
    await page.clock.install({ time: new Date("2026-11-07T08:30:00") });
    await page.goto("/measurements");
    const labs = page.getByRole("region", { name: "Lab results" });
    const visits = labs.getByRole("list", { name: "Visits" });
    await expect(visits.getByTestId("lab-visit")).toHaveCount(1, { timeout: 10_000 });
    await expect(visits.getByTestId("lab-visit-summary")).toHaveText("21 results, 3 outside the range");

    await labs.getByRole("button", { name: "Log a lab visit" }).click();
    const sheet = page.getByRole("dialog", { name: "Log a lab visit" });
    // Filled in from the last report.
    await expect(sheet.getByLabel("Glucose: the range's high end")).toHaveValue("99");
    await sheet.getByLabel("Time (optional)").fill("07:45");
    await sheet.getByLabel("Lab (optional)").fill("LabCorp");
    await sheet.getByLabel("Note (optional)").fill("Fasting");
    await sheet.getByLabel(/^Glucose\s*mg\/dL$/).fill("101");
    await sheet.getByRole("button", { name: "Add a test" }).click();
    await sheet.getByLabel("Test", { exact: true }).fill("TSH");
    await sheet.getByLabel("Unit", { exact: true }).fill("mIU/L");
    await sheet.getByLabel("Result", { exact: true }).fill("2.15");
    await sheet.getByLabel("Low", { exact: true }).fill("0.45");
    await sheet.getByLabel("High", { exact: true }).fill("4.5");
    await sheet.getByLabel("Report (optional)").setInputFiles({
      name: "labcorp-results.pdf",
      mimeType: "application/pdf",
      buffer: Buffer.from("%PDF-1.4\n%%EOF\n"),
    });
    await sheet.getByRole("button", { name: "Save visit" }).click();
    await expect(page.getByText("Saved 2 results from Nov 7")).toBeVisible();
    await expect(sheet).toBeHidden();

    await expect(visits.getByTestId("lab-visit")).toHaveCount(2);
    const visit = visits.getByTestId("lab-visit").first();
    await visit.getByRole("button", { name: "Nov 7, 2026 · LabCorp: 2 results, 1 outside the range, with its report" }).click();
    const results = visit.getByTestId("lab-visit-results");
    await expect(results.getByRole("row", { name: /^Glucose/ })).toContainText("High");
    await expect(results.getByRole("row", { name: /^TSH/ })).toContainText("2.15 mIU/L");
    await expect(visit.getByText("Fasting")).toBeVisible();
    await expect(visit.getByRole("button", { name: "Open the report, labcorp-results.pdf" })).toBeEnabled();
    // TSH is a test now, under Other tests.
    await expect(labs.getByRole("list", { name: "Other tests" }).getByRole("heading", { name: /^TSH/ })).toBeVisible();

    await visit.getByRole("button", { name: "Edit visit" }).click();
    const edit = page.getByRole("dialog", { name: "Edit lab visit" });
    await expect(edit.getByText("labcorp-results.pdf")).toBeVisible();
    await edit.getByRole("button", { name: "Delete visit" }).click();
    await page.getByRole("alertdialog", { name: "Delete this lab visit?" }).getByRole("button", { name: "Delete visit" }).click();
    await expect(page.getByText("Deleted the lab visit")).toBeVisible();
    await expect(visits.getByTestId("lab-visit")).toHaveCount(1);
  });
});
