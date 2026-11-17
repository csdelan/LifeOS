import { expect, test } from "@playwright/test";

test.describe("golden paths: focus", () => {
  // The Primary focus (TODAY-1, D6, focus plan G1): a Goal chosen in a review leads Focus over
  // the nearest target date until a review changes it, and No focus lets Focus suggest one.
  test("choose the focus in today's review, see it lead Focus, then choose none", async ({ page }) => {
    await page.goto("/");
    const focus = page.locator("[data-slot=card]", { hasText: "Primary focus" });
    // The toast lands before the review reads again. Complete review is off only while a save
    // runs, and Save stays off once it's done, with nothing left to save.
    const saved = async () => {
      await expect(page.getByRole("button", { name: "Complete review" })).toBeEnabled();
      await expect(page.getByRole("button", { name: "Save" })).toBeDisabled();
    };
    await expect(focus).toContainText("Ship LifeOS production UI");
    await expect(focus).toContainText("Chosen in your daily review.");

    // In the app, so the mock store keeps the choice.
    await page.getByRole("link", { name: "Reviews" }).first().click();
    await page.getByRole("switch", { name: "Keep primary focus" }).click();
    await page.getByRole("combobox", { name: "New focus" }).click();
    await page.getByRole("option", { name: /^Run a half-marathon/ }).click();
    await page.getByRole("button", { name: "Save" }).click();
    await expect(page.getByText("Focus: Run a half-marathon")).toBeVisible();
    await saved();

    await page.getByRole("link", { name: "Focus" }).first().click();
    await expect(focus).toContainText("Run a half-marathon");
    await expect(focus).toContainText("Chosen in your daily review.");

    // Chosen in this review, its Keep focus is off, so the picker is still there.
    await page.getByRole("link", { name: "Reviews" }).first().click();
    await page.getByRole("combobox", { name: "New focus" }).click();
    await page.getByRole("option", { name: "No focus" }).click();
    await page.getByRole("button", { name: "Save" }).click();
    await expect(page.getByText("Focus cleared: Focus suggests a Goal")).toBeVisible();
    await saved();

    await page.getByRole("link", { name: "Focus" }).first().click();
    await expect(focus).toContainText("Sleep through the night");
    await expect(focus).toContainText("Suggested: the Active Goal with the nearest target date.");
    await expect(focus.getByRole("link", { name: "Choose one in a review" })).toBeVisible();
  });
});
