import { expect, test } from "@playwright/test";

test.describe("golden paths: Browse and Decisions", () => {
  test("Browse: pick Decisions, filter by status, find one from the search box, and make a new one", async ({
    page,
  }) => {
    await page.goto("/browse");
    const filters = page.getByRole("complementary", { name: "Browse filters" });
    await filters.getByRole("button", { name: /^Decision\s*2$/ }).click();
    const list = page.getByRole("list", { name: "Decision list" });
    await expect(list.getByRole("button")).toHaveCount(2);

    await filters.getByRole("checkbox", { name: "Implementing" }).click();
    await expect(list.getByRole("button")).toHaveCount(1);
    await list.getByRole("button", { name: /React \+ Vite for Production UI/ }).click();
    await expect(page.getByRole("heading", { name: "React + Vite for Production UI" })).toBeVisible();

    // Coming back keeps the type, the open item and the filters.
    await page.goto("/");
    await page.getByRole("navigation", { name: "Primary" }).getByRole("link", { name: "Browse" }).click();
    await expect(filters.getByRole("checkbox", { name: "Implementing" })).toBeChecked();
    await expect(page.getByRole("heading", { name: "React + Vite for Production UI" })).toBeVisible();

    // The search box finds the other Decision, though it's past the first twenty items.
    await page.keyboard.press("Control+k");
    await page.getByRole("combobox").fill("blazor");
    await page.getByRole("option", { name: /Blazor for Production UI/ }).click();
    await expect(page).toHaveURL(/\/browse\?.*selected=dec-blazor/);
    await expect(page.getByRole("heading", { name: "Blazor for Production UI" })).toBeVisible();
    await expect(page.getByText("“Blazor for Production UI” is open, but the filters hide it in the list.")).toBeVisible();
    await page.getByRole("button", { name: "Show it" }).click();
    await expect(list.getByRole("button", { name: /Blazor for Production UI/ })).toHaveAttribute("aria-current", "true");

    // A new Decision made from here is selected in the list.
    await page.getByRole("button", { name: "New Decision" }).click();
    const dialog = page.getByRole("dialog", { name: "New Decision" });
    await dialog.getByRole("textbox", { name: "Title" }).fill("Use SQLite for local storage");
    await dialog.getByRole("button", { name: "Save" }).click();
    await expect(dialog).toBeHidden({ timeout: 10_000 });
    await expect(list.getByRole("button", { name: /Use SQLite for local storage/ })).toHaveAttribute(
      "aria-current",
      "true",
    );
    await expect(page.getByRole("heading", { name: "Use SQLite for local storage" })).toBeVisible();
  });

  test("Decisions: record one from a Problem, create its resulting work, add context, and replace it", async ({
    page,
  }) => {
    // Recorded from a Problem, a Decision shows the Problem as its Context before Save.
    await page.goto("/browse?type=Problem&selected=prob-sleep");
    await expect(page.getByRole("heading", { name: "How do I sleep through the night?" })).toBeVisible({
      timeout: 10_000,
    });
    await page.getByRole("button", { name: "Record decision" }).click();
    const newDecision = page.getByRole("dialog", { name: "New Decision" });
    await expect(newDecision.getByRole("group", { name: "Context" })).toContainText("How do I sleep through the night?");
    await newDecision.getByRole("textbox", { name: "Title" }).fill("No caffeine after noon");
    await newDecision.getByRole("button", { name: "Save" }).click();
    await expect(newDecision).toBeHidden({ timeout: 10_000 });

    const filters = page.getByRole("complementary", { name: "Browse filters" });
    await filters.getByRole("button", { name: /^Decision\s*3$/ }).click();
    const list = page.getByRole("list", { name: "Decision list" });
    await list.getByRole("button", { name: /No caffeine after noon/ }).click();
    await expect(page.getByRole("heading", { name: "No caffeine after noon" })).toBeVisible();
    await page.getByRole("tab", { name: "Relationships" }).click();
    const context = page.getByRole("region", { name: "Context" });
    await expect(context).toContainText("How do I sleep through the night?");

    // Resulting work: pick the type, then the normal Task form with the link shown before Save.
    await page.getByRole("button", { name: "Resulting work" }).click();
    await page.getByRole("menuitem", { name: "Task" }).click();
    const newTask = page.getByRole("dialog", { name: "New Task" });
    await expect(newTask.getByRole("group", { name: "Resulting work of" })).toContainText("No caffeine after noon");
    await newTask.getByRole("textbox", { name: "Title" }).fill("Switch to decaf after lunch");
    await newTask.getByRole("button", { name: "Save" }).click();
    await expect(newTask).toBeHidden({ timeout: 10_000 });
    const work = page.getByRole("region", { name: "Resulting work" });
    await expect(work.getByRole("button", { name: "Switch to decaf after lunch" })).toBeVisible();
    // Making its work doesn't move the Decision: it's still Open.
    await expect(list.getByRole("button", { name: /No caffeine after noon/ })).toContainText("Open");

    // An existing Idea added to Context later.
    await page.getByRole("combobox", { name: "Target type" }).click();
    await page.getByRole("option", { name: "Idea", exact: true }).click();
    await page.getByRole("combobox", { name: "Target subject" }).click();
    await page.getByRole("option", { name: "Wind-down without screens" }).click();
    await page.getByRole("button", { name: "Link", exact: true }).click();
    await expect(context).toContainText("Wind-down without screens");

    // Reversed: a replacement Decision supersedes it, and it becomes Cancelled with its title kept.
    await page.getByRole("button", { name: "Replacement" }).click();
    await expect(newDecision.getByRole("group", { name: "Replaces" })).toContainText("No caffeine after noon");
    await expect(newDecision.getByRole("group", { name: "Replaces" })).toContainText("Saving marks it Cancelled");
    await newDecision.getByRole("textbox", { name: "Title" }).fill("No caffeine after 10am");
    await newDecision.getByRole("button", { name: "Save" }).click();
    await expect(newDecision).toBeHidden({ timeout: 10_000 });
    await expect(page.getByRole("heading", { name: "No caffeine after noon" })).toBeVisible();
    await expect(list.getByRole("button", { name: /No caffeine after noon/ })).toContainText("Cancelled");
    const replacedBy = page.getByRole("region", { name: "Replaced by" });
    await replacedBy.getByRole("button", { name: "No caffeine after 10am" }).click();
    await expect(page.getByRole("heading", { name: "No caffeine after 10am" })).toBeVisible();
    await page.getByRole("tab", { name: "Relationships" }).click();
    await expect(page.getByRole("region", { name: "Replaces" })).toContainText("No caffeine after noon");
  });
});
