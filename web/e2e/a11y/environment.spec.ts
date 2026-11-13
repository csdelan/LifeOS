import AxeBuilder from "@axe-core/playwright";
import { expect, test } from "@playwright/test";
import { settled } from "../support";

test.describe("accessibility: Environment", () => {
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

  // Photos (HOME-4): the compare dialog with Change day open, then Make this its own shot, then
  // Remove photo asking; the add photos sheet with a photo picked for a new shot; a held photo in
  // the walk-through beside the Kitchen's baseline; and the spaces editor's shots, in both themes.
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
      const changing = await scan();
      expect(changing, JSON.stringify(changing, null, 2)).toEqual([]);
      await compare.getByRole("button", { name: "Make this its own shot" }).click();
      await expect(compare.getByLabel("Name the new shot")).toBeVisible();
      const owning = await scan();
      expect(owning, JSON.stringify(owning, null, 2)).toEqual([]);
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
      await add.getByLabel("Shot").selectOption({ label: "New shot…" });
      await add.getByLabel("New shot's name").fill("Sink");
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
});
