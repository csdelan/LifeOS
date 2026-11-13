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
  test("allow an agent, read its calls, and revoke it", async ({ page }) => {
    await page.goto("/people");
    const helper = page.getByRole("article", { name: "Research helper" });
    await expect(helper.getByText("Not allowed")).toBeVisible();
    await helper.getByRole("button", { name: "Allow" }).click();
    await page.getByRole("alertdialog").getByRole("button", { name: "Allow" }).click();
    await expect(helper.getByText("Connected")).toBeVisible();

    const claude = page.getByRole("article", { name: "Claude" });
    await claude.getByRole("button", { name: "Recent calls" }).click();
    await expect(claude.getByRole("list", { name: "Recent calls, newest first" }).getByText("life_snapshot")).toBeVisible();
    await claude.getByRole("button", { name: "Revoke" }).click();
    await page.getByRole("alertdialog").getByRole("button", { name: "Revoke" }).click();
    await expect(claude.getByText("Revoked", { exact: true })).toBeVisible();
  });

  test("an expired consent request says to start again", async ({ page }) => {
    await page.goto("/oauth/consent?authorization_id=mock-expired");
    await expect(page.getByRole("heading", { name: "This request can't be found" })).toBeVisible();
    await page.goto("/oauth/consent");
    await expect(page.getByRole("heading", { name: "This link has no request in it" })).toBeVisible();
  });
});
