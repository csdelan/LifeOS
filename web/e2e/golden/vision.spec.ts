import { expect, test } from "@playwright/test";
import { settled } from "../support";

test.describe("golden paths: vision", () => {
  // Chris's notes from his phone: Vision opens on his identity statements with the goals a tab
  // away; a goal's title spans the sheet rather than wrapping beside the buttons; and the
  // detail's tabs wrap onto a second row instead of running off the side.
  test("Vision on a phone: statements first, goals in their own tab, and a goal's detail fits the sheet", async ({
    page,
  }) => {
    await page.setViewportSize({ width: 375, height: 812 });
    await page.goto("/vision");
    const statements = page.getByRole("tab", { name: "Identity statements" });
    const goals = page.getByRole("tab", { name: "Goals" });
    await expect(statements).toHaveAttribute("aria-selected", "true");
    await expect(page.getByRole("heading", { name: "Steward of my health" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Ship LifeOS production UI" })).toHaveCount(0);

    await goals.click();
    await expect(page).toHaveURL(/view=goals/);
    await expect(page.getByRole("heading", { name: "Steward of my health" })).toHaveCount(0);
    await page.getByRole("button", { name: "Ship LifeOS production UI" }).click();

    const sheet = page.getByRole("dialog");
    const title = sheet.locator("header").getByRole("heading", { name: "Ship LifeOS production UI" });
    await expect(title).toBeVisible();
    // Measured once the sheet has slid in: mid-way it's a fraction of a pixel wider than the screen.
    await settled(page);
    const sheetBox = (await sheet.boundingBox())!;
    const titleBox = (await title.boundingBox())!;
    const lineHeight = await title.evaluate((h) => parseFloat(getComputedStyle(h).lineHeight));
    // The sheet's width less its 20 px sides, and one line.
    expect(titleBox.width).toBeGreaterThanOrEqual(sheetBox.width - 41);
    expect(titleBox.height).toBeLessThan(lineHeight * 1.5);

    // Every tab is on screen, on more than one row, and the strip doesn't scroll.
    const tabs = sheet.getByRole("tab");
    await expect(tabs).toHaveCount(5);
    const tops = new Set<number>();
    for (const tab of await tabs.all()) {
      const box = (await tab.boundingBox())!;
      expect(box.x).toBeGreaterThanOrEqual(sheetBox.x);
      expect(box.x + box.width).toBeLessThanOrEqual(sheetBox.x + sheetBox.width);
      tops.add(Math.round(box.y));
    }
    expect(tops.size).toBeGreaterThan(1);
    const strip = sheet.getByRole("tablist");
    expect(await strip.evaluate((el) => el.scrollWidth <= el.clientWidth)).toBe(true);
    await sheet.getByRole("tab", { name: "History" }).click();
    await expect(sheet.getByRole("tab", { name: "History" })).toHaveAttribute("aria-selected", "true");

    // Back from the goal lands on the goals.
    await page.goBack();
    await expect(goals).toHaveAttribute("aria-selected", "true");
  });
});
