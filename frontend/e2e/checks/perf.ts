import type { Page } from '@playwright/test';
import { fail, pass, type Result, type XdWindow } from './types';

export const budget = { readyMs: 2000, cls: 0.1 };

/** Slows the page like the spec says: 4x CPU, Fast 4G (CDP, Chromium only). */
export async function throttle(page: Page): Promise<void> {
  const cdp = await page.context().newCDPSession(page);
  await cdp.send('Emulation.setCPUThrottlingRate', { rate: 4 });
  await cdp.send('Network.enable');
  await cdp.send('Network.emulateNetworkConditions', {
    offline: false,
    latency: 150,
    downloadThroughput: (1.6 * 1024 * 1024) / 8,
    uploadThroughput: (750 * 1024) / 8,
  });
}

/** C14: time from navigation to the `data-ready` marker and the layout shift; fails only above twice the budget. */
export async function c14Performance(page: Page): Promise<Result> {
  await page.waitForSelector('main [data-ready]', { state: 'attached', timeout: 60_000 });
  await page.waitForLoadState('networkidle');
  const ready = await page.evaluate(() => {
    const entry = performance.getEntriesByType('mark').find((m) => m.name === 'xd-ready');
    return entry?.startTime ?? performance.now();
  });
  const cls = await page.evaluate(() => (window as unknown as XdWindow).__cls);
  const figures = `interactive-ready ${Math.round(ready)} ms (budget ${budget.readyMs}), CLS ${cls.toFixed(3)} (budget ${budget.cls}), 4x CPU + Fast 4G, lab trend only`;
  const hard = ready > 2 * budget.readyMs || cls > 2 * budget.cls;
  return hard ? fail('C14', `above twice the budget: ${figures}`) : pass('C14', figures);
}
