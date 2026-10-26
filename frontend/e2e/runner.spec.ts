import { expect, test } from '@playwright/test';
import { c1NoPageScroll, c3TouchTargets } from './checks/layout';
import type { Ctx } from './checks/types';
import { screens } from './matrix';
import { evidenceDir, record } from './record';
import { lightChecks, open } from './run';

// The self-test of the runner (P8-16 criterion 1): two screens at two widths prove the machinery, and two planted defects
// prove the checks can fail. It writes beside the matrix results, never into them.

const selftestFile = `${evidenceDir}runner-selftest.jsonl`;

const shown = screens.filter((s) => s.id === 'P8-02-overview' || s.id === 'P8-08-what-works');

for (const screen of shown) {
  for (const width of [360, 1440]) {
    test(`self-test ${screen.id} at ${width}: every check reports and no request is unanswered`, async ({ page }) => {
      const ctx: Ctx = { screen: screen.id, width, scheme: 'light', state: 'full' };
      const mount = screen.mounts.full;
      if (!mount) throw new Error('fixture missing');
      const { log, release } = await open(page, screen, ctx, mount);
      const results = await lightChecks(page, screen, ctx, log);
      release();
      record(ctx, results, selftestFile);
      expect(log.unanswered).toEqual([]);
      expect(results.map((r) => r.check)).toEqual(expect.arrayContaining(['C1', 'C3', 'C5', 'C10', 'C4', 'C13']));
      for (const result of results) expect(['pass', 'fail', 'na']).toContain(result.status);
      expect(results.find((r) => r.check === 'C1')?.status).toBe('pass');
    });
  }
}

test('self-test: a planted 2000 px div fails C1 and names the element', async ({ page }) => {
  const screen = shown[0];
  const mount = screen?.mounts.full;
  if (!screen || !mount) throw new Error('fixture missing');
  await open(page, screen, { screen: screen.id, width: 360, scheme: 'light', state: 'full' }, mount);
  await page.evaluate(() => {
    const div = document.createElement('div');
    div.id = 'planted-overflow';
    div.style.cssText = 'width:2000px;height:10px';
    document.querySelector('main')?.append(div);
  });
  const result = await c1NoPageScroll(page);
  expect(result.status).toBe('fail');
  expect(result.note).toContain('planted-overflow');
});

test('self-test: a planted 20 px button fails C3 at 360 and at 1440', async ({ page }) => {
  const screen = shown[0];
  const mount = screen?.mounts.full;
  if (!screen || !mount) throw new Error('fixture missing');
  await open(page, screen, { screen: screen.id, width: 360, scheme: 'light', state: 'full' }, mount);
  await page.evaluate(() => {
    const button = document.createElement('button');
    button.id = 'planted-small';
    button.textContent = 'x';
    button.style.cssText = 'width:20px;height:20px;padding:0';
    document.querySelector('main')?.append(button);
  });
  for (const [width, minimum] of [
    [360, 44],
    [1440, 24],
  ] as const) {
    const result = await c3TouchTargets(page, width);
    expect(result.status).toBe('fail');
    expect(result.note).toContain('planted-small');
    expect(result.note).toContain(`under ${minimum}x${minimum}`);
  }
});
