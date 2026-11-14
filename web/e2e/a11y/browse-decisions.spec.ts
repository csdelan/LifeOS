import { expect, test } from "@playwright/test";
import { expectAccessible } from "../support";

test.describe("accessibility: Browse and Decisions", () => {
  // Browse (BROWSE-2): every type as a grid with sortable headers, then Decisions filtered with
  // one open that the filters hide, in both themes.
  for (const theme of ["dark", "light"] as const) {
    test(`axe serious/critical: /browse with filters and an item open, ${theme} theme`, async ({ page }) => {
      await page.setViewportSize({ width: 1600, height: 900 });
      await page.emulateMedia({ reducedMotion: "reduce" });
      await page.addInitScript((t) => localStorage.setItem("theme", t), theme);
      await page.goto("/browse?type=all");
      await expect(page.locator("html")).toHaveClass(new RegExp(theme));
      await expect(page.getByRole("button", { name: "Sort by Status" })).toBeVisible();
      await expectAccessible(page);

      await page.goto("/browse?type=Decision&selected=dec-blazor");
      await page.getByRole("checkbox", { name: "Implementing" }).click();
      await expect(page.getByRole("button", { name: "Show it" })).toBeVisible();
      await expect(page.getByRole("heading", { name: "Blazor for Production UI" })).toBeVisible();
      await expectAccessible(page);
    });
  }

  // Browse on a phone: the type menu and the filters sheet rising from the bottom.
  test("axe serious/critical: /browse on a phone with the filters open", async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 812 });
    await page.emulateMedia({ reducedMotion: "reduce" });
    await page.goto("/browse?type=Task");
    await expect(page.getByRole("combobox", { name: "Type" })).toBeVisible();
    await page.getByRole("button", { name: "Filters" }).click();
    const sheet = page.getByRole("dialog", { name: "Filters" });
    await expect(sheet.getByRole("checkbox", { name: "Not started" })).toBeVisible();
    await expectAccessible(page);
  });

  // Decisions (GEN-13): a Decision's Context / Resulting work / Replaces groups, its Resulting
  // work menu, the New Task form with the pending link, and the replacement form, in both themes.
  for (const theme of ["dark", "light"] as const) {
    test(`axe serious/critical: a Decision's relationships and its New forms, ${theme} theme`, async ({ page }) => {
      await page.setViewportSize({ width: 1600, height: 900 });
      await page.emulateMedia({ reducedMotion: "reduce" });
      await page.addInitScript((t) => localStorage.setItem("theme", t), theme);
      await page.goto("/browse?type=Decision&selected=dec-stack");
      await expect(page.locator("html")).toHaveClass(new RegExp(theme));
      await expect(page.getByRole("heading", { name: "React + Vite for Production UI" })).toBeVisible();
      await page.getByRole("tab", { name: "Relationships" }).click();
      await expect(page.getByRole("region", { name: "Replaces" })).toContainText("Blazor for Production UI");
      await expectAccessible(page);

      await page.getByRole("button", { name: "Resulting work" }).click();
      await expect(page.getByRole("menuitem", { name: "Task" })).toBeVisible();
      await expectAccessible(page);

      await page.getByRole("menuitem", { name: "Task" }).click();
      const newTask = page.getByRole("dialog", { name: "New Task" });
      await expect(newTask.getByRole("group", { name: "Resulting work of" })).toBeVisible();
      await expectAccessible(page);
      await newTask.getByRole("button", { name: "Cancel" }).click();
      await expect(newTask).toBeHidden();

      await page.getByRole("button", { name: "Replacement" }).click();
      const replacement = page.getByRole("dialog", { name: "New Decision" });
      await expect(replacement.getByRole("group", { name: "Replaces" })).toBeVisible();
      await expectAccessible(page);
    });
  }

  // On a phone the detail is a sheet: a Decision's groups, then a Decision recorded from a Problem.
  test("axe serious/critical: a Decision's relationships and Record decision on a phone", async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 812 });
    await page.emulateMedia({ reducedMotion: "reduce" });
    await page.goto("/browse?type=Decision&selected=dec-stack");
    await expect(page.getByRole("heading", { name: "React + Vite for Production UI" })).toBeVisible();
    await page.getByRole("tab", { name: "Relationships" }).click();
    await expect(page.getByRole("region", { name: "Resulting work" })).toContainText("Ship LifeOS production UI");
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(375);
    await expectAccessible(page);

    await page.goto("/browse?type=Problem&selected=prob-sleep");
    await page.getByRole("button", { name: "Record decision" }).click();
    const dialog = page.getByRole("dialog", { name: "New Decision" });
    await expect(dialog.getByRole("group", { name: "Context" })).toContainText("How do I sleep through the night?");
    await expectAccessible(page);
  });
});
