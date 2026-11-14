import { expect, test } from "@playwright/test";

test.describe("golden paths: habits and rewards", () => {
  test("redeem a catalog reward with a note", async ({ page }) => {
    await page.goto("/rewards");
    const freedom = page.getByRole("listitem", { name: /^Freedom: / });
    await expect(freedom).toHaveAccessibleName("Freedom: 60");
    // Movie night costs more Freedom than the wallet holds: blocked, with the reason.
    await expect(page.getByRole("button", { name: "Redeem Movie night" })).toBeDisabled();
    await expect(page.getByText("You have 60 Freedom; this needs 120.")).toBeVisible();

    await page.getByRole("button", { name: "Redeem Episode of a show" }).click();
    const dialog = page.getByRole("dialog", { name: "Redeem Episode of a show" });
    await expect(dialog).toBeVisible();
    await dialog.getByLabel(/What's it for/).fill("Season finale, part two");
    await dialog.getByRole("button", { name: "Redeem", exact: true }).click();

    await expect(page.getByTestId("confetti")).toBeAttached();
    await expect(page.getByText("Redeemed Episode of a show: 45 Freedom")).toBeVisible();
    await expect(dialog).toBeHidden();
    await expect(freedom).toHaveAccessibleName("Freedom: 15");
    await expect(page.getByText("Episode of a show — Season finale, part two")).toBeVisible();
  });

  test("earn on a habit day, then establish a habit that met its target", async ({ page }) => {
    await page.goto("/habits");
    const inbox = page.getByRole("list", { name: "Building" }).getByRole("listitem").filter({ hasText: "Inbox Zero pass" });
    await expect(inbox.getByText("Pays 5 Freedom · 5 Life XP a day")).toBeAttached();
    await inbox.getByRole("button", { name: "Followed", exact: true }).click();
    await expect(page.getByText("Earned +5 Freedom +5 Life XP")).toBeVisible();

    await page.getByRole("button", { name: "Ready to establish Morning mobility" }).click();
    const confirm = page.getByRole("alertdialog", { name: "Establish Morning mobility?" });
    await expect(confirm).toContainText("pays 60 Freedom · 1 Gems · 100 Life XP");
    await confirm.getByRole("button", { name: "Establish" }).click();

    await expect(page.getByText("Earned +60 Freedom +1 Gems +100 Life XP")).toBeVisible();
    const graduated = page.getByRole("region", { name: "Established" });
    await expect(graduated.getByText("Morning mobility")).toBeVisible();
    await expect(page.getByRole("list", { name: "Building" })).not.toContainText("Morning mobility");

    // Maintained, not tracked: a one-tap check-off, nothing missed, and a second tap undoes it.
    const mobility = graduated.getByRole("listitem").filter({ hasText: "Morning mobility" });
    await expect(mobility.getByText("Not logged today")).toBeVisible();
    await expect(mobility.getByRole("button", { name: /not followed/i })).toHaveCount(0);
    const checkOff = mobility.getByRole("button", { name: "Morning mobility: followed today" });
    await checkOff.click();
    await expect(mobility.getByText("Done today")).toBeVisible();
    await expect(checkOff).toHaveAttribute("aria-pressed", "true");
    await checkOff.click();
    await expect(mobility.getByText("Not logged today")).toBeVisible();
  });

  test("Today keeps established habits in a collapsed Maintaining group", async ({ page }) => {
    await page.goto("/habits");
    await page.getByRole("button", { name: "Ready to establish Morning mobility" }).click();
    await page
      .getByRole("alertdialog", { name: "Establish Morning mobility?" })
      .getByRole("button", { name: "Establish" })
      .click();
    await expect(page.getByRole("region", { name: "Established" }).getByText("Morning mobility")).toBeVisible();

    // In-app navigation keeps the mock's state.
    await page.getByRole("link", { name: "Focus" }).first().click();
    const toggle = page.getByRole("button", { name: /Maintaining/ });
    await expect(toggle).toHaveText(/0 of 1 done today/);
    await expect(toggle).toHaveAttribute("aria-expanded", "false");
    await toggle.click();
    const group = page.getByRole("list", { name: "Maintaining" });
    await group.getByRole("button", { name: "Morning mobility: followed today" }).click();
    await expect(group.getByText("Done today")).toBeVisible();
    await expect(toggle).toHaveText(/1 of 1 done today/);
  });

  test("a habit day earned today shows in the rewards history, and opens its habit", async ({ page }) => {
    await page.goto("/habits");
    const inbox = page.getByRole("list", { name: "Building" }).getByRole("listitem").filter({ hasText: "Inbox Zero pass" });
    await inbox.getByRole("button", { name: "Followed", exact: true }).click();
    await expect(page.getByText("Earned +5 Freedom +5 Life XP")).toBeVisible();

    // In-app navigation keeps the mock's state.
    await page.getByRole("link", { name: "Rewards" }).first().click();
    await page.getByRole("button", { name: "See all history" }).click();
    await expect(page).toHaveURL(/view=history/);

    const today = page.getByRole("button", { name: /\(today\): earned / });
    await today.click();
    await expect(today).toHaveAttribute("aria-pressed", "true");
    const entries = page.getByRole("list", { name: /^Entries for / });
    const entry = entries.getByRole("button", { name: "Inbox Zero pass" });
    await expect(entry.first()).toBeVisible();
    await entry.first().click();
    await expect(page).toHaveURL(/selected=habit-inbox/);
    await expect(page.getByRole("heading", { name: /Inbox Zero pass/ }).last()).toBeVisible();
  });

  test("a habit checked off after midnight, on a page left open, lands on the new day", async ({ page }) => {
    // Code review CR58: Focus kept the day it loaded, so a morning tap went to yesterday.
    await page.clock.install({ time: new Date("2026-11-12T23:58:00") });
    await page.goto("/");
    const row = page.getByRole("listitem").filter({ hasText: "Inbox Zero pass" });
    await expect(row.getByText("Unrecorded")).toBeVisible();

    await page.clock.runFor(5 * 60_000);
    await row.getByRole("button", { name: "Followed", exact: true }).click();
    await expect(page.getByText("Earned +5 Freedom +5 Life XP")).toBeVisible();

    // In-app navigation keeps the mock's state: Habits reads the new day and finds it followed.
    await page.getByRole("link", { name: "Habits" }).first().click();
    const inbox = page.getByRole("list", { name: "Building" }).getByRole("listitem").filter({ hasText: "Inbox Zero pass" });
    await expect(inbox.getByRole("button", { name: "Followed", exact: true })).toHaveAttribute("aria-pressed", "true");
  });
});
