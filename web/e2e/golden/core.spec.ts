import { expect, test } from "@playwright/test";
import { GRAPH_DRAWN } from "../support";

test.describe("golden paths: creating, the Map, the Inbox and the shell", () => {
  test("create via New dialog", async ({ page }) => {
    await page.goto("/");
    await page.getByRole("button", { name: "New" }).click();
    await expect(page.getByRole("dialog")).toBeVisible();
    await page.getByRole("textbox", { name: "Title" }).fill("I finish what I start");
    // An Identity Statement needs its statement (subject-forms).
    await page.getByRole("textbox", { name: "Statement" }).fill("I'm the type of person who ships.");
    await page.getByRole("button", { name: "Save" }).click();
    await expect(page.getByRole("dialog")).toBeHidden();
  });

  test("inline create-child in Map", async ({ page }) => {
    await page.goto("/map");
    await expect(page.getByRole("list", { name: "Alignment outline" })).toBeVisible();
    await page.getByRole("button", { name: "Run a half-marathon", exact: true }).click();
    await page.keyboard.press("Enter");
    const title = page.getByLabel(/New (project|task|goal)/i);
    await expect(title).toBeVisible();
    await title.fill("Track Sunday long runs");
    await page.getByRole("button", { name: "Add", exact: true }).click();
    await expect(page.getByRole("button", { name: "Track Sunday long runs", exact: true })).toBeVisible();
  });

  test("Enter on a selected row's toggle opens or closes it, and adds nothing (code review CR87)", async ({ page }) => {
    await page.goto("/map");
    await expect(page.getByRole("list", { name: "Alignment outline" })).toBeVisible();
    await page.getByRole("button", { name: "Run a half-marathon", exact: true }).click();
    const toggle = page.getByRole("button", { name: /^(Expand|Collapse) Run a half-marathon$/ }).first();
    const before = await toggle.getAttribute("aria-label");
    const after = before!.startsWith("Expand") ? before!.replace("Expand", "Collapse") : before!.replace("Collapse", "Expand");
    await toggle.focus();
    await page.keyboard.press("Enter");
    await expect(page.getByRole("button", { name: after, exact: true }).first()).toBeVisible();
    await expect(page.getByLabel(/New (project|task|goal)/i)).toHaveCount(0);
  });

  test("Tab leaves the Map's inline create, and its type buttons say which is chosen (code review CR95)", async ({
    page,
  }) => {
    await page.goto("/map");
    await expect(page.getByRole("list", { name: "Alignment outline" })).toBeVisible();
    await page.getByRole("button", { name: "Run a half-marathon", exact: true }).click();
    await page.keyboard.press("Enter");
    const types = page.getByRole("group", { name: "New item's type", exact: true }).getByRole("button");
    await expect(types.first()).toHaveAttribute("aria-pressed", "true");
    const first = (await types.first().textContent())!;
    const last = (await types.last().textContent())!;
    expect(last).not.toBe(first);

    const title = page.getByLabel(/^New .+ title$/);
    await title.fill("Long run every Sunday");
    await title.press("Tab");
    await expect(page.getByRole("button", { name: "Add", exact: true })).toBeFocused();
    await expect(page.getByLabel(`New ${first} title`)).toBeVisible();

    // Back past the field to the type buttons, and choose one there.
    await page.keyboard.press("Shift+Tab");
    await expect(title).toBeFocused();
    await page.keyboard.press("Shift+Tab");
    await expect(types.last()).toBeFocused();
    await page.keyboard.press("Space");
    await expect(types.last()).toHaveAttribute("aria-pressed", "true");
    await expect(types.first()).toHaveAttribute("aria-pressed", "false");
    await expect(page.getByLabel(`New ${last} title`)).toHaveValue("Long run every Sunday");
  });

  test("Enter pressed twice in the Map's inline create makes one item", async ({ page }) => {
    await page.goto("/map");
    await expect(page.getByRole("list", { name: "Alignment outline" })).toBeVisible();
    await page.getByRole("button", { name: "Run a half-marathon", exact: true }).click();
    await page.keyboard.press("Enter");
    const title = page.getByLabel(/New (project|task|goal)/i);
    await title.fill("Track tempo runs");
    // The second Enter lands while the first create is still saving (code review CR60).
    await title.press("Enter");
    await title.press("Enter");
    const made = page.getByRole("button", { name: "Track tempo runs", exact: true });
    await expect(made.first()).toBeVisible();
    // A second create would land a moment after the first; give it time to show before counting.
    await page.waitForTimeout(500);
    await expect(made).toHaveCount(1);
  });

  test("the Map hides finished work until Show asks for it", async ({ page }) => {
    await page.goto("/map");
    const outline = page.getByRole("list", { name: "Alignment outline" });
    await expect(outline).toBeVisible();
    const expandAll = page.getByRole("switch", { name: "Expand all" });
    if ((await expandAll.getAttribute("aria-checked")) !== "true") await expandAll.click();
    // The active project shows (under each of its parents); its completed task stays hidden
    // until Show includes done steps.
    await expect(
      outline.getByRole("button", { name: "Production web UI — first pass", exact: true }).first(),
    ).toBeVisible();
    const done = outline.getByRole("button", { name: "Mirror production-ui-types.ts", exact: true });
    await expect(done).toHaveCount(0);
    await expect(page.getByTestId("map-hidden-count")).toHaveText("1 finished hidden");

    await page.getByRole("combobox", { name: "Show" }).click();
    await page.getByRole("option", { name: "Open + done steps" }).click();
    await expect(done.first()).toBeVisible();
    await expect(page.getByTestId("map-hidden-count")).toHaveCount(0);

    // The choice is remembered.
    await page.goto("/map");
    await expect(done.first()).toBeVisible();
  });

  test("edit and save a subject", async ({ page }) => {
    await page.goto("/map?selected=goal-lifeos");
    await expect(page.getByRole("heading", { name: /Ship LifeOS production UI/ })).toBeVisible();
    await page.getByRole("button", { name: "Edit" }).click();
    const description = page.getByLabel("Description");
    await description.fill("Polished across every interface.");
    await page.getByRole("button", { name: "Save" }).click();
    await expect(page.getByRole("button", { name: "Edit" })).toBeVisible();
  });

  test("Discard asks once, and the next item opens as it is (code review CR92)", async ({ page }) => {
    await page.goto("/browse?type=Goal&selected=goal-lifeos");
    await expect(page.getByRole("heading", { name: "Ship LifeOS production UI" })).toBeVisible();
    await page.getByRole("button", { name: "Edit" }).click();
    await page.getByLabel("Title", { exact: true }).fill("Typed for the first item");

    await page.getByRole("list", { name: "Goal list" }).getByText("Run a half-marathon").click();
    const dialog = page.getByRole("alertdialog", { name: "Unsaved edits" });
    await dialog.getByRole("button", { name: "Discard" }).click();

    await expect(dialog).toBeHidden();
    await expect(page.getByRole("heading", { name: "Run a half-marathon" })).toBeVisible();
    await expect(page.getByRole("button", { name: "Save" })).toBeHidden();
  });

  test("Discard throws the edit away when the item stays open (code review CR92)", async ({ page }) => {
    await page.goto("/browse?type=Goal&selected=goal-lifeos");
    await expect(page.getByRole("heading", { name: "Ship LifeOS production UI" })).toBeVisible();
    await page.getByRole("button", { name: "Edit" }).click();
    await page.getByLabel("Title", { exact: true }).fill("Typed and then discarded");

    // All types keeps the open Goal open.
    await page.getByRole("complementary", { name: "Browse filters" }).getByRole("button", { name: /^All types/ }).click();
    const dialog = page.getByRole("alertdialog", { name: "Unsaved edits" });
    await dialog.getByRole("button", { name: "Discard" }).click();

    await expect(dialog).toBeHidden();
    await expect(page.getByRole("heading", { name: "Ship LifeOS production UI" })).toBeVisible();
    await expect(page.getByRole("button", { name: "Save" })).toBeHidden();
    // Nothing is held, so leaving doesn't ask.
    await page.getByRole("link", { name: "Focus" }).click();
    await expect(page).toHaveURL(/\/$/);
    await expect(dialog).toBeHidden();
  });

  test("Escape keeps the edit, and leaving asks again (code review CR92)", async ({ page }) => {
    await page.goto("/browse?type=Goal&selected=goal-lifeos");
    await expect(page.getByRole("heading", { name: "Ship LifeOS production UI" })).toBeVisible();
    await page.getByRole("button", { name: "Edit" }).click();
    const title = page.getByLabel("Title", { exact: true });
    await title.fill("Kept through Escape");
    const dialog = page.getByRole("alertdialog", { name: "Unsaved edits" });

    await page.getByRole("link", { name: "Focus" }).click();
    await expect(dialog).toBeVisible();
    await page.keyboard.press("Escape");

    await expect(dialog).toBeHidden();
    await expect(page).toHaveURL(/\/browse/);
    await expect(title).toHaveValue("Kept through Escape");
    await page.getByRole("link", { name: "Focus" }).click();
    await expect(dialog).toBeVisible();
    await dialog.getByRole("button", { name: "Discard" }).click();
    await expect(page).toHaveURL(/\/$/);
    await expect(dialog).toBeHidden();
  });

  test("a page whose file is gone after an update says so, inside the shell (code review CR93)", async ({ page }) => {
    // As after a deploy: the page's file, fetched the first time it's opened, is no longer there.
    await page.route(/\/assets\/browse-page-[\w-]+\.js$/, (route) => route.abort());
    await page.goto("/");
    await page.getByRole("link", { name: "Browse" }).click();

    // It reloads once to get the new build; when that doesn't help, it says so and offers Reload.
    // The reload comes first, so this waits longer than a check's usual wait.
    const failed = page.getByRole("alert").filter({ hasText: "LifeOS was updated" });
    const afterTheReload = { timeout: 15_000 };
    await expect(failed).toBeVisible(afterTheReload);
    await expect(failed.getByRole("button", { name: "Reload" })).toBeVisible();

    // The shell stays, so another page still opens.
    await page.getByRole("link", { name: "Inbox" }).click();
    await expect(page.getByRole("heading", { name: "Inbox" })).toBeVisible();
    await expect(failed).toBeHidden();
  });

  test("Inbox keyboard triage: arrows, Enter, Drop confirm", async ({ page }) => {
    await page.goto("/inbox");
    await expect(page.getByRole("heading", { name: "Inbox" })).toBeVisible();
    await page.keyboard.press("ArrowDown");
    await page.getByRole("button", { name: "Drop" }).focus();
    await page.keyboard.press("Enter");
    await expect(page.getByRole("alertdialog")).toBeVisible();
    await page.getByRole("button", { name: "Drop" }).last().click();
    await expect(page.getByRole("alertdialog")).toBeHidden();
  });

  test("graph node click opens detail peek", async ({ page }) => {
    await page.goto("/map?view=graph");
    const node = page.getByText("Craftsman of systems", { exact: false }).first();
    await expect(node).toBeVisible(GRAPH_DRAWN);
    await node.click();
    await expect(page.getByRole("heading", { name: /Craftsman of systems/ })).toBeVisible();
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
    await expect(page.getByText("Captured to Inbox")).toBeVisible();
    // On a desktop the Inbox opens its newest item, so the capture can show in the detail pane
    // too: its row in the list is the first match.
    await expect(page.getByText("Standing desk trial this week.").first()).toBeVisible();
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

  test("Focus's half-width lists show each title whole, its details on the line under it", async ({ page }) => {
    // The narrowest desktop: each section is half the page, about 300 px.
    await page.setViewportSize({ width: 1024, height: 800 });
    await page.goto("/");
    for (const [heading, name, detail] of [
      ["Due & overdue", "Buy race shoes", /overdue/],
      ["Active goals", "Run a half-marathon", /^Active$/],
      ["Active projects", "12-week base build", /^Active$/],
    ] as const) {
      const section = page.locator("section").filter({ has: page.getByRole("heading", { name: heading }) });
      const row = section.getByRole("button", { name: new RegExp(name) });
      const title = row.getByText(name, { exact: true });
      const chip = row.getByText(detail);
      await expect(chip).toBeVisible();
      expect(await title.evaluate((el) => el.scrollWidth <= el.clientWidth), `${name} is cut off`).toBe(true);
      const titleBox = (await title.boundingBox())!;
      const chipBox = (await chip.boundingBox())!;
      expect(chipBox.y, `${name}'s details sit beside it`).toBeGreaterThanOrEqual(titleBox.y + titleBox.height - 1);
    }
  });
});
