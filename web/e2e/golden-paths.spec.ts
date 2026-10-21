import { expect, test } from "@playwright/test";

test.describe("golden paths", () => {
  test("create via New dialog", async ({ page }) => {
    await page.goto("/");
    await page.getByRole("button", { name: "New" }).click();
    await expect(page.getByRole("dialog")).toBeVisible();
    await page.getByRole("textbox", { name: "Title" }).fill("I finish what I start");
    // An Identity Statement needs its statement (subject-forms).
    await page.getByRole("textbox", { name: "Statement" }).fill("I'm the type of person who ships.");
    await page.getByRole("button", { name: "Save" }).click();
    await expect(page.getByRole("dialog")).toBeHidden({ timeout: 10_000 });
  });

  test("inline create-child in Map", async ({ page }) => {
    await page.goto("/map");
    await expect(page.getByRole("tree", { name: "Alignment outline" })).toBeVisible();
    await page.getByRole("button", { name: "Run a half-marathon", exact: true }).click();
    await page.keyboard.press("Enter");
    const title = page.getByLabel(/New (project|task|goal)/i);
    await expect(title).toBeVisible();
    await title.fill("Track Sunday long runs");
    await page.getByRole("button", { name: "Add", exact: true }).click();
    await expect(page.getByRole("button", { name: "Track Sunday long runs", exact: true })).toBeVisible({
      timeout: 10_000,
    });
  });

  test("edit and save a subject", async ({ page }) => {
    await page.goto("/map?selected=goal-lifeos");
    await expect(page.getByRole("heading", { name: /Ship LifeOS production UI/ })).toBeVisible();
    await page.getByRole("button", { name: "Edit" }).click();
    const description = page.getByLabel("Description");
    await description.fill("Polished across every interface.");
    await page.getByRole("button", { name: "Save" }).click();
    await expect(page.getByRole("button", { name: "Edit" })).toBeVisible({ timeout: 10_000 });
  });

  test("Inbox keyboard triage: arrows, Enter, Drop confirm", async ({ page }) => {
    await page.goto("/inbox");
    await expect(page.getByRole("heading", { name: "Inbox" })).toBeVisible();
    await page.keyboard.press("ArrowDown");
    await page.getByRole("button", { name: "Drop" }).focus();
    await page.keyboard.press("Enter");
    await expect(page.getByRole("alertdialog")).toBeVisible();
    await page.getByRole("button", { name: "Drop" }).last().click();
    await expect(page.getByRole("alertdialog")).toBeHidden({ timeout: 10_000 });
  });

  test("graph node click opens detail peek", async ({ page }) => {
    await page.goto("/map?view=graph");
    const node = page.getByText("Craftsman of systems", { exact: false }).first();
    await expect(node).toBeVisible({ timeout: 20_000 });
    await node.click();
    await expect(page.getByRole("heading", { name: /Craftsman of systems/ })).toBeVisible({
      timeout: 10_000,
    });
  });

  test("quick capture lands in Inbox", async ({ page }) => {
    await page.goto("/inbox");
    await expect(page.getByRole("heading", { name: "Inbox" })).toBeVisible();
    await page.getByRole("button", { name: "Capture", exact: true }).click();
    const dialog = page.getByRole("dialog", { name: "Capture" });
    await expect(dialog).toBeVisible();
    const note = dialog.getByPlaceholder("A note…");
    await note.fill("Standing desk trial this week.");
    await expect(note).toHaveValue("Standing desk trial this week.");
    await dialog.getByRole("button", { name: "Capture" }).click();
    await expect(page.getByText("Captured to Inbox")).toBeVisible({ timeout: 10_000 });
    await expect(page.getByText("Standing desk trial this week.")).toBeVisible({
      timeout: 10_000,
    });
  });

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
    await expect(page.getByText("Redeemed Episode of a show: 45 Freedom")).toBeVisible({ timeout: 10_000 });
    await expect(dialog).toBeHidden();
    await expect(freedom).toHaveAccessibleName("Freedom: 15");
    await expect(page.getByText("Episode of a show — Season finale, part two")).toBeVisible();
  });

  test("earn on a habit day, then establish a habit that met its target", async ({ page }) => {
    await page.goto("/habits");
    const inbox = page.getByRole("list", { name: "Building" }).getByRole("listitem").filter({ hasText: "Inbox Zero pass" });
    await expect(inbox.getByText("Pays 5 Freedom · 5 Life XP a day")).toBeAttached();
    await inbox.getByRole("button", { name: "Followed", exact: true }).click();
    await expect(page.getByText("Earned +5 Freedom +5 Life XP")).toBeVisible({ timeout: 10_000 });

    await page.getByRole("button", { name: "Ready to establish Morning mobility" }).click();
    const confirm = page.getByRole("alertdialog", { name: "Establish Morning mobility?" });
    await expect(confirm).toContainText("pays 60 Freedom · 1 Gems · 100 Life XP");
    await confirm.getByRole("button", { name: "Establish" }).click();

    await expect(page.getByText("Earned +60 Freedom +1 Gems +100 Life XP")).toBeVisible({ timeout: 10_000 });
    const graduated = page.getByRole("region", { name: "Established" });
    await expect(graduated.getByText("Morning mobility")).toBeVisible();
    await expect(page.getByRole("list", { name: "Building" })).not.toContainText("Morning mobility");

    // Maintained, not tracked: a one-tap check-off, nothing missed, and a second tap undoes it.
    const mobility = graduated.getByRole("listitem").filter({ hasText: "Morning mobility" });
    await expect(mobility.getByText("Not logged today")).toBeVisible();
    await expect(mobility.getByRole("button", { name: /not followed/i })).toHaveCount(0);
    const checkOff = mobility.getByRole("button", { name: "Morning mobility: followed today" });
    await checkOff.click();
    await expect(mobility.getByText("Done today")).toBeVisible({ timeout: 10_000 });
    await expect(checkOff).toHaveAttribute("aria-pressed", "true");
    await checkOff.click();
    await expect(mobility.getByText("Not logged today")).toBeVisible({ timeout: 10_000 });
  });

  test("Today keeps established habits in a collapsed Maintaining group", async ({ page }) => {
    await page.goto("/habits");
    await page.getByRole("button", { name: "Ready to establish Morning mobility" }).click();
    await page
      .getByRole("alertdialog", { name: "Establish Morning mobility?" })
      .getByRole("button", { name: "Establish" })
      .click();
    await expect(page.getByRole("region", { name: "Established" }).getByText("Morning mobility")).toBeVisible({
      timeout: 10_000,
    });

    // In-app navigation keeps the mock's state.
    await page.getByRole("link", { name: "Focus" }).first().click();
    const toggle = page.getByRole("button", { name: /Maintaining/ });
    await expect(toggle).toHaveText(/0 of 1 done today/);
    await expect(toggle).toHaveAttribute("aria-expanded", "false");
    await toggle.click();
    const group = page.getByRole("list", { name: "Maintaining" });
    await group.getByRole("button", { name: "Morning mobility: followed today" }).click();
    await expect(group.getByText("Done today")).toBeVisible({ timeout: 10_000 });
    await expect(toggle).toHaveText(/1 of 1 done today/);
  });

  test("a habit day earned today shows in the rewards history, and opens its habit", async ({ page }) => {
    await page.goto("/habits");
    const inbox = page.getByRole("list", { name: "Building" }).getByRole("listitem").filter({ hasText: "Inbox Zero pass" });
    await inbox.getByRole("button", { name: "Followed", exact: true }).click();
    await expect(page.getByText("Earned +5 Freedom +5 Life XP")).toBeVisible({ timeout: 10_000 });

    // In-app navigation keeps the mock's state.
    await page.getByRole("link", { name: "Rewards" }).first().click();
    await page.getByRole("button", { name: "See all history" }).click();
    await expect(page).toHaveURL(/view=history/);

    const today = page.getByRole("button", { name: /\(today\): earned / });
    await today.click();
    await expect(today).toHaveAttribute("aria-pressed", "true");
    const entries = page.getByRole("list", { name: /^Entries for / });
    const entry = entries.getByRole("button", { name: "Inbox Zero pass" });
    await expect(entry.first()).toBeVisible({ timeout: 10_000 });
    await entry.first().click();
    await expect(page).toHaveURL(/selected=habit-inbox/);
    await expect(page.getByRole("heading", { name: /Inbox Zero pass/ }).last()).toBeVisible({ timeout: 10_000 });
  });

  test("theme toggle switches the document class", async ({ page }) => {
    await page.goto("/");
    const html = page.locator("html");
    const before = await html.getAttribute("class");
    await page.getByRole("button", { name: /Switch to (light|dark) theme/ }).click();
    await expect(html).not.toHaveAttribute("class", before ?? "");
  });

  test("mobile nav opens and closes", async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 812 });
    await page.goto("/");
    await page.getByRole("button", { name: "Open navigation" }).click();
    await expect(page.getByRole("dialog", { name: "Navigation" })).toBeVisible();
    await page.getByRole("link", { name: "Inbox" }).click();
    await expect(page).toHaveURL(/inbox/);
    await expect(page.getByRole("dialog", { name: "Navigation" })).toBeHidden();
  });
});
