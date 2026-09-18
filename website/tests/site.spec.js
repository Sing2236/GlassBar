const { test, expect } = require("@playwright/test");

test("keeps the minimal GlassBar download home", async ({ page }) => {
  await page.goto("/");
  await expect(page.getByLabel("Example GlassBar")).toBeVisible();
  await expect(page.getByRole("link")).toHaveCount(3);
  await expect(page.getByRole("link", { name: "Download" })).toHaveAttribute("href", "https://github.com/Sing2236/GlassBar/releases/latest/download/GlassBarSetup.exe");
  await expect(page.getByRole("link", { name: "Community" })).toHaveAttribute("href", "/studio");
  await expect(page.getByRole("link", { name: "Support" })).toHaveAttribute("href", /business=Ethanhuynh365%40gmail\.com/);
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(page.viewportSize().width);
});

test("organizes the community into GlassBars, Widgets, and Animations", async ({ page }) => {
  await page.goto("/studio");
  await expect(page.getByRole("heading", { name: "Make the bar yours." })).toBeVisible();
  await expect(page.getByRole("heading", { name: "GlassBars" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Widgets" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Animations" })).toBeVisible();
  await expect(page.getByRole("article")).toHaveCount(6);
  await page.getByLabel("Search designs").fill("embers");
  await expect(page.getByRole("article")).toHaveCount(1);
});

test("edits each package type and exports without authentication", async ({ page }) => {
  await page.goto("/studio/editor");
  await expect(page.getByRole("heading", { name: "Shape the full bar" })).toBeVisible();
  const opacity = page.getByRole("slider", { name: "Opacity" });
  await opacity.fill("64");
  await expect(page.getByText("64%", { exact: true })).toBeVisible();
  await page.getByRole("tab", { name: "Widget" }).click();
  await expect(page.getByRole("heading", { name: "Build one focused widget" })).toBeVisible();
  await page.getByRole("tab", { name: "Animation" }).click();
  await expect(page.getByRole("heading", { name: "Tune an ambient animation" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Export package" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Sign up to publish" })).toBeVisible();
  await expect(page.getByText(/held for manual approval/)).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(page.viewportSize().width);
});
