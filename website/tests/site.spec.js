const { test, expect } = require("@playwright/test");

test("shows the centered GlassBar with working links", async ({ page }, testInfo) => {
  await page.goto("/");
  const rainDrops = page.locator(".rain i");
  await expect(rainDrops).toHaveCount(10);
  await expect(rainDrops.first()).toHaveCSS("animation-name", "rain-fall");
  await page.emulateMedia({ reducedMotion: "reduce" });

  await expect(page.locator("body")).toHaveCSS("background-color", "rgb(255, 255, 255)");
  await expect(page.getByLabel("Example GlassBar")).toBeVisible();
  await expect(page.getByRole("link")).toHaveCount(2);

  const download = page.getByRole("link", { name: "Download" });
  const support = page.getByRole("link", { name: "Support" });

  await expect(download).toHaveAttribute(
    "href",
    "https://github.com/Sing2236/GlassBar/releases/latest/download/GlassBarSetup.exe"
  );
  await expect(support).toHaveAttribute("href", /business=Ethanhuynh365%40gmail\.com/);

  const barBox = await page.getByLabel("Example GlassBar").boundingBox();
  const viewport = page.viewportSize();
  expect(barBox).not.toBeNull();
  expect(viewport).not.toBeNull();
  expect(Math.abs(barBox.x + barBox.width / 2 - viewport.width / 2)).toBeLessThan(2);
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(viewport.width);

  await page.screenshot({
    path: testInfo.outputPath("glassbar-site.png"),
    fullPage: true
  });
});
