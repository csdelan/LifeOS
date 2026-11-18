import { expect, test } from "@playwright/test";
import { expectAccessible } from "../support";

test.describe("accessibility: connected agents", () => {
  // The agent consent page (agent-access plan §4.4), outside the app shell: the request in both
  // themes and on a phone, and the expired request.
  for (const theme of ["dark", "light"] as const) {
    test(`axe serious/critical: the agent consent page, ${theme} theme`, async ({ page }) => {
      await page.emulateMedia({ reducedMotion: "reduce" });
      await page.addInitScript((t) => localStorage.setItem("theme", t), theme);
      await page.goto("/oauth/consent?authorization_id=mock-1");
      await expect(page.getByRole("heading", { name: "Connect Claude to LifeOS?" })).toBeVisible();
      await expectAccessible(page);
    });
  }

  // Connected agents on the People page (§4.5): an allowed agent with its calls open, one LifeOS
  // refuses, and the confirm before allowing it, in both themes and on a phone.
  for (const theme of ["dark", "light"] as const) {
    test(`axe serious/critical: Connected agents with calls and the allow dialog, ${theme} theme`, async ({ page }) => {
      await page.emulateMedia({ reducedMotion: "reduce" });
      await page.addInitScript((t) => localStorage.setItem("theme", t), theme);
      await page.goto("/people");
      const claude = page.getByRole("article", { name: "Claude" });
      await claude.getByRole("button", { name: "Recent calls" }).click();
      await expect(claude.getByText("life_snapshot")).toBeVisible();
      await expectAccessible(page);

      await page.getByRole("article", { name: "Research helper" }).getByRole("button", { name: "Allow" }).click();
      await expect(page.getByRole("alertdialog")).toBeVisible();
      await expectAccessible(page);
    });
  }

  // "via Claude" (step 2.4): an item's history with an agent's field and status changes, its
  // journal, and the Inbox with an agent's capture, in both themes. The Map's graph isn't this
  // feature's and is scanned on its own page.
  for (const theme of ["dark", "light"] as const) {
    test(`axe serious/critical: what an agent wrote, via its Person, ${theme} theme`, async ({ page }) => {
      await page.emulateMedia({ reducedMotion: "reduce" });
      await page.addInitScript((t) => localStorage.setItem("theme", t), theme);
      await page.goto("/map?selected=task-graph");
      await page.getByRole("tab", { name: "History" }).click();
      await expect(page.getByRole("tabpanel", { name: "History" }).getByText("via Compass").first()).toBeVisible();
      await expectAccessible(page, { exclude: ".react-flow" });

      await page.getByRole("tab", { name: "Journal" }).click();
      await expect(page.getByRole("tabpanel", { name: "Journal" }).getByText("via Compass")).toBeVisible();
      await expectAccessible(page, { exclude: ".react-flow" });

      await page.goto("/inbox");
      await expect(page.getByText("via Compass").first()).toBeVisible();
      await expectAccessible(page);
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
    await expectAccessible(page);
  });

  test("axe serious/critical: the agent consent page on a phone, and an expired request", async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 812 });
    await page.emulateMedia({ reducedMotion: "reduce" });
    await page.goto("/oauth/consent?authorization_id=mock-1");
    await expect(page.getByRole("heading", { name: "Connect Claude to LifeOS?" })).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(375);
    await expectAccessible(page);

    await page.goto("/oauth/consent?authorization_id=mock-expired");
    await expect(page.getByRole("heading", { name: "This request can't be found" })).toBeVisible();
    await expectAccessible(page);
  });
});
