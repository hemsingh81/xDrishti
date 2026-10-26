import type { Page } from '@playwright/test';
import { fail, na, pass, type Result, type State, type XdWindow } from './types';

// The words the kit prints for a state; the runner looks for one of them (never for colour).
const stateWords: Record<Exclude<State, 'full'>, RegExp> = {
  loading: /\S+/,
  error: /\S+/,
  empty: /no [a-z ]+ (yet|stored|recorded|found|to show)|nothing|empty|not stored|no snapshot|none/i,
  thin: /not enough|too few|thin|insufficient|fewer than/i,
  unavailable: /unavailable|not evaluable|not evaluable|unknown|cannot be/i,
};

/** C6: with colour removed the state is still an icon plus a word (the word is what is asserted; the icon is noted). */
export async function c6WithoutColour(page: Page, state: State): Promise<Result> {
  if (state === 'full') return na('C6', 'full state: no state word expected');
  await page.addStyleTag({ content: 'html{filter:grayscale(1) !important}' });
  const word = stateWords[state];
  const out = await page.evaluate(
    ({ source, region }) => {
      const re = new RegExp(source, 'i');
      const main = document.querySelector('main');
      // An error is told by its alert (the kit's role=alert region): its text is the word.
      const text = region ? (main?.querySelector(region)?.textContent ?? '') : (main?.innerText ?? '');
      const hit = text.match(re)?.[0] ?? '';
      const holder = hit
        ? [...(main?.querySelectorAll('*') ?? [])]
            .reverse()
            .find((e) => re.test(e.textContent) && e.children.length < 8)
        : undefined;
      const icon = !!holder
        ?.closest('[role=status],[role=alert],section,article,div')
        ?.querySelector('svg, [role=img], .MuiCircularProgress-root');
      return { hit, icon };
    },
    { source: word.source, region: state === 'error' ? '[role=alert]' : state === 'loading' ? '[role=status]' : '' },
  );
  if (!out.hit) return fail('C6', `no ${state} word found in the main region`);
  return pass('C6', `word "${out.hit}"${out.icon ? ' with an icon' : ' (no icon found next to it)'}`);
}

/** C7: a chart (canvas) has a table or a toggle for one in the page, and a text name/summary. */
export async function c7ChartHasTable(page: Page): Promise<Result> {
  const out = await page.evaluate(() => {
    const sel = (window as unknown as XdWindow).__sel;
    const charts = [...document.querySelectorAll('main canvas, main [role=img] canvas')].filter(
      (c) => c.getBoundingClientRect().width > 0,
    );
    if (charts.length === 0) return { charts: 0, missing: [] as string[] };
    const hasTable = document.querySelector('main table, main [role=table], main [role=grid]') !== null;
    const toggle = [...document.querySelectorAll('main button')].some((b) => /table/i.test(b.textContent));
    const missing: string[] = [];
    for (const chart of charts) {
      const named = chart.closest('[role=img][aria-label], [aria-label], figure, [aria-labelledby]');
      if (!named) missing.push(`${sel(chart)} has no text summary`);
    }
    if (!hasTable && !toggle) missing.push('no table or table toggle on the page');
    return { charts: charts.length, missing };
  });
  if (out.charts === 0) return na('C7', 'no chart on this state');
  return out.missing.length === 0
    ? pass('C7', `${out.charts} chart(s) with table and summary`)
    : fail('C7', out.missing.slice(0, 4).join(' | '));
}

