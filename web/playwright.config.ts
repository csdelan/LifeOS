import { defineConfig, devices } from "@playwright/test";

// The test runner counts in Chris's zone too, not only the browser (timezoneId below): a test
// pins the clock with a local time such as new Date("2026-11-07T08:30:00"), which Node reads in
// its own zone. In CI that's UTC, eight hours off the page. Set before the workers start, so they
// inherit it.
process.env.TZ = "America/Los_Angeles";

export default defineConfig({
  testDir: "./e2e",
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? "github" : "list",
  use: {
    baseURL: "http://127.0.0.1:5177",
    trace: "on-first-retry",
    // Chris's home zone and locale, on every machine: CI runs in UTC otherwise.
    timezoneId: "America/Los_Angeles",
    locale: "en-US",
  },
  webServer: {
    command: "npx vite --host 127.0.0.1 --port 5177 --strictPort",
    url: "http://127.0.0.1:5177",
    reuseExistingServer: false,
    timeout: 120_000,
    env: {
      VITE_API_BASE_URL: "",
    },
  },
  projects: [
    { name: "chromium", use: { ...devices["Desktop Chrome"] } },
  ],
});
