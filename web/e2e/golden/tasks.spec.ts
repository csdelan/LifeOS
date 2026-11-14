import { expect, test } from "@playwright/test";

test.describe("golden paths: repeating tasks and timers", () => {
  test("a repeat pattern is saved in the kernel's shape and read back", async ({ page }) => {
    await page.goto("/map?selected=cmt-inbox");
    await expect(page.getByRole("heading", { name: /Reach Inbox Zero each weekday/ })).toBeVisible();
    await expect(page.getByText("Weekdays", { exact: true })).toBeVisible();
    await page.getByRole("button", { name: "Edit" }).click();
    const days = page.getByRole("group", { name: "On these days" });
    for (const day of ["Monday", "Tuesday", "Thursday", "Friday"]) {
      await days.getByRole("button", { name: day }).click();
    }
    await expect(days.getByRole("button", { name: "Wednesday" })).toHaveAttribute("aria-pressed", "true");
    await page.getByRole("button", { name: "Save" }).click();
    await expect(page.getByRole("button", { name: "Edit" })).toBeVisible();
    await expect(page.getByRole("tabpanel", { name: "Overview" }).getByText("Every Wednesday", { exact: true })).toBeVisible();

    await page.getByRole("button", { name: "Edit" }).click();
    await page.getByRole("group", { name: "Repeat" }).getByRole("button", { name: "Doesn't repeat" }).click();
    await page.getByRole("button", { name: "Save" }).click();
    await expect(page.getByRole("button", { name: "Edit" })).toBeVisible();
    await expect(page.getByRole("tabpanel", { name: "Overview" }).getByText("Every Wednesday", { exact: true })).toBeHidden();
  });

  test("a repeating task: make one, finish today's, the next appears, and mark a late one missed", async ({
    page,
  }) => {
    // Wednesday 21 October 2026: a weekly Wednesday repeat from the 14th has a late one and today's.
    await page.clock.install({ time: new Date("2026-10-21T12:00:00") });
    await page.addInitScript(() => localStorage.removeItem("lifeos.view.tasks"));
    await page.goto("/tasks");
    await page.getByRole("textbox", { name: "New task title" }).fill("Water plants");
    await page.getByRole("button", { name: "More…" }).click();
    const dialog = page.getByRole("dialog");
    await expect(dialog).toBeVisible();
    await dialog.getByRole("group", { name: "Repeat" }).getByRole("button", { name: "Weekly" }).click();
    await expect(
      dialog.getByRole("group", { name: "On these days" }).getByRole("button", { name: "Wednesday" }),
    ).toHaveAttribute("aria-pressed", "true");
    // A repeating Task's dates come from its repetitions, so its own due date is hidden.
    await expect(dialog.getByLabel("Due", { exact: true })).toHaveCount(0);
    await dialog.getByLabel("Repeats from").fill("2026-10-14");
    await dialog.getByRole("button", { name: "Save" }).click();
    await expect(dialog).toBeHidden();

    const group = (name: string) => page.locator("section", { has: page.getByRole("heading", { name }) });
    const overdue = group(/^Overdue/).getByRole("button", { name: /Water plants/ });
    const today = group(/^Today/).getByRole("button", { name: /Water plants/ });
    await expect(overdue).toContainText("Every Wednesday");
    await expect(overdue).toContainText("7 days overdue");
    await expect(today).toContainText("Every Wednesday");

    // Finish today's: it pays, and next Wednesday's is made.
    await today.click();
    const detail = page.locator("aside").filter({ has: page.getByRole("button", { name: "Close panel" }) });
    const repeats = detail.getByRole("region", { name: "Repeats" });
    await expect(repeats).toContainText("Every Wednesday");
    await expect(repeats.getByRole("list", { name: "Repetitions" }).getByRole("listitem")).toHaveCount(2);
    await detail.getByRole("button", { name: "Edit" }).click();
    await detail.getByRole("combobox", { name: "Status" }).click();
    await page.getByRole("option", { name: "Completed", exact: true }).click();
    await detail.getByRole("button", { name: "Save" }).click();
    await expect(detail.getByRole("button", { name: "Edit" })).toBeVisible();
    await expect(group(/^Upcoming/).getByRole("button", { name: /Water plants/ })).toContainText("Oct 28");

    // Close the late one myself: missed, not paid, and gone from the working view.
    await overdue.click();
    await expect(detail.getByRole("region", { name: "Timer" })).toBeVisible();
    await expect(repeats.getByRole("listitem").filter({ hasText: "This one" })).toContainText("due Oct 14");
    await repeats.getByRole("button", { name: "Mark missed" }).click();
    await expect(page.getByText("Marked missed")).toBeVisible();
    await expect(group(/^Overdue/).getByRole("button", { name: /Water plants/ })).toHaveCount(0);

    // The Repeating view lists the task itself, with how it's gone.
    await page.getByRole("combobox", { name: "Status" }).first().click();
    await page.getByRole("option", { name: "Repeating", exact: true }).click();
    const series = group(/^Repeating/).getByRole("button", { name: /Water plants/ });
    await expect(series).toContainText("1 of 2 done");
    await expect(series).toContainText("Oct 28");
    await series.click();
    await expect(repeats.getByRole("button", { name: "Stop repeating" })).toBeVisible();
    // The task itself isn't timed; its repetitions are.
    await expect(detail.getByRole("region", { name: "Timer" })).toHaveCount(0);
  });

  test("time a task, stop for now and start again, then Complete only after it asks", async ({ page }) => {
    await page.goto("/browse?type=Task&selected=task-shoes");
    await expect(page.getByRole("heading", { name: "Buy race shoes" })).toBeVisible();
    const timer = page.getByRole("region", { name: "Timer", exact: true });
    await timer.getByRole("button", { name: "Start" }).click();
    await expect(timer.getByText("Running", { exact: true })).toBeVisible();

    // Stop ends the session and leaves the task open.
    await timer.getByRole("button", { name: "Stop" }).click();
    await timer.getByRole("button", { name: "Start" }).click();
    await expect(timer.getByText("Running", { exact: true })).toBeVisible();

    // Complete closes it, so it asks first; Cancel changes nothing.
    await timer.getByRole("button", { name: "Complete" }).click();
    const ask = page.getByRole("alertdialog", { name: "Complete Buy race shoes?" });
    await expect(ask).toContainText("use Stop instead");
    await ask.getByRole("button", { name: "Cancel" }).click();
    await expect(timer.getByText("Running", { exact: true })).toBeVisible();
    await timer.getByRole("button", { name: "Complete" }).click();
    await ask.getByRole("button", { name: "Complete" }).click();

    // A closed task isn't timed until it's reopened.
    await expect(timer.getByText("Reopen the task to time it.")).toBeVisible();
    await expect(timer.getByRole("button", { name: "Start" })).toHaveCount(0);
  });

  test("a repeating task is one node on the Map, and a review counts its weeks", async ({ page }) => {
    await page.clock.install({ time: new Date("2026-10-21T12:00:00") });
    await page.goto("/map");
    const outline = page.getByRole("list", { name: "Alignment outline" });
    await expect(outline).toBeVisible();
    const expandAll = page.getByRole("switch", { name: "Expand all" });
    if ((await expandAll.getAttribute("aria-checked")) !== "true") await expandAll.click();
    // Saturday's shop serves the Sunday dinner project: once, not once a week.
    const title = outline.getByRole("button", { name: "Shop for Sunday dinner", exact: true });
    await expect(title).toHaveCount(1);
    const shop = page.locator('[data-map-node="task-dinner-shop"]');
    await expect(shop).toContainText("Every Saturday");
    await expect(shop).toContainText("Repeating");
    await title.click();
    await expect(page.getByRole("region", { name: "Repeats" }).getByRole("button", { name: "Stop repeating" })).toBeVisible();

    // The week before: the trash and the shop each done once.
    await page.goto("/reviews?kind=weekly");
    await page.getByRole("button", { name: "Previous" }).click();
    const repeating = page.locator("div", { has: page.getByRole("heading", { name: "Repeating tasks" }) }).last();
    await expect(repeating).toContainText("Take out trash — 1 of 1 done");
    await expect(repeating).toContainText("Shop for Sunday dinner — 1 of 1 done");
  });
});
