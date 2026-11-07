import { defineConfig, devices } from "@playwright/test";
if (!process.env.SHIFTFLOW_E2E_RUN || !process.env.SHIFTFLOW_E2E_PRIVATE || !process.env.SHIFTFLOW_E2E_FRONTEND_URL)
  throw new Error("Use npm run test:e2e; direct execution against an existing environment is prohibited.");
export default defineConfig({
  testDir: "./e2e/specs", workers: 1, fullyParallel: false, retries: 0,
  forbidOnly: !!process.env.CI, timeout: 90_000, globalTimeout: 15 * 60_000,
  expect: { timeout: 12_000 },
  outputDir: `${process.env.SHIFTFLOW_E2E_PRIVATE}/results`,
  reporter: [["json", { outputFile: `${process.env.SHIFTFLOW_E2E_PRIVATE}/report.json` }], ["./e2e/progress-reporter.ts"]],
  use: {
    ...devices["Desktop Chrome"], baseURL: process.env.SHIFTFLOW_E2E_FRONTEND_URL,
    locale: "he-IL", timezoneId: "Asia/Jerusalem",
    trace: { mode: "retain-on-failure", sources: false }, screenshot: "only-on-failure",
    video: "off", actionTimeout: 15_000, navigationTimeout: 30_000,
  },
  projects: [{ name: "chromium" }],
});
