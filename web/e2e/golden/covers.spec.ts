import { expect, test } from "@playwright/test";

test.describe("golden paths: covers", () => {
  test("Covers: add one to a goal from search, see its credit, reposition it with the keyboard, and remove it", async ({
    page,
  }) => {
    await page.goto("/map?selected=goal-half");
    await expect(page.getByRole("heading", { name: "Run a half-marathon" })).toBeVisible({ timeout: 10_000 });
    await page.getByRole("button", { name: "Add cover" }).click();

    const picker = page.getByRole("dialog", { name: "Choose a cover" });
    const search = picker.getByRole("searchbox", { name: "Search Unsplash" });
    await expect(search).toHaveValue("Run a half-marathon");
    await expect(picker.getByRole("button", { name: /^Use the photo by / })).toHaveCount(1);
    await search.fill("anything at all");
    await search.press("Enter");
    await expect(picker.getByRole("button", { name: /^Use the photo by / })).toHaveCount(6);
    await picker.getByRole("button", { name: "More photos" }).click();
    await expect(picker.getByRole("button", { name: /^Use the photo by / })).toHaveCount(8);
    await picker.getByRole("button", { name: /^Use the photo by Teo Marin/ }).click();
    await expect(picker).toBeHidden();

    const banner = page.getByTestId("cover-banner");
    await expect(banner).toContainText("Photo by Teo Marin on Unsplash");
    await expect(banner.getByRole("link", { name: "Unsplash" })).toHaveAttribute(
      "href",
      /utm_source=lifeos&utm_medium=referral/,
    );
    await expect(page.getByRole("button", { name: "Add cover" })).toBeHidden();

    await banner.getByRole("button", { name: "Cover options" }).click();
    await page.getByRole("menuitem", { name: "Reposition" }).click();
    const slider = page.getByRole("slider", { name: /Cover position/ });
    await expect(slider).toBeFocused();
    await page.keyboard.press("ArrowDown");
    await page.keyboard.press("ArrowDown");
    await expect(slider).toHaveAttribute("aria-valuenow", "40");
    await page.getByRole("button", { name: "Save position" }).click();
    await expect(slider).toBeHidden();
    await expect(banner.locator("img")).toHaveCSS("object-position", "50% 40%");

    await banner.getByRole("button", { name: "Cover options" }).click();
    await page.getByRole("menuitem", { name: "Remove cover" }).click();
    await expect(banner).toBeHidden();
    await expect(page.getByRole("button", { name: "Add cover" })).toBeVisible();
  });

  // The mock keeps its data in memory, so this moves between pages with the sidebar.
  test("Covers on cards: the Map graph, Today and Vision show a cover set or removed in the panel", async ({
    page,
  }) => {
    await page.goto("/map?view=graph");
    const half = page.getByTestId("rf__node-goal-half");
    const lifeos = page.getByTestId("rf__node-goal-lifeos");
    await expect(lifeos.locator(".lifeos-node")).toHaveClass(/has-cover/, { timeout: 20_000 });
    await expect(lifeos.locator(".lifeos-node__shell")).toHaveAttribute("title", "Photo by Mina Cho on Unsplash");
    await expect(half.locator(".lifeos-node")).not.toHaveClass(/has-cover/);

    // A cover picked in the panel shows on its node at once, with no new layout.
    await half.click();
    await expect(page.getByRole("heading", { name: "Run a half-marathon" })).toBeVisible({ timeout: 10_000 });
    await page.getByRole("button", { name: "Add cover" }).click();
    const picker = page.getByRole("dialog", { name: "Choose a cover" });
    await picker.getByRole("searchbox", { name: "Search Unsplash" }).fill("meadow");
    await picker.getByRole("button", { name: "Search", exact: true }).click();
    await picker.getByRole("button", { name: /^Use the photo by Lars Brandt/ }).click();
    await expect(picker).toBeHidden();
    await expect(half.locator(".lifeos-node")).toHaveClass(/has-cover/);
    await page.getByRole("button", { name: "Close panel" }).click();

    // And one removed leaves it.
    await lifeos.click();
    const banner = page.getByTestId("cover-banner");
    await expect(banner).toContainText("Photo by Mina Cho on Unsplash", { timeout: 10_000 });
    await banner.getByRole("button", { name: "Cover options" }).click();
    await page.getByRole("menuitem", { name: "Remove cover" }).click();
    await expect(banner).toBeHidden();
    await expect(lifeos.locator(".lifeos-node")).not.toHaveClass(/has-cover/);

    // Today: the focus (Ship LifeOS production UI) has lost its strip; the half-marathon's
    // row leads with its new cover, and the project keeps its own.
    await page.getByRole("link", { name: "Focus" }).click();
    const focus = page.locator("[data-slot=card]", { hasText: "Primary focus" });
    await expect(focus).toContainText("Ship LifeOS production UI");
    await expect(focus.getByTestId("cover-image")).toHaveCount(0);
    await expect(
      page.getByRole("button", { name: /Run a half-marathon/ }).getByTestId("cover-image"),
    ).toHaveAttribute("title", "Photo by Lars Brandt on Unsplash");
    await expect(
      page.getByRole("button", { name: /Production web UI/ }).getByTestId("cover-image"),
    ).toHaveAttribute("title", "Photo by Sam Okafor on Unsplash");

    // Vision: the statement's tile keeps its photo and credit; the goals' tiles follow the panel.
    await page.getByRole("link", { name: "Vision" }).click();
    const tile = (name: string | RegExp) => page.locator("article", { has: page.getByRole("heading", { name }) });
    const steward = tile("Steward of my health");
    await expect(steward.getByTestId("cover-image")).toBeVisible();
    await expect(steward).toContainText("Photo by Ines Vale on Unsplash");
    await expect(steward.getByRole("link", { name: "Ines Vale" })).toHaveAttribute("target", "_blank");
    await expect(tile(/Run a half-marathon/).getByTestId("cover-image")).toBeVisible();
    await expect(tile(/Run a half-marathon/)).toContainText("Photo by Lars Brandt on Unsplash");
    await expect(tile(/Ship LifeOS production UI/).getByTestId("cover-image")).toHaveCount(0);
    await expect(tile(/Ship LifeOS production UI/).getByTestId("cover-placeholder")).toBeVisible();
    await steward.getByRole("button", { name: "Steward of my health" }).click();
    await expect(page.getByTestId("cover-banner")).toContainText("Photo by Ines Vale on Unsplash", {
      timeout: 10_000,
    });
  });
});
