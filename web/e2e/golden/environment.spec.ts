import { expect, test } from "@playwright/test";
import { bigPng } from "../support";

test.describe("golden paths: Environment", () => {
  test("Environment: a walk-through rates two spaces in one save and lifts the Home Score, then a quick check", async ({
    page,
  }) => {
    await page.goto("/");
    await page.getByRole("link", { name: "Environment" }).first().click();
    await expect(page.getByRole("heading", { name: "Your home", level: 1 })).toBeVisible();
    const score = page.getByTestId("home-score");
    await expect(score).toHaveText(/^\d+\.\d$/, { timeout: 10_000 });
    const before = Number(await score.textContent());
    await expect(page.getByTestId("space-card")).toHaveCount(9);
    await expect(page.getByTestId("fix-next").getByRole("listitem")).toHaveCount(3);

    // The garage was skipped last Sunday, so the walk-through starts there, open.
    await page.getByRole("button", { name: "Walk-through" }).click();
    const sheet = page.getByRole("dialog", { name: "Walk-through" });
    const spaces = sheet.getByTestId("walk-through-space");
    await expect(spaces).toHaveCount(9);
    await expect(spaces.first().getByRole("button", { name: /^Garage/ })).toHaveAttribute("aria-expanded", "true");
    await sheet.getByRole("radio", { name: "5 · put away" }).check();
    await expect(sheet.getByText("Everything put away, surfaces clear.")).toBeVisible();
    await sheet.getByRole("radio", { name: "4.5 · clean to spotless" }).check();
    await expect(sheet.getByText("Between 4 (clean, light dust or smudges) and 5 (freshly cleaned, spotless).")).toBeVisible();
    await sheet.getByRole("radio", { name: "3 · plain" }).check();

    // Opening the kitchen closes the garage; its header says what it will save.
    await sheet.getByRole("button", { name: /^Kitchen/ }).click();
    await expect(sheet.getByRole("button", { name: /^Garage/ })).toContainText(
      "New: tidiness 5, cleanliness 4.5, aesthetics 3",
    );
    await sheet.getByRole("radio", { name: "5 · put away" }).check();
    await sheet.getByLabel("Note").fill("Counters clear");
    await expect(sheet.getByText("2 of 9 spaces rated")).toBeVisible();
    await sheet.getByRole("button", { name: "Save", exact: true }).click();
    await expect(sheet).toBeHidden();
    await expect(page.getByText("Rated 2 spaces")).toBeVisible();
    await expect.poll(async () => Number(await score.textContent()), { timeout: 10_000 }).toBeGreaterThan(before);
    const garage = page.getByRole("article", { name: "Garage" });
    await expect(garage).toContainText("rated today");

    // A card's Rate is a quick check of that space: Tidiness and Cleanliness, Aesthetics a tap away.
    await page.getByRole("button", { name: "Rate Office", exact: true }).click();
    const quick = page.getByRole("dialog", { name: "Rate Office" });
    await expect(quick.getByRole("group", { name: /^Tidiness/ })).toBeVisible();
    await expect(quick.getByRole("group", { name: /^Aesthetics/ })).toHaveCount(0);
    await expect(quick.getByRole("button", { name: "Rate Aesthetics too" })).toBeVisible();
    await quick.getByRole("group", { name: /^Cleanliness/ }).getByRole("radio", { name: "5 · spotless" }).check();
    await quick.getByRole("button", { name: "Save", exact: true }).click();
    await expect(quick).toBeHidden();
    await expect(page.getByText("Rated Office")).toBeVisible();
    await expect(page.getByRole("article", { name: "Office" })).toContainText("rated today");
  });

  test("Environment: a photo in the walk-through becomes Now, and the compare dialog makes it the baseline", async ({
    page,
  }) => {
    await page.goto("/environment");
    await expect(page.getByTestId("home-score")).toHaveText(/^\d+\.\d$/, { timeout: 10_000 });
    const png = await bigPng(page);
    const office = page.getByRole("article", { name: "Office" });
    await expect(office.getByTestId("then-and-now")).toContainText("2 photos");

    // Office's panel shows its baseline to line the new photo up with.
    await page.getByRole("button", { name: "Walk-through" }).click();
    const sheet = page.getByRole("dialog", { name: "Walk-through" });
    await sheet.getByRole("button", { name: /^Office/ }).click();
    await expect(sheet.getByRole("img", { name: /^Office's baseline, / })).toBeVisible();
    await expect(sheet.getByText(/^Line the new photo up with Then, /)).toBeVisible();
    const chooser = page.waitForEvent("filechooser");
    await sheet.getByRole("button", { name: "Choose photo of Office" }).click();
    await (await chooser).setFiles({ name: "IMG_6001.png", mimeType: "image/png", buffer: png });
    await expect(sheet.getByRole("img", { name: "New photo of Office" })).toBeVisible();
    await expect(sheet.getByText("New photo, dated with the walk-through.")).toBeVisible();
    await expect(sheet.getByRole("button", { name: /^Office/ })).toContainText("1 photo");
    await expect(sheet.getByText("Nothing rated yet · 1 photo to add")).toBeVisible();
    await sheet.getByRole("button", { name: "Save", exact: true }).click();
    await expect(sheet).toBeHidden();
    await expect(page.getByText("Added a photo of Office")).toBeVisible({ timeout: 10_000 });
    await expect(office.getByTestId("then-and-now")).toContainText("photo today · 3 photos");

    // Now is today's photo, shrunk to 1,200 px; the compare dialog opens on it, Then beside it.
    await office.getByTestId("photo-now").click();
    const compare = page.getByRole("dialog", { name: "Office" });
    await expect(compare.getByRole("button", { name: /^Right side: Now · / })).toHaveAttribute("aria-pressed", "true");
    await expect(compare.getByRole("button", { name: /^Left side: Then · / })).toBeVisible();
    await expect(compare.getByTestId("filmstrip").getByRole("button")).toHaveCount(3);
    const now = compare.getByTestId("compare-right").locator("img");
    await expect.poll(() => now.evaluate((img: HTMLImageElement) => img.naturalWidth)).toBe(1200);
    await compare.getByRole("button", { name: "Make this the baseline" }).click();
    await expect(page.getByText("Made the baseline")).toBeVisible({ timeout: 10_000 });
    await expect(compare.getByRole("button", { name: "This is the baseline" })).toBeDisabled();
    await page.keyboard.press("Escape");
    await expect(compare).toBeHidden();
    // Then and Now are the same photo now, so the card asks for another to compare.
    await expect(office.getByRole("button", { name: "Add a photo of Office to compare" })).toBeVisible();
  });

  test("Environment: photos from two spots added at once, the second starting its own shot", async ({ page }) => {
    await page.goto("/environment");
    await expect(page.getByTestId("home-score")).toHaveText(/^\d+\.\d$/, { timeout: 10_000 });
    const png = await bigPng(page);
    const garage = page.getByRole("article", { name: "Garage" });
    await garage.getByRole("button", { name: "Add photos of Garage", exact: true }).click();
    const sheet = page.getByRole("dialog", { name: "Add photos of Garage" });
    const chooser = page.waitForEvent("filechooser");
    await sheet.getByRole("button", { name: "Choose photos" }).click();
    await (await chooser).setFiles([
      { name: "IMG_8001.png", mimeType: "image/png", buffer: png },
      { name: "IMG_8002.png", mimeType: "image/png", buffer: png },
    ]);
    const rows = sheet.getByTestId("pending-photo");
    await expect(rows).toHaveCount(2);
    await rows.nth(1).getByLabel("Shot").selectOption({ label: "New shot…" });
    await rows.nth(1).getByLabel("New shot's name").fill("Workbench");
    await sheet.getByRole("button", { name: "Add 2 photos" }).click();
    await expect(sheet).toBeHidden({ timeout: 10_000 });
    await expect(page.getByText("Added 2 photos of Garage and the shot Workbench")).toBeVisible();

    // Each spot is its own shot with its own Then, picked on the card.
    const shots = garage.getByRole("group", { name: "Shot" });
    await expect(shots.getByRole("button")).toHaveText(["Garage", "Workbench"]);
    await expect(garage.getByRole("button", { name: "Add a photo of Garage to compare" })).toBeVisible();
    await shots.getByRole("button", { name: "Workbench" }).click();
    await expect(garage.getByTestId("photo-then")).toHaveAccessibleName(/^Compare Garage · Workbench: Then · /);
  });

  test("Environment: add a space, change its weight, and archive it out of the score", async ({ page }) => {
    await page.goto("/environment");
    const editor = page.getByRole("region", { name: "Manage spaces" });
    await expect(editor.getByTestId("space-row")).toHaveCount(9, { timeout: 10_000 });
    const add = editor.getByRole("heading", { name: "Add a space" }).locator("..");
    await add.getByLabel("Name").fill("Office");
    await add.getByRole("button", { name: "Add space" }).click();
    await expect(add.getByRole("alert")).toHaveText("You already have a space called 'Office'.");
    await add.getByLabel("Name").fill("Attic");
    // With spaces already there, what weight and pace mean is in ⓘ tips, not words.
    await expect(add.getByRole("button", { name: "About weight" })).toBeVisible();
    await add.getByLabel("Weight", { exact: true }).fill("2");
    await add.getByRole("group", { name: "Pace" }).getByRole("button", { name: "1", exact: true }).click();
    await add.getByRole("button", { name: "Add space" }).click();
    await expect(editor.getByTestId("space-row")).toHaveCount(10);
    // One line per space: weight, pace and share in their own columns.
    const attic = editor.getByTestId("space-row").filter({ hasText: "Attic" });
    await expect(attic.getByTestId("space-weight")).toHaveText("2");
    await expect(attic.getByTestId("space-pace")).toHaveText("1");
    await expect(attic.getByTestId("space-share")).toHaveText(/%$/);

    await editor.getByRole("button", { name: "Change Attic" }).click();
    // The row's form comes before Add a space's.
    await editor.getByLabel("Weight", { exact: true }).first().fill("4");
    await editor.getByRole("button", { name: "Save", exact: true }).click();
    await expect(attic.getByTestId("space-weight")).toHaveText("4");
    await expect(attic.getByTestId("space-pace")).toHaveText("1");

    // Not rated yet: its card says so, and it doesn't count.
    await expect(page.getByRole("article", { name: "Attic" })).toContainText("not rated yet");
    await editor.getByRole("button", { name: "Archive Attic" }).click();
    await expect(page.getByRole("article", { name: "Attic" })).toHaveCount(0);
    await expect(editor.getByRole("list", { name: "Archived spaces" })).toContainText("Attic");
    await editor.getByRole("button", { name: "Restore Attic" }).click();
    await expect(page.getByRole("article", { name: "Attic" })).toBeVisible();
  });
});
