import { expect, test } from "@playwright/test";
import { bigPng } from "../support";

test.describe("golden paths: people", () => {
  // A Person's photo (GEN-15): added on the Person's own page, shrunk in the browser, and shown
  // on the People page; then taken off again.
  test("add a photo to a Person, see it on the People page, and remove it", async ({ page }) => {
    await page.goto("/people");
    const sam = page.getByRole("button", { name: /^Sam/ });
    await expect(sam.locator("img")).toHaveCount(0);
    await sam.click();

    await expect(page.getByRole("heading", { name: "Sam", exact: true })).toBeVisible();
    await page.getByLabel("Photo of Sam").setInputFiles({ name: "sam.png", mimeType: "image/png", buffer: await bigPng(page) });
    await expect(page.getByRole("button", { name: "Change photo" })).toBeVisible();
    await page.getByRole("button", { name: "View the photo of Sam" }).click();
    await expect(page.getByRole("dialog").getByRole("img", { name: "Photo of Sam" })).toBeVisible();
    await page.keyboard.press("Escape");

    // In the app, so the mock store keeps it.
    await page.getByRole("link", { name: "People" }).first().click();
    await expect(sam.locator("img")).toBeVisible();

    await sam.click();
    await page.getByRole("button", { name: "Remove photo" }).click();
    await expect(page.getByRole("button", { name: "Add photo" })).toBeVisible();
    await page.getByRole("link", { name: "People" }).first().click();
    await expect(sam).toBeVisible();
    await expect(sam.locator("img")).toHaveCount(0);
  });
});
