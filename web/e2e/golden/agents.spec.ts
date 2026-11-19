import { expect, test } from "@playwright/test";

test.describe("golden paths: connected agents", () => {
  // An agent's sign-in request (agent-access plan §4.4), with the mock's pretend request from
  // Claude. Claude's callback is answered here, so the paths never leave the test.
  test("approve an agent's consent request", async ({ page }) => {
    await page.route("https://claude.ai/**", (route) => route.fulfill({ body: "back at the agent" }));
    await page.goto("/oauth/consent?authorization_id=mock-1");
    await expect(page.getByRole("heading", { name: "Connect Claude to LifeOS?" })).toBeVisible();
    await expect(page.getByRole("navigation")).toHaveCount(0);
    // The mock's Claude is connecting again, so the page offers to replace its earlier client.
    await expect(page.getByRole("checkbox", { name: "Replace the earlier connection" })).toBeChecked();
    await page.getByRole("button", { name: "Approve" }).click();
    await expect(page).toHaveURL(/^https:\/\/claude\.ai\/api\/mcp\/auth_callback\?code=mock-code/);
  });

  test("deny an agent's consent request", async ({ page }) => {
    await page.route("https://claude.ai/**", (route) => route.fulfill({ body: "back at the agent" }));
    await page.goto("/oauth/consent?authorization_id=mock-1");
    await page.getByRole("button", { name: "Deny" }).click();
    await expect(page).toHaveURL(/auth_callback\?error=access_denied/);
  });

  // Connected agents (agent-access plan §4.5), with the mock's Claude (allowed) and a helper
  // Supabase lists but LifeOS doesn't allow (fix 3).
  test("allow an agent, read its calls, and revoke it, which then waits under Show revoked", async ({ page }) => {
    await page.goto("/people");
    const helper = page.getByRole("article", { name: "Research helper" });
    await expect(helper.getByText("Not allowed")).toBeVisible();
    await helper.getByRole("button", { name: "Allow" }).click();
    await page.getByRole("alertdialog").getByRole("button", { name: "Allow" }).click();
    await expect(helper.getByText("Connected", { exact: true })).toBeVisible();

    const claude = page.getByRole("article", { name: "Claude" });
    await claude.getByRole("button", { name: "Recent calls" }).click();
    await expect(claude.getByRole("list", { name: "Recent calls, newest first" }).getByText("life_snapshot")).toBeVisible();
    await claude.getByRole("button", { name: "Revoke" }).click();
    await page.getByRole("alertdialog").getByRole("button", { name: "Revoke" }).click();
    // Revoked agents are hidden until asked for.
    await expect(page.getByRole("status")).toHaveText("Revoked Claude. Show revoked lists it.");
    await expect(claude).toHaveCount(0);
    await page.getByRole("switch", { name: "Show revoked (1)" }).click();
    await expect(claude.getByText("Revoked", { exact: true })).toBeVisible();
    await page.getByRole("switch", { name: "Show revoked (1)" }).click();
    await expect(claude).toHaveCount(0);
  });

  // "via Claude" (agent-access plan §5.6 step 2.4): what an agent wrote says which, by the Person it
  // acts as. The mock's Compass started the Map graph Task, moved its due date, set its priority and
  // wrote in its journal, and flagged a camping nudge to the Inbox; Chris renamed the Task himself.
  test("what an agent wrote says via its Person: an item's history and journal, and the Inbox", async ({ page }) => {
    await page.goto("/map?selected=task-graph");
    await page.getByRole("tab", { name: "History" }).click();
    const changes = page.getByRole("tabpanel", { name: "History" }).getByRole("listitem");
    await expect(changes.first()).toContainText("Priority set to High");
    await expect(changes.first()).toContainText("via Compass");
    await expect(changes.filter({ hasText: "Map graph view → Ship the Map graph view" })).not.toContainText("via");

    await page.getByRole("tab", { name: "Journal" }).click();
    const entry = page.getByRole("tabpanel", { name: "Journal" }).getByRole("listitem").filter({ hasText: "layered layout first" });
    await expect(entry).toContainText("via Compass");

    await page.goto("/inbox");
    await expect(page.getByRole("button", { name: /Camping logistics has no next action/ }).first()).toContainText("via Compass");
  });

  test("an expired consent request says to start again", async ({ page }) => {
    await page.goto("/oauth/consent?authorization_id=mock-expired");
    await expect(page.getByRole("heading", { name: "This request can't be found" })).toBeVisible();
    await page.goto("/oauth/consent");
    await expect(page.getByRole("heading", { name: "This link has no request in it" })).toBeVisible();
  });
});
