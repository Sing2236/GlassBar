const { defineConfig, devices } = require("@playwright/test");
const liveBaseUrl = process.env.PLAYWRIGHT_BASE_URL;

module.exports = defineConfig({
  testDir: "./tests",
  outputDir: "./test-results",
  reporter: "line",
  use: {
    baseURL: liveBaseUrl || "http://127.0.0.1:4173",
    trace: "retain-on-failure"
  },
  webServer: liveBaseUrl ? undefined : {
    command: "npm run build && npm run preview -- --host 127.0.0.1 --port 4173",
    url: "http://127.0.0.1:4173",
    reuseExistingServer: true
  },
  projects: [
    { name: "desktop", use: { ...devices["Desktop Chrome"] } },
    { name: "mobile", use: { ...devices["Pixel 7"] } }
  ]
});
