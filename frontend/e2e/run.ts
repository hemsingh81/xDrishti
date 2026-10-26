import type { Page } from '@playwright/test';
import { installApi, type ApiLog } from './api';
import { installProbes } from './checks/dom';
import { axeCheck } from './checks/axe';
import { keyboardChecks } from './checks/keyboard';
import { c10WideTables, c12Reflow, c1NoPageScroll, c2AboveFold, c3TouchTargets } from './checks/layout';
import {
  c11KitStates,
  c15LiveRegions,
  c16UrlState,
  c6WithoutColour,
  c7ChartHasTable,
  c9ReducedMotion,
} from './checks/states';
import { fail, na, pass, type Ctx, type Result } from './checks/types';
import type { Screen } from './matrix';
import { record, shotsDir } from './record';

export interface Opened {
  log: ApiLog;
  release: () => void;
}

/** Opens a screen in a state at a width and scheme, with reduced motion on (C9 reads the page as that user sees it). */
export async function open(page: Page, screen: Screen, ctx: Ctx, mount: () => void): Promise<Opened> {
  await page.setViewportSize({ width: ctx.width, height: ctx.width === 360 ? 800 : 900 });
  await page.emulateMedia({ colorScheme: ctx.scheme, reducedMotion: 'reduce' });
  await page.addInitScript(installProbes);
  const api = await installApi(page, ctx.state, mount);
  await page.goto(screen.route);
  // A screen with its own loading look and no status region is a finding (C11), not a runner failure.
  if (ctx.state === 'loading')
    await page.waitForSelector('main [role=status]', { timeout: 4_000 }).catch(() => undefined);
  else await page.waitForSelector('main[data-ready]', { timeout: 30_000 });
  await page.waitForTimeout(300);
  return api;
}

/** Everything that depends on the state fixtures being rendered, for one open page. */
export async function lightChecks(page: Page, screen: Screen, ctx: Ctx, log: ApiLog): Promise<Result[]> {
  const results: Result[] = [
    await c1NoPageScroll(page),
    await c3TouchTargets(page, ctx.width),
    await c10WideTables(page),
  ];
  results.push(await axeCheck(page, ctx.scheme));
  results.push(await c6WithoutColour(page, ctx.state));
  results.push(await c7ChartHasTable(page));
  results.push(await c11KitStates(page, ctx.state));
  results.push(await c15LiveRegions(page, ctx.state));
  results.push(await c9ReducedMotion(page, ctx.state));
  results.push(await c2AboveFold(page, ctx.width, ctx.state === 'full' && !!screen.review));
  if (ctx.state !== 'loading') {
    results.push(
      log.unanswered.length === 0
        ? pass('C0', `${log.count} API call(s), all answered`)
        : fail('C0', `unanswered: ${[...new Set(log.unanswered)].slice(0, 4).join(' | ')}`),
    );
    if (log.writes.length > 0) results.push(fail('C0-writes', `a read-only screen sent: ${log.writes.join(' | ')}`));
  }
  if (ctx.state === 'full' && ctx.scheme === 'light') {
    if (ctx.width === 360 || ctx.width === 1440) results.push(...(await keyboardChecks(page)));
    if (ctx.width === 1440) results.push(await c16UrlState(page, screen.route));
    if (ctx.width === 360) results.push(await c12Reflow(page));
  }
  return results;
}

/** One cell of the matrix: open, check, screenshot, record. A state without a fixture is recorded `na` without loading. */
export async function runCell(page: Page, screen: Screen, ctx: Ctx): Promise<Result[]> {
  const mount =
    screen.mounts[ctx.state] ?? (ctx.state === 'loading' || ctx.state === 'error' ? screen.mounts.full : undefined);
  if (!mount) {
    const results = [na('C11', `no ${ctx.state} fixture for this screen`)];
    record(ctx, results);
    return results;
  }
  const { log, release } = await open(page, screen, ctx, mount);
  const results =
    ctx.scheme === 'dark'
      ? [await c1NoPageScroll(page), await axeCheck(page, 'dark')]
      : await lightChecks(page, screen, ctx, log);
  await page
    .screenshot({ path: `${shotsDir}${ctx.screen}-${ctx.width}-${ctx.scheme}-${ctx.state}.png`, fullPage: true })
    .catch(() => undefined);
  release();
  record(ctx, results);
  return results;
}
