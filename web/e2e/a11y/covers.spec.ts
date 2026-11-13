import AxeBuilder from "@axe-core/playwright";
import { expect, test } from "@playwright/test";
import { settled } from "../support";

test.describe("accessibility: covers", () => {
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

  // Covers on cards (COVER-3): Vision's statement and goal tiles, Today's focus
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
      await expect(page.getByText("Photo by Mina Cho on Unsplash")).toBeVisible();
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
    // One tile across on a phone, its photo across the top at 3:2.
    const photo = (await card.getByTestId("cover-image").boundingBox())!;
    const box = (await card.boundingBox())!;
    expect(photo.x).toBe(box.x);
    expect(photo.y).toBe(box.y);
    expect(photo.width).toBe(box.width);
    expect(Math.abs(photo.height - (photo.width * 2) / 3)).toBeLessThan(1);
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
});
