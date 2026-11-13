import { expect, type Page } from "@playwright/test";

// What more than one spec uses. The specs are split by feature (code review CR56) so two workers
// can run them side by side: each test opens its own page, and the mock store lives in the page.

/**
 * The mock pays activity XP from Oura from 2026-10-22 by the page's date (activity plan A9), and
 * the Food Log and Character pages ask for that sync on opening, so Health's numbers would move
 * with the real date. The paths that read them run on the day before activity XP started.
 */
export async function beforeActivityXp(page: Page) {
  await page.clock.install({ time: new Date("2026-10-21T12:00:00") });
}

/** A 2400 x 1600 PNG, as a phone might take: the browser shrinks it to 1,200 px. */
export async function bigPng(page: Page): Promise<Buffer> {
  return Buffer.from(
    await page.evaluate(() => {
      const canvas = document.createElement("canvas");
      canvas.width = 2400;
      canvas.height = 1600;
      const g = canvas.getContext("2d")!;
      g.fillStyle = "#c2410c";
      g.fillRect(0, 0, 2400, 1600);
      g.fillStyle = "#fde68a";
      g.beginPath();
      g.arc(1200, 800, 500, 0, Math.PI * 2);
      g.fill();
      return canvas.toDataURL("image/png").split(",")[1];
    }),
    "base64",
  );
}

// The page starts dark and next-themes switches it to light after it mounts. Under reduced
// motion every element still takes a 0.01ms transition, and the colour reaches each level of
// the page a frame after its parent, so for a moment deep text sits halfway between the
// themes. Wait until no transition is running before a scan, or axe reads that moment.
export async function settled(page: Page) {
  await expect
    .poll(() =>
      page.evaluate(
        () => document.getAnimations().filter((a) => a.effect?.getComputedTiming().endTime !== Infinity).length,
      ),
    )
    .toBe(0);
}
