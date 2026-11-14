import { expect, test } from "@playwright/test";
import { bigPng, expectAccessible } from "../support";

test.describe("accessibility: people", () => {
  // A Person's photo control and the People page with a photo and the revoked agents shown
  // (GEN-15), in both themes.
  for (const theme of ["dark", "light"] as const) {
    test(`axe serious/critical: a Person's photo, the People page and Show revoked, ${theme} theme`, async ({ page }) => {
      await page.emulateMedia({ reducedMotion: "reduce" });
      await page.addInitScript((t) => localStorage.setItem("theme", t), theme);
      await page.goto("/people");
      await page.getByRole("button", { name: /^Sam/ }).click();
      await expect(page.getByRole("button", { name: "Add photo" })).toBeVisible();
      await expectAccessible(page);

      await page.getByLabel("Photo of Sam").setInputFiles({ name: "sam.png", mimeType: "image/png", buffer: await bigPng(page) });
      await expect(page.getByRole("button", { name: "Change photo" })).toBeVisible();
      await expectAccessible(page);

      await page.getByRole("link", { name: "People" }).first().click();
      await page.getByRole("article", { name: "Claude" }).getByRole("button", { name: "Revoke" }).click();
      await page.getByRole("alertdialog").getByRole("button", { name: "Revoke" }).click();
      await page.getByRole("switch", { name: "Show revoked (1)" }).click();
      await expect(page.getByRole("article", { name: "Claude" }).getByText("Revoked", { exact: true })).toBeVisible();
      await expectAccessible(page);
    });
  }
});
