import AxeBuilder from "@axe-core/playwright";
import { expect, test } from "@playwright/test";
import { settled } from "../support";

test.describe("accessibility: connected agents", () => {
  // The agent consent page (agent-access plan §4.4), outside the app shell: the request in both
  // themes and on a phone, and the expired request.
  for (const theme of ["dark", "light"] as const) {
    test(`axe serious/critical: the agent consent page, ${theme} theme`, async ({ page }) => {
      await page.emulateMedia({ reducedMotion: "reduce" });
      await page.addInitScript((t) => localStorage.setItem("theme", t), theme);
      await page.goto("/oauth/consent?authorization_id=mock-1");
      await expect(page.getByRole("heading", { name: "Connect Claude to LifeOS?" })).toBeVisible();
      await settled(page);
      const results = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21aa"]).analyze();
      const blocking = results.violations.filter((v) => v.impact === "critical" || v.impact === "serious");
      expect(blocking, JSON.stringify(blocking, null, 2)).toEqual([]);
    });
  }

  // Connected agents on the People page (§4.5): an allowed agent with its calls open, one LifeOS
  // refuses, and the confirm before allowing it, in both themes and on a phone.
  for (const theme of ["dark", "light"] as const) {
    test(`axe serious/critical: Connected agents with calls and the allow dialog, ${theme} theme`, async ({ page }) => {
      const scan = async () => {
        await settled(page);
        const results = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21aa"]).analyze();
        return results.violations.filter((v) => v.impact === "critical" || v.impact === "serious");
      };
      await page.emulateMedia({ reducedMotion: "reduce" });
      await page.addInitScript((t) => localStorage.setItem("theme", t), theme);
      await page.goto("/people");
      const claude = page.getByRole("article", { name: "Claude" });
      await claude.getByRole("button", { name: "Recent calls" }).click();
      await expect(claude.getByText("life_snapshot")).toBeVisible();
      const open = await scan();
      expect(open, JSON.stringify(open, null, 2)).toEqual([]);

      await page.getByRole("article", { name: "Research helper" }).getByRole("button", { name: "Allow" }).click();
      await expect(page.getByRole("alertdialog")).toBeVisible();
      const dialog = await scan();
      expect(dialog, JSON.stringify(dialog, null, 2)).toEqual([]);
    });
  }

  test("axe serious/critical: Connected agents on a phone", async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 812 });
    await page.emulateMedia({ reducedMotion: "reduce" });
    await page.goto("/people");
    const claude = page.getByRole("article", { name: "Claude" });
    await claude.getByRole("button", { name: "Recent calls" }).click();
    await expect(claude.getByText("life_snapshot")).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(375);
    await settled(page);
    const results = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21aa"]).analyze();
    const blocking = results.violations.filter((v) => v.impact === "critical" || v.impact === "serious");
    expect(blocking, JSON.stringify(blocking, null, 2)).toEqual([]);
  });

  test("axe serious/critical: the agent consent page on a phone, and an expired request", async ({ page }) => {
    const scan = async () => {
      await settled(page);
      const results = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21aa"]).analyze();
      return results.violations.filter((v) => v.impact === "critical" || v.impact === "serious");
    };
    await page.setViewportSize({ width: 375, height: 812 });
    await page.emulateMedia({ reducedMotion: "reduce" });
    await page.goto("/oauth/consent?authorization_id=mock-1");
    await expect(page.getByRole("heading", { name: "Connect Claude to LifeOS?" })).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(375);
    const ask = await scan();
    expect(ask, JSON.stringify(ask, null, 2)).toEqual([]);

    await page.goto("/oauth/consent?authorization_id=mock-expired");
    await expect(page.getByRole("heading", { name: "This request can't be found" })).toBeVisible();
    const expired = await scan();
    expect(expired, JSON.stringify(expired, null, 2)).toEqual([]);
  });
});
