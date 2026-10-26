import { test } from '@playwright/test';
import { c14Performance, throttle } from './checks/perf';
import { installProbes } from './checks/dom';
import type { Ctx } from './checks/types';
import { installApi } from './api';
import { screens, states, widths } from './matrix';
import { record } from './record';
import { runCell } from './run';

// One test per screen x width x scheme x state. Dark runs at 360 and 1440 for the full state only (Decisions 7).
for (const screen of screens) {
  test.describe(screen.id, () => {
    for (const width of widths) {
      for (const state of states) {
        const ctx: Ctx = { screen: screen.id, width, scheme: 'light', state };
        test(`${width} light ${state}`, async ({ page }) => {
          await runCell(page, screen, ctx);
        });
      }
      if (width === 360 || width === 1440) {
        const ctx: Ctx = { screen: screen.id, width, scheme: 'dark', state: 'full' };
        test(`${width} dark full`, async ({ page }) => {
          await runCell(page, screen, ctx);
        });
      }
    }
    for (const width of [360, 1440]) {
      test(`${width} perf @perf`, async ({ page }) => {
        const ctx: Ctx = { screen: screen.id, width, scheme: 'light', state: 'full' };
        const mount = screen.mounts.full;
        if (!mount) return;
        await page.setViewportSize({ width, height: 900 });
        await page.addInitScript(installProbes);
        await installApi(page, 'full', mount);
        await throttle(page);
        await page.goto(screen.route);
        const result = await c14Performance(page, screen.scrollToEnd);
        record(ctx, [result]);
      });
    }
  });
}
