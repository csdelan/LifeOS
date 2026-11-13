import { expect, test } from "@playwright/test";
import { beforeActivityXp } from "../support";

test.describe("golden paths: the Character page", () => {
  test("following a habit day lifts Health a rank, celebrated, and the Character page shows it", async ({ page }) => {
    await beforeActivityXp(page);
    await page.goto("/character");
    const health = page.getByRole("region", { name: "Health", exact: true });
    await expect(health.getByText("Level 9", { exact: true })).toBeVisible({ timeout: 10_000 });
    await expect(health.getByText("Novice", { exact: true })).toBeVisible();

    // In-app navigation keeps the mock's state. The seeded Health history is 33 XP short of
    // level 10, and its last 90 days already support more: today's 90 base XP (108 at ×1.2) reaches it.
    await page.getByRole("link", { name: "Habits" }).first().click();
    const mobility = page.getByRole("list", { name: "Building" }).getByRole("listitem").filter({ hasText: "Morning mobility" });
    await mobility.getByRole("button", { name: "Followed", exact: true }).click();
    await expect(page.getByText("You're now a Health Apprentice.")).toBeVisible({ timeout: 10_000 });

    await page.getByRole("link", { name: "Character" }).first().click();
    await expect(health.getByText("Level 10", { exact: true })).toBeVisible({ timeout: 10_000 });
    await expect(health.getByText("Apprentice", { exact: true })).toBeVisible();
    await expect(health.getByRole("img", { name: "Today: 90 of 20 action XP, active" })).toBeVisible();
    const ranks = page.getByRole("region", { name: "Health ranks" }).getByRole("listitem");
    await expect(ranks.nth(1)).toHaveAttribute("aria-current", "step");
  });

  test("Character Reports show Health's rank over time and each period's figures", async ({ page }) => {
    await beforeActivityXp(page);
    await page.goto("/character");
    await page.getByRole("tab", { name: "Reports" }).click();
    await expect(page).toHaveURL(/view=reports/);
    const chart = page.getByTestId("rank-chart");
    await expect(chart).toHaveAccessibleName(/^Rank over the last 30 days\. Health: Novice throughout, best Novice\./, {
      timeout: 10_000,
    });
    // The seeded sick day, 14 days ago, is hatched.
    await expect(page.getByTestId("exception-band")).toHaveCount(1);

    await page.getByRole("button", { name: "3 months" }).click();
    await expect(chart).toHaveAccessibleName(/^Rank over the last 3 months\./, { timeout: 10_000 });
    await expect(page.getByRole("heading", { name: "By domain, last 3 months" })).toBeVisible();
    await page.getByRole("button", { name: "Table", exact: true }).click();
    await expect(page.getByRole("region", { name: "Rank over the last 3 months, as a table" })).toBeVisible();
  });

  test("mark today as an exception on the Character page, then unmark it", async ({ page }) => {
    await beforeActivityXp(page);
    await page.goto("/character");
    const section = page.getByRole("region", { name: "Exception days" });
    const marks = section.getByRole("list", { name: "Exception days, last 90 days" }).getByRole("listitem");
    await expect(marks).toHaveCount(1, { timeout: 10_000 });
    await expect(marks.first()).toContainText("Sick with a cold");

    await section.getByRole("button", { name: "Mark a day as an exception" }).click();
    const dialog = page.getByRole("dialog", { name: "Mark a day as an exception" });
    await expect(dialog.getByRole("radio", { name: "Today" })).toBeChecked();
    await dialog.getByRole("textbox", { name: "Why" }).fill("Flying home");
    await dialog.getByRole("button", { name: "Mark as exception" }).click();
    await expect(dialog).toBeHidden();

    await expect(marks).toHaveCount(2);
    await expect(marks.first()).toContainText("Flying home");
    const health = page.getByRole("region", { name: "Health", exact: true });
    await expect(
      health.getByRole("img", { name: "Today is an exception day: it doesn't count for or against you." }),
    ).toBeVisible();

    await marks.first().getByRole("button", { name: /^Unmark / }).click();
    await expect(marks).toHaveCount(1);
    await expect(health.getByRole("img", { name: /^Today: 0 of 20 action XP/ })).toBeVisible();
  });
});
