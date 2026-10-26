import type { Page } from '@playwright/test';

/**
 * Scrolls the page to its end and back (P8-17f): panels below the first view mount only when they come near the viewport, so
 * the checks and the screenshots would never see them. `main` scrolls from `md` up, the window below it; both are moved. The
 * layout is read at the top again (C2 and the keyboard walk need the first view). The readiness figure of C14 is the first view:
 * `data-ready` is set before the deferred panels mount.
 */
export async function scrollThrough(page: Page): Promise<void> {
  // Step by step, as a reader scrolls: a place far above the end is near the viewport on the way down.
  const step = (top: number | null) =>
    page.evaluate((to) => {
      const main = document.querySelector('main');
      const scroller = main && main.scrollHeight > main.clientHeight ? main : document.scrollingElement;
      if (!scroller) return true;
      scroller.scrollTop = to ?? scroller.scrollTop + window.innerHeight * 0.7;
      return scroller.scrollTop + scroller.clientHeight >= scroller.scrollHeight - 2;
    }, top);
  for (let i = 0; i < 40 && !(await step(null)); i += 1) await page.waitForTimeout(150);
  // The observer reports on the next frames; the request starts after that, so idle is awaited after a short pause.
  await page.waitForTimeout(400);
  await page.waitForLoadState('networkidle');
  await step(0);
}
