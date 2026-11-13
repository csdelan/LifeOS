import { expect, test } from "@playwright/test";
import { beforeActivityXp, bigPng } from "../support";

test.describe("golden paths: the Food Log", () => {
  test("record a fast, log food and close the day for diet XP", async ({ page }) => {
    await beforeActivityXp(page);
    await page.goto("/food");
    await expect(page.getByRole("heading", { name: "Today", level: 1 })).toBeVisible({ timeout: 10_000 });

    // A 23-hour fast ending now: 7 base Health XP, 8 at the mock's ×1.2 today.
    await page.getByRole("button", { name: "Record a past fast" }).click();
    const fast = page.getByRole("dialog", { name: "Record a past fast" });
    const began = await page.evaluate(() => {
      const d = new Date(Date.now() - 23 * 3_600_000);
      const pad = (n: number) => String(n).padStart(2, "0");
      return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
    });
    await fast.getByLabel("Began").fill(began);
    await expect(fast.getByText("A fast of 23 h 00 m")).toBeVisible();
    await fast.getByRole("button", { name: "Save fast" }).click();
    await expect(page.getByText("Earned +8 Health XP")).toBeVisible({ timeout: 10_000 });
    await expect(page.getByRole("list", { name: "Fasts that ended this day" })).toContainText("23 h 00 m");

    // A very healthy 1,200-calorie entry: 28 for quality and 4 for calories, with the fast's 7.
    await page.getByRole("button", { name: "Log food", exact: true }).click();
    const sheet = page.getByRole("dialog", { name: "Log food" });
    await sheet.getByLabel("Name").fill("Salmon salad");
    await sheet.getByRole("radio", { name: "5 · very healthy" }).check();
    await sheet.getByLabel("Calories").fill("1200");
    await sheet.getByRole("button", { name: "Log", exact: true }).click();
    await expect(sheet).toBeHidden();
    await expect(page.getByText("On track for 39 of 40")).toBeVisible({ timeout: 10_000 });
    await expect(page.getByRole("list", { name: "Entries for today" }).getByRole("button", { name: /^Salmon salad/ })).toBeVisible();

    // Closing the day pays the food: 32 base, 38 at ×1.2.
    await page.getByRole("button", { name: "Close day" }).click();
    await expect(page.getByText("Earned +38 Health XP")).toBeVisible({ timeout: 10_000 });
    await expect(page.getByText("39 base → 46 Health XP (×1.2)")).toBeVisible();
  });

  test("Food Log: rate an entry 3.5 on a phone, see it pay halfway, and log it again", async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 812 });
    await beforeActivityXp(page);
    await page.goto("/food");
    await expect(page.getByRole("heading", { name: "Today", level: 1 })).toBeVisible({ timeout: 10_000 });

    // The halves sit in a row of smaller buttons, each under the gap between its neighbours.
    await page.getByRole("button", { name: "Log food", exact: true }).click();
    const sheet = page.getByRole("dialog", { name: "Log food" });
    // All three read at the same instant: the sheet is still sliding up, so boxes read one after
    // another can each be at a different point of the slide.
    const [three, half, four] = await sheet.evaluate((el) =>
      ["3 · average", "3.5 · average to good", "4 · good"].map((name) => {
        const r = el.querySelector(`input[aria-label="${name}"]`)!.closest("label")!.getBoundingClientRect();
        return { x: r.x, y: r.y, width: r.width, height: r.height };
      }),
    );
    expect(half.y).toBeGreaterThan(three.y + three.height - 1);
    expect(half.width).toBeGreaterThanOrEqual(44);
    expect(half.width).toBeLessThan(three.width);
    expect(Math.abs(half.x + half.width / 2 - (three.x + three.width + four.x) / 2)).toBeLessThan(2);

    // 1,200 calories at 3.5: 17.5 for quality and 4 for calories, on track for 22.
    await sheet.getByLabel("Name").fill("Turkey wrap");
    await sheet.getByRole("radio", { name: "3.5 · average to good" }).check();
    await expect(sheet.getByText("Average to good: halfway between 3 and 4.")).toBeVisible();
    await sheet.getByLabel("Calories").fill("1200");
    await sheet.getByRole("button", { name: "Log", exact: true }).click();
    await expect(sheet).toBeHidden();
    await expect(page.getByText("On track for 22 of 40")).toBeVisible({ timeout: 10_000 });
    const entries = page.getByRole("list", { name: "Entries for today" });
    await expect(entries.getByRole("button", { name: /^Turkey wrap, / })).toContainText("3.5 · average to good");

    // Log again keeps the half. On a phone the "Logged" toast sits over the Log food button
    // until it goes; a toast under the pointer waits, so the pointer moves off first.
    await page.mouse.move(0, 0);
    await expect(page.getByText("Logged Turkey wrap")).toBeHidden({ timeout: 10_000 });
    await page.getByRole("button", { name: "Log food", exact: true }).click();
    await sheet.getByRole("button", { name: "Log again: Turkey wrap · 3.5 · 1,200 cal" }).click();
    await expect(sheet.getByRole("radio", { name: "3.5 · average to good" })).toBeChecked();
  });

  test("Food Log: log drinks with the score locked, see the cut and the streak, and count them in the week", async ({
    page,
  }) => {
    await beforeActivityXp(page);
    await page.goto("/food");
    await expect(page.getByRole("heading", { name: "Today", level: 1 })).toBeVisible({ timeout: 10_000 });
    await expect(page.getByRole("region", { name: "Sobriety streak" })).toContainText(/\d+ days sober/);

    // An alcoholic drink scores 1, greyed out, and is typed as a drink.
    await page.getByRole("button", { name: "Log food", exact: true }).click();
    const sheet = page.getByRole("dialog", { name: "Log food" });
    await sheet.getByLabel("Name").fill("IPA");
    await sheet.getByRole("radio", { name: "4 · good" }).check();
    await sheet.getByRole("switch", { name: "Alcoholic drink" }).click();
    await expect(sheet.getByRole("radio", { name: "1 · junk" })).toBeChecked();
    await expect(sheet.getByRole("radio", { name: "4 · good" })).toBeDisabled();
    await expect(sheet.getByText("A drink always scores 1.")).toBeVisible();
    await expect(sheet.getByLabel("Meal type")).toHaveValue("");
    await expect(sheet.getByRole("option", { name: "Drink (it's alcoholic)" })).toBeAttached();
    await sheet.getByLabel("Calories").fill("200");
    await sheet.getByRole("button", { name: "Log", exact: true }).click();
    await expect(sheet).toBeHidden();
    const entries = page.getByRole("list", { name: "Entries for today" });
    await expect(entries.getByRole("button", { name: /^IPA \(alcoholic drink\), / })).toBeVisible({ timeout: 10_000 });
    await expect(page.getByRole("region", { name: "Sobriety streak" })).toContainText("A drink today");

    // Three more with Log again: four drinks take 40% off the day.
    for (let i = 0; i < 3; i++) {
      await page.getByRole("button", { name: "Log food", exact: true }).click();
      await sheet.getByRole("button", { name: "Log again: IPA · drink · 200 cal" }).click();
      await expect(sheet.getByRole("switch", { name: "Alcoholic drink" })).toBeChecked();
      await sheet.getByRole("button", { name: "Log", exact: true }).click();
      await expect(sheet).toBeHidden();
    }
    await expect(page.getByText("4 drinks · 40% off the day's diet XP")).toBeVisible({ timeout: 10_000 });

    // The four show as one card (FOOD-7). Have another adds a fifth; deleting a copy takes it back.
    const card = entries.getByRole("button", { name: /^IPA ×4 \(alcoholic drink\), .+: show the 4 entries$/ });
    await expect(card).toContainText("800 cal");
    await card.click();
    const copies = page.getByRole("dialog", { name: "IPA ×4" });
    await expect(copies.getByRole("list", { name: "Each IPA" }).getByRole("button")).toHaveCount(4);
    await copies.getByRole("button", { name: "Have another" }).click();
    await expect(sheet.getByLabel("Name")).toHaveValue("IPA");
    await expect(sheet.getByRole("switch", { name: "Alcoholic drink" })).toBeChecked();
    await expect(sheet.getByLabel("Calories")).toHaveValue("200");
    await sheet.getByRole("button", { name: "Log", exact: true }).click();
    await expect(sheet).toBeHidden();
    await expect(page.getByText("5 drinks · 50% off the day's diet XP")).toBeVisible({ timeout: 10_000 });

    await entries.getByRole("button", { name: /^IPA ×5 / }).click();
    await page.getByRole("dialog", { name: "IPA ×5" }).getByRole("list", { name: "Each IPA" }).getByRole("button").first().click();
    const change = page.getByRole("dialog", { name: "Change IPA" });
    await change.getByRole("button", { name: "Delete" }).click();
    await page.getByRole("alertdialog").getByRole("button", { name: "Delete" }).click();
    await expect(change).toBeHidden();
    await expect(entries.getByRole("button", { name: /^IPA ×4 / })).toBeVisible({ timeout: 10_000 });
    await expect(page.getByText("4 drinks · 40% off the day's diet XP")).toBeVisible();

    // The week counts them, and the streak as of today.
    await page.getByRole("group", { name: "View" }).getByRole("button", { name: "Week" }).click();
    const totals = page.getByRole("definition");
    await expect(totals.filter({ hasText: /^4 drinks$/ })).toBeVisible({ timeout: 10_000 });
    await expect(totals.filter({ hasText: /^Drink-free \d+ of \d+ days$/ })).toBeVisible();
    await expect(totals.filter({ hasText: /^A drink today · best \d+ days$/ })).toBeVisible();
    await page.getByRole("button", { name: "Table", exact: true }).click();
    await expect(page.getByRole("row", { name: /\(today\)/ })).toContainText(/4$/);
  });

  test("Food Log: switch to the week, step back a month, and open a day", async ({ page }) => {
    await beforeActivityXp(page);
    await page.goto("/food");
    await expect(page.getByRole("heading", { name: "Today", level: 1 })).toBeVisible({ timeout: 10_000 });

    await page.getByRole("group", { name: "View" }).getByRole("button", { name: "Week" }).click();
    await expect(page).toHaveURL(/view=week/);
    await expect(page.getByRole("heading", { name: /^Week of /, level: 1 })).toBeVisible();
    const days = page.getByRole("list", { name: "Diet XP each day, out of 40" }).getByRole("button");
    await expect(days).toHaveCount(7);
    await expect(page.getByRole("definition").filter({ hasText: /^\d+ of \d+$/ })).toBeVisible();

    // The month view holds every seeded day (from six days back); two days back paid 46.
    await page.getByRole("group", { name: "View" }).getByRole("button", { name: "Month" }).click();
    const twoBack = new Date();
    twoBack.setDate(twoBack.getDate() - 2);
    if (twoBack.getMonth() !== new Date().getMonth()) {
      await page.getByRole("button", { name: "Previous month" }).click();
    }
    const day = page.getByRole("group", { name: "Diet XP each day" }).getByRole("button", { name: /: 46 Health XP paid \(39 base\)/ });
    await expect(day).toBeVisible({ timeout: 10_000 });
    await day.click();

    await expect(page).not.toHaveURL(/view=/);
    await expect(page.getByText("39 base → 46 Health XP (×1.2)")).toBeVisible({ timeout: 10_000 });
    await expect(page.getByRole("list", { name: /^Entries for / }).getByRole("listitem")).toHaveCount(2);
  });

  test("Food Log: log an entry with a photo, see it full size, and remove it", async ({ page }) => {
    await beforeActivityXp(page);
    await page.goto("/food");
    await expect(page.getByRole("heading", { name: "Today", level: 1 })).toBeVisible({ timeout: 10_000 });
    const png = await bigPng(page);

    await page.getByRole("button", { name: "Log food", exact: true }).click();
    const sheet = page.getByRole("dialog", { name: "Log food" });
    await sheet.getByLabel("Name").fill("Grain bowl");
    await sheet.getByRole("radio", { name: "4 · good" }).check();
    await sheet.getByLabel("Calories").fill("650");
    const chooser = page.waitForEvent("filechooser");
    await sheet.getByRole("button", { name: "Choose photo" }).click();
    await (await chooser).setFiles({ name: "IMG_4021.png", mimeType: "image/png", buffer: png });
    await expect(sheet.getByRole("button", { name: "Show the photo of Grain bowl full size" })).toBeVisible();
    await expect(sheet.getByText("It's saved with the entry when you log it.")).toBeVisible();
    await sheet.getByRole("button", { name: "Log", exact: true }).click();
    await expect(sheet).toBeHidden();
    await expect(page.getByText("Photo added to Grain bowl")).toBeVisible({ timeout: 10_000 });

    // The entry's thumbnail opens the photo full size, shrunk to 1,200 px.
    const entries = page.getByRole("list", { name: "Entries for today" });
    await entries.getByRole("button", { name: "Show the photo of Grain bowl full size" }).click();
    const viewer = page.getByRole("dialog", { name: "Grain bowl" });
    const photo = viewer.getByRole("img", { name: "Photo of Grain bowl" });
    await expect(photo).toBeVisible();
    expect(await photo.evaluate((img: HTMLImageElement) => [img.naturalWidth, img.naturalHeight])).toEqual([1200, 800]);
    await page.keyboard.press("Escape");
    await expect(viewer).toBeHidden();

    // Remove photo saves at once; the entry stays. The card offers Change or delete and Have another.
    await entries.getByRole("button", { name: /^Grain bowl, .+: change, delete or have another$/ }).click();
    const picker = page.getByRole("dialog", { name: "Grain bowl" });
    await expect(picker.getByRole("button", { name: "Have another" })).toBeVisible();
    await picker.getByRole("button", { name: "Change or delete" }).click();
    const edit = page.getByRole("dialog", { name: "Change Grain bowl" });
    await edit.getByRole("button", { name: "Remove photo" }).click();
    await expect(page.getByText("Photo removed from Grain bowl")).toBeVisible({ timeout: 10_000 });
    await expect(edit.getByRole("button", { name: "Choose photo" })).toBeVisible();
    await page.keyboard.press("Escape");
    await expect(entries.getByRole("button", { name: /^Grain bowl/ })).toBeVisible();
    await expect(entries.getByRole("button", { name: /^Show the photo/ })).toHaveCount(0);
  });

  test("Food Log: Log again and Have another carry the photo, and each entry's photo is its own", async ({ page }) => {
    await beforeActivityXp(page);
    await page.goto("/food");
    await expect(page.getByRole("heading", { name: "Today", level: 1 })).toBeVisible({ timeout: 10_000 });
    const png = await bigPng(page);

    await page.getByRole("button", { name: "Log food", exact: true }).click();
    const sheet = page.getByRole("dialog", { name: "Log food" });
    await sheet.getByLabel("Name").fill("Sausage burrito");
    await sheet.getByRole("radio", { name: "2 · indulgent" }).check();
    await sheet.getByLabel("Calories").fill("650");
    const chooser = page.waitForEvent("filechooser");
    await sheet.getByRole("button", { name: "Choose photo" }).click();
    await (await chooser).setFiles({ name: "IMG_5120.png", mimeType: "image/png", buffer: png });
    await expect(sheet.getByRole("button", { name: "Show the photo of Sausage burrito full size" })).toBeVisible();
    await sheet.getByRole("button", { name: "Log", exact: true }).click();
    await expect(sheet).toBeHidden();
    await expect(page.getByText("Photo added to Sausage burrito")).toBeVisible({ timeout: 10_000 });
    // A toast under the pointer waits, and this one sits over Log food on a phone.
    await page.mouse.move(0, 0);
    await expect(page.getByText("Photo added to Sausage burrito")).toBeHidden({ timeout: 10_000 });

    // Log again fills in the photo too: the one already stored.
    await page.getByRole("button", { name: "Log food", exact: true }).click();
    await sheet.getByRole("button", { name: "Log again: Sausage burrito · 2 · 650 cal" }).click();
    await expect(sheet.getByRole("button", { name: "Show the photo of Sausage burrito full size" })).toBeVisible();
    await expect(sheet.getByText(/^The last one's photo comes with it\./)).toBeVisible();
    await sheet.getByRole("button", { name: "Log", exact: true }).click();
    await expect(sheet).toBeHidden();

    // The two show as one card. The copy has the photo; removing it leaves the first's.
    const entries = page.getByRole("list", { name: "Entries for today" });
    const card = entries.getByRole("button", { name: /^Sausage burrito ×2, / });
    await expect(card).toBeVisible({ timeout: 10_000 });
    await card.click();
    const copies = page.getByRole("dialog", { name: "Sausage burrito ×2" });
    const each = copies.getByRole("list", { name: "Each Sausage burrito" }).getByRole("button");
    await expect(each.last()).toContainText("Change");
    await each.last().click();
    const change = page.getByRole("dialog", { name: "Change Sausage burrito" });
    await expect(change.getByRole("button", { name: "Show the photo of Sausage burrito full size" })).toBeVisible();
    await change.getByRole("button", { name: "Remove photo" }).click();
    await expect(page.getByText("Photo removed from Sausage burrito")).toBeVisible({ timeout: 10_000 });
    await page.keyboard.press("Escape");
    await expect(change).toBeHidden();
    await expect(entries.getByRole("button", { name: "Show the photo of Sausage burrito full size" })).toBeVisible();

    // Have another starts from the last copy, with the card's photo since it has none.
    await card.click();
    await copies.getByRole("button", { name: "Have another" }).click();
    await expect(sheet.getByLabel("Name")).toHaveValue("Sausage burrito");
    await expect(sheet.getByRole("button", { name: "Show the photo of Sausage burrito full size" })).toBeVisible();
    await sheet.getByRole("button", { name: "Log", exact: true }).click();
    await expect(sheet).toBeHidden();
    await expect(entries.getByRole("button", { name: /^Sausage burrito ×3, / })).toBeVisible({ timeout: 10_000 });
  });
});