/** C9: with reduced motion nothing animates after load (a spinner in the loading state is the state itself). */
export async function c9ReducedMotion(page: Page, state: State): Promise<Result> {
  if (state === 'loading') return na('C9', 'loading state shows a progress indicator by design');
  // Let entry transitions end; an animation still running after that is the finding.
  await page
    .waitForFunction(() => document.getAnimations().every((a) => a.playState !== 'running'), null, { timeout: 1500 })
    .catch(() => undefined);
  const running = await page.evaluate(() => {
    const sel = (window as unknown as XdWindow).__sel;
    return document
      .getAnimations()
      .filter((a) => a.playState === 'running')
      .map((a) => {
        const target = (a.effect as KeyframeEffect | null)?.target;
        return `${target ? sel(target) : 'unknown'} ${a instanceof CSSAnimation ? a.animationName : a instanceof CSSTransition ? a.transitionProperty : ''}`;
      });
  });
  return running.length === 0
    ? pass('C9', 'no running animation')
    : fail('C9', `${running.length} running: ${running.slice(0, 4).join(' | ')}`);
}

/** C11: loading, error and empty render the kit's states, not a blank page. */
export async function c11KitStates(page: Page, state: State): Promise<Result> {
  if (state !== 'loading' && state !== 'error' && state !== 'empty')
    return na('C11', 'checked for loading, error and empty');
  const out = await page.evaluate(() => {
    const main = document.querySelector('main');
    return {
      text: (main?.innerText ?? '').trim().length,
      status: main?.querySelector('[role=status], [role=progressbar]') !== null,
      alert: main?.querySelector('[role=alert]') !== null,
    };
  });
  if (out.text < 20 && !(state === 'loading' && out.status))
    return fail('C11', `the main region is blank (${out.text} characters)`);
  if (state === 'loading')
    return out.status
      ? pass('C11', 'progress/status shown')
      : fail('C11', 'no status or progress element while loading');
  if (state === 'error')
    return out.alert ? pass('C11', 'alert shown') : fail('C11', 'no role=alert element for the failed request');
  return pass('C11', `${out.text} characters of text in the empty state`);
}

/** C15: the live regions announce each state change once; not one announcement per cell. */
export async function c15LiveRegions(page: Page, state: State): Promise<Result> {
  const log = await page.evaluate(() => (window as unknown as XdWindow).__live);
  const distinct = new Set(log);
  const repeated = [...distinct].filter((t) => log.filter((l) => l === t).length > 2);
  if (state === 'error' && !log.some((l) => l.startsWith('alert')))
    return fail('C15', 'the error was not announced by a role=alert region');
  if (log.length > 6)
    return fail('C15', `${log.length} live-region announcements: ${[...distinct].slice(0, 3).join(' | ')}`);
  if (repeated.length > 0) return fail('C15', `announced more than twice: ${repeated[0] ?? ''}`);
  return pass(
    'C15',
    `${log.length} announcement(s)${log.length > 0 ? `: ${[...distinct].slice(0, 2).join(' | ')}` : ''}`,
  );
}

/** C16: reload keeps the URL and the selected tab; Back from another route returns to this one. */
export async function c16UrlState(page: Page, route: string): Promise<Result> {
  const selected = () =>
    page.evaluate(() =>
      [...document.querySelectorAll('[aria-selected=true],[aria-current=page]')]
        .map((e) => e.textContent.trim())
        .join(','),
    );
  const before = { url: page.url(), tab: await selected() };
  await page.reload();
  await page.waitForSelector('main [data-ready]', { state: 'attached', timeout: 20_000 });
  const after = { url: page.url(), tab: await selected() };
  if (after.url !== before.url || after.tab !== before.tab)
    return fail('C16', `reload changed ${before.url} [${before.tab}] to ${after.url} [${after.tab}]`);
  const link = page.getByRole('link', { name: 'Today', exact: true }).first();
  if (!(await link.isVisible()))
    return pass('C16', 'reload keeps the URL and tab; Back not run (no sidebar at this width)');
  await link.click();
  await page.waitForURL((u) => u.pathname === '/');
  await page.goBack();
  await page.waitForSelector('main [data-ready]', { state: 'attached', timeout: 20_000 });
  const back = new URL(page.url());
  return `${back.pathname}${back.search}` === route
    ? pass('C16', 'reload and Back return to the same URL and tab')
    : fail('C16', `Back went to ${back.pathname}${back.search}, expected ${route}`);
}
