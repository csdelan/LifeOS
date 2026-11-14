import { expect, test } from "@playwright/test";
import { expectAccessible } from "../support";

test.describe("accessibility: the Food Log", () => {
  // The Food Log on a seeded day with entries and a fast, then the Log food sheet with a
  // score picked, in both themes. Opening the page settles diet XP, which stacks toasts, and
  // day changes fade buttons: reduced motion (no transitions, in index.css) scans them settled.
  for (const theme of ["dark", "light"] as const) {
    test(`axe serious/critical: /food with entries and the log sheet, ${theme} theme`, async ({ page }) => {
      await page.emulateMedia({ reducedMotion: "reduce" });
      await page.addInitScript((t) => localStorage.setItem("theme", t), theme);
      await page.goto("/food");
      await expect(page.locator("html")).toHaveClass(new RegExp(theme));
      await expect(page.getByRole("heading", { name: "Today", level: 1 })).toBeVisible();
      // Two days back holds the seeded 23-hour fast, black coffee and a salmon salad.
      await page.getByRole("button", { name: "Previous day" }).click();
      await page.getByRole("button", { name: "Previous day" }).click();
      await expect(page.getByRole("list", { name: /^Entries for / }).getByRole("listitem")).toHaveCount(2);
      await expect(page.getByText(/^\d+ base → \d+ Health XP/)).toBeVisible();
      await expectAccessible(page);

      await page.getByRole("button", { name: "Today", exact: true }).click();
      await expect(page.getByRole("heading", { name: "Today", level: 1 })).toBeVisible();
      await page.getByRole("button", { name: "Log food", exact: true }).click();
      const sheet = page.getByRole("dialog", { name: "Log food" });
      await sheet.getByRole("radio", { name: "3 · average" }).check();
      await expect(sheet.getByText(/^Average: /)).toBeVisible();
      await expectAccessible(page);

      // A half point (food-log plan §8): picked, then logged, so the gallery shows its tile.
      await sheet.getByRole("radio", { name: "3.5 · average to good" }).check();
      await expect(sheet.getByText("Average to good: halfway between 3 and 4.")).toBeVisible();
      await expectAccessible(page);
      await sheet.getByLabel("Name").fill("Turkey wrap");
      await sheet.getByLabel("Calories").fill("550");
      await sheet.getByRole("button", { name: "Log", exact: true }).click();
      await expect(sheet).toBeHidden();
      await expect(page.getByRole("list", { name: "Entries for today" })).toContainText("3.5 · average to good");
      await expectAccessible(page);
    });
  }

  // Drinks (FOOD-6), in both themes: the Log food sheet with Alcoholic drink on (the score
  // greyed out at 1), then today with three drinks (one combined card, the cut and the streak),
  // the card's list of copies (FOOD-7), and the week and month with them counted.
  for (const theme of ["dark", "light"] as const) {
    test(`axe serious/critical: /food with drinks, ${theme} theme`, async ({ page }) => {
      await page.emulateMedia({ reducedMotion: "reduce" });
      await page.addInitScript((t) => localStorage.setItem("theme", t), theme);
      await page.goto("/food");
      await expect(page.locator("html")).toHaveClass(new RegExp(theme));
      await expect(page.getByRole("heading", { name: "Today", level: 1 })).toBeVisible();

      const sheet = page.getByRole("dialog", { name: "Log food" });
      await page.getByRole("button", { name: "Log food", exact: true }).click();
      await sheet.getByLabel("Name").fill("Lager");
      await sheet.getByRole("switch", { name: "Alcoholic drink" }).click();
      await sheet.getByLabel("Calories").fill("150");
      await expect(sheet.getByText("A drink always scores 1.")).toBeVisible();
      await expectAccessible(page);
      await sheet.getByRole("button", { name: "Log", exact: true }).click();
      await expect(sheet).toBeHidden();
      for (let i = 0; i < 2; i++) {
        await page.getByRole("button", { name: "Log food", exact: true }).click();
        await sheet.getByRole("button", { name: /^Log again: Lager/ }).click();
        await sheet.getByRole("button", { name: "Log", exact: true }).click();
        await expect(sheet).toBeHidden();
      }
      await expect(page.getByText("3 drinks · 30% off the day's diet XP")).toBeVisible();
      await expectAccessible(page);

      // The three show as one card (FOOD-7), which lists its copies.
      await page.getByRole("button", { name: /^Lager ×3 / }).click();
      const copies = page.getByRole("dialog", { name: "Lager ×3" });
      await expect(copies.getByRole("button", { name: "Have another" })).toBeVisible();
      await expectAccessible(page);
      await page.keyboard.press("Escape");
      await expect(copies).toBeHidden();

      await page.getByRole("group", { name: "View" }).getByRole("button", { name: "Week" }).click();
      await expect(page.getByRole("definition").filter({ hasText: /^3 drinks$/ })).toBeVisible();
      await expectAccessible(page);

      await page.getByRole("group", { name: "View" }).getByRole("button", { name: "Month" }).click();
      await expect(page.getByRole("group", { name: "Diet XP each day" }).getByRole("button", { name: /3 drinks$/ })).toBeVisible();
      await expect(page.locator("[aria-busy=true]")).toHaveCount(0);
      await expectAccessible(page);
    });
  }

  // The Food Log's Week (stacked bars) and Month (calendar) views, and the Table view, in both
  // themes, around two days back, where the seed logged its last day of food.
  for (const theme of ["dark", "light"] as const) {
    test(`axe serious/critical: /food week and month, ${theme} theme`, async ({ page }) => {
      const twoBack = new Date();
      twoBack.setDate(twoBack.getDate() - 2);
      const date = `${twoBack.getFullYear()}-${String(twoBack.getMonth() + 1).padStart(2, "0")}-${String(twoBack.getDate()).padStart(2, "0")}`;
      await page.emulateMedia({ reducedMotion: "reduce" });
      await page.addInitScript((t) => localStorage.setItem("theme", t), theme);
      await page.goto(`/food?view=week&date=${date}`);
      await expect(page.locator("html")).toHaveClass(new RegExp(theme));
      const week = page.getByRole("list", { name: "Diet XP each day, out of 40" });
      await expect(week.getByRole("button", { name: /: 46 Health XP paid/ })).toBeVisible();
      await expectAccessible(page);

      await page.getByRole("group", { name: "View" }).getByRole("button", { name: "Month" }).click();
      const month = page.getByRole("group", { name: "Diet XP each day" });
      await expect(month.getByRole("button", { name: /: 46 Health XP paid/ })).toBeVisible();
      // The week stays up, dimmed, while the month loads.
      await expect(page.locator("[aria-busy=true]")).toHaveCount(0);
      await expectAccessible(page);

      await page.getByRole("button", { name: "Table", exact: true }).click();
      await expect(page.getByRole("table", { name: "Diet XP each day, oldest first" })).toBeVisible();
      await expectAccessible(page);
    });
  }

  // A food entry's photo (FOOD-5), in both themes: picked in the Log food sheet, as a
  // thumbnail on the entry, and full size in the viewer.
  for (const theme of ["dark", "light"] as const) {
    test(`axe serious/critical: /food with a photo, ${theme} theme`, async ({ page }) => {
      await page.emulateMedia({ reducedMotion: "reduce" });
      await page.addInitScript((t) => localStorage.setItem("theme", t), theme);
      await page.goto("/food");
      await expect(page.getByRole("heading", { name: "Today", level: 1 })).toBeVisible();
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
      await expectAccessible(page);

      await sheet.getByRole("button", { name: "Log", exact: true }).click();
      await expect(sheet).toBeHidden();
      const thumbnail = page.getByRole("button", { name: "Show the photo of Green salad full size" });
      await expect(thumbnail.locator("img")).toBeVisible();
      await expectAccessible(page);

      await thumbnail.click();
      await expect(page.getByRole("dialog", { name: "Green salad" }).getByRole("img")).toBeVisible();
      await expectAccessible(page);
      await page.keyboard.press("Escape");
      await expect(page.getByRole("dialog", { name: "Green salad" })).toBeHidden();

      // The entry's dialog: Change or delete, and Have another, which carries the photo.
      await page.getByRole("button", { name: /^Green salad, .+: change, delete or have another$/ }).click();
      const entry = page.getByRole("dialog", { name: "Green salad" });
      await expect(entry.getByRole("button", { name: "Change or delete" })).toBeVisible();
      await expectAccessible(page);
      await entry.getByRole("button", { name: "Have another" }).click();
      await expect(sheet.getByText(/^The last one's photo comes with it\./)).toBeVisible();
      await expect(sheet.getByRole("button", { name: "Show the photo of Green salad full size" }).locator("img")).toBeVisible();
      await expectAccessible(page);
    });
  }

  // The Food Log on a phone: Log food sits at the bottom, and the sheet rises from there.
  test("axe serious/critical: /food on a phone with the log sheet open", async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 812 });
    await page.emulateMedia({ reducedMotion: "reduce" });
    await page.goto("/food");
    await expect(page.getByRole("heading", { name: "Today", level: 1 })).toBeVisible();
    await page.getByRole("button", { name: "Log food", exact: true }).click();
    const sheet = page.getByRole("dialog", { name: "Log food" });
    await expect(sheet).toBeVisible();
    const box = await sheet.boundingBox();
    expect(box && box.y + box.height).toBeGreaterThan(800);
    await expectAccessible(page);
  });
});
