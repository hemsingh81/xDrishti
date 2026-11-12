import type { Page } from '@playwright/test';
import { scrollThrough } from '../scroll';
import { fail, pass, type Result, type XdWindow } from './types';

export const budget = { readyMs: 2000, cls: 0.1 };

/** Slows the page: 4x CPU only (CDP, Chromium only). No network profile: the app is served on 127.0.0.1 (REVIEW-2026-11-10 Q14). */
export async function throttle(page: Page): Promise<void> {
  const cdp = await page.context().newCDPSession(page);
  await cdp.send('Emulation.setCPUThrottlingRate', { rate: 4 });
}

/** C14: time from navigation to the `data-ready` marker and the layout shift; fails only above twice the budget. */
export async function c14Performance(page: Page, scrollToEnd = false): Promise<Result> {
  await page.waitForSelector('main [data-ready]', { state: 'attached', timeout: 60_000 });
  await page.waitForLoadState('networkidle');
  const ready = await page.evaluate(() => {
    const entry = performance.getEntriesByType('mark').find((m) => m.name === 'xd-ready');
    return entry?.startTime ?? performance.now();
  });
  // The deferred panels mount on the scroll; their shift counts, their load time does not (the figure is the first view).
  if (scrollToEnd) await scrollThrough(page);
  const { cls, shifts } = await page.evaluate(() => {
    const w = window as unknown as XdWindow;
    return { cls: w.__cls, shifts: w.__shifts.slice(0, 4) };
  });
  const figures = `interactive-ready ${Math.round(ready)} ms (budget ${budget.readyMs}), CLS ${cls.toFixed(3)} (budget ${budget.cls}), 4x CPU, no network profile, lab trend only${cls > budget.cls ? `; shifts: ${shifts.join(' | ')}` : ''}`;
  const hard = ready > 2 * budget.readyMs || cls > 2 * budget.cls;
  return hard ? fail('C14', `above twice the budget: ${figures}`) : pass('C14', figures);
}
