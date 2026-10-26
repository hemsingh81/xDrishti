import type { Page } from '@playwright/test';
import { fail, pass, type Result, type XdWindow } from './types';

interface Stop {
  at: string;
  ring: boolean;
  obscured: boolean;
  skip: boolean;
  visits: number;
}

const maxStops = 80;

/** C4 (order start, ring, no trap) and C13 (focus not hidden under another element), by pressing Tab up to 80 times. */
export async function keyboardChecks(page: Page): Promise<Result[]> {
  await page.evaluate(() => {
    (document.activeElement as HTMLElement | null)?.blur();
    window.scrollTo(0, 0);
  });
  const stops: Stop[] = [];
  let trapped = '';
  for (let i = 0; i < maxStops; i += 1) {
    await page.keyboard.press('Tab');
    const stop = await page.evaluate(() => {
      const el = document.activeElement;
      if (!el || el === document.body) return null;
      const sel = (window as unknown as XdWindow).__sel;
      // Count visits on the element itself: selectors are not unique, so they cannot tell a trap from a list of links.
      const visits = Number(el.getAttribute('data-xd-visits') ?? '0') + 1;
      el.setAttribute('data-xd-visits', String(visits));
      const style = getComputedStyle(el);
      const outline = style.outlineStyle !== 'none' && parseFloat(style.outlineWidth) >= 2;
      // Text fields and selects show focus on their own 2 px border (the design in globalStyles.ts), not on an outline.
      const field = el.closest('.MuiInputBase-root')?.querySelector('.MuiOutlinedInput-notchedOutline');
      const border = field ? parseFloat(getComputedStyle(field).borderTopWidth) >= 2 : false;
      const shadow = style.boxShadow !== 'none' && /\b[2-9]px/.test(style.boxShadow);
      const r = el.getBoundingClientRect();
      const x = Math.min(Math.max(r.left + r.width / 2, 0), window.innerWidth - 1);
      const y = Math.min(Math.max(r.top + r.height / 2, 0), window.innerHeight - 1);
      const top = document.elementFromPoint(x, y);
      const obscured = !!top && !el.contains(top) && !top.contains(el);
      return {
        at: sel(el),
        ring: outline || shadow || border,
        obscured,
        visits,
        skip: /skip to content/i.test(el.textContent),
      };
    });
    if (!stop) break; // focus left the page: the cycle is complete
    if (stop.visits >= 3) {
      trapped = stop.at;
      break;
    }
    if (stop.visits === 2) break; // the cycle came round: the whole page was visited
    stops.push(stop);
  }
  const results: Result[] = [];
  const noRing = stops.filter((s) => !s.ring).map((s) => s.at);
  const first = stops[0];
  if (trapped) results.push(fail('C4', `keyboard trap at ${trapped}`));
  else if (!first?.skip) results.push(fail('C4', `first Tab stop is ${first?.at ?? 'nothing'}, not the skip link`));
  else if (noRing.length > 0)
    results.push(
      fail(
        'C4',
        `${noRing.length} of ${stops.length} stops without a 2 px focus ring: ${noRing.slice(0, 4).join(' | ')}`,
      ),
    );
  else results.push(pass('C4', `${stops.length} stops, skip link first, ring on every stop`));
  const hidden = stops.filter((s) => s.obscured).map((s) => s.at);
  results.push(
    hidden.length === 0
      ? pass('C13', `${stops.length} stops, none covered`)
      : fail('C13', `focused element covered: ${hidden.slice(0, 4).join(' | ')}`),
  );
  return results;
}
