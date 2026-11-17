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

  // Sub-goals (focus plan G2): a Goal made under the focus Goal is one of its milestones, and a
  // target date after the focus's is said, not refused.
  test("make a sub-goal under the focus Goal and see it as a milestone on Focus", async ({ page }) => {
    await page.goto("/map?selected=goal-lifeos");
    await page.getByRole("button", { name: "Child", exact: true }).click();
    const dialog = page.getByRole("dialog");
    await dialog.getByRole("combobox", { name: "Subject type" }).click();
    await page.getByRole("option", { name: "Sub-goal" }).click();
    await expect(dialog.getByRole("heading", { name: "New Sub-goal" })).toBeVisible();
    await dialog.getByLabel("Title").fill("Ship the focus plan");
    await dialog.getByLabel("Target date").fill("2099-01-01");
    await expect(dialog.getByText("After Ship LifeOS production UI's target date")).toBeVisible();
    await dialog.getByRole("combobox", { name: "Status" }).click();
    await page.getByRole("option", { name: "Active" }).click();
    await dialog.getByRole("button", { name: "Save" }).click();
    await expect(dialog).toBeHidden();

    await page.getByRole("link", { name: "Focus" }).first().click();
    const focus = page.locator("[data-slot=card]", { hasText: "Primary focus" });
    await expect(focus).toContainText("Ship LifeOS production UI");
    await expect(focus.getByRole("heading", { name: "Milestones" })).toBeVisible();
    await expect(focus.getByRole("link", { name: "Ship the focus plan" })).toBeVisible();
  });
});
