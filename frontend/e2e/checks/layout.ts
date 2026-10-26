import type { Page } from '@playwright/test';
import { fail, na, pass, type Result, type XdWindow } from './types';

const list = (items: string[]) => items.slice(0, 5).join(' | ');

/** C1: the page itself never scrolls sideways. Offenders: elements reaching past the viewport outside a scroller. */
export async function c1NoPageScroll(page: Page): Promise<Result> {
  const out = await page.evaluate(() => {
    const root = document.scrollingElement ?? document.documentElement;
    const sel = (window as unknown as XdWindow).__sel;
    const over = root.scrollWidth - root.clientWidth;
    if (over <= 0) return { over: 0, offenders: [] as string[] };
    const offenders: string[] = [];
    for (const el of document.body.querySelectorAll('*')) {
      const r = el.getBoundingClientRect();
      if (r.width === 0 || r.right <= root.clientWidth + 1) continue;
      let clipped = false;
      for (let p = el.parentElement; p && p !== document.body; p = p.parentElement) {
        if (
          /(auto|scroll|hidden|clip)/.test(getComputedStyle(p).overflowX) &&
          p.getBoundingClientRect().right <= root.clientWidth + 1
        ) {
          clipped = true;
          break;
        }
      }
      if (!clipped) offenders.push(`${sel(el)} right=${Math.round(r.right)}`);
      if (offenders.length >= 5) break;
    }
    return { over, offenders };
  });
  return out.over <= 0 ? pass('C1') : fail('C1', `page scrolls sideways by ${out.over}px: ${list(out.offenders)}`);
}

/** C2: the screen's answer is above the fold at 360 x 800 (review screens only). */
export async function c2AboveFold(page: Page, width: number, review: boolean): Promise<Result> {
  if (!review) return na('C2', 'not a review-type screen');
  if (width !== 360) return na('C2', 'checked at 360 x 800 only');
  const out = await page.evaluate(() => {
    const main = document.querySelector('main');
    const sel = (window as unknown as XdWindow).__sel;
    // The answer is the first piece of content of the screen: the first panel body, heading, table, status or alert in the
    // main region (a proxy; the owner's walkthrough judges whether it is the answer).
    const candidates = [
      ...(main?.querySelectorAll('[role=tabpanel] > *, h2, h3, table, [role=status], [role=alert]') ?? []),
    ];
    const first = candidates.sort((a, b) => a.getBoundingClientRect().top - b.getBoundingClientRect().top)[0];
    if (!first) return { found: false, top: 0, bottom: 0, at: '' };
    const r = first.getBoundingClientRect();
    return { found: true, top: Math.round(r.top), bottom: Math.round(r.bottom), at: sel(first) };
  });
  if (!out.found) return fail('C2', 'no answer element (heading, status, table) found in the main region');
  return out.top < 800
    ? pass('C2', `first answer element starts at y=${out.top} (${out.at})`)
    : fail('C2', `first answer element starts at y=${out.top}, below the 800 px fold (${out.at})`);
}

/** C3: touch targets, at least 44 below 480 px and at least 24 at every width (WCAG 2.5.8). */
export async function c3TouchTargets(page: Page, width: number): Promise<Result> {
  const minimum = width < 480 ? 44 : 24;
  const out = await page.evaluate((min) => {
    const sel = (window as unknown as XdWindow).__sel;
    const selector =
      'button, a[href], input:not([type=hidden]), select, textarea, [role=button], [role=tab], [role=link], [role=menuitem], [role=checkbox], [role=switch], [role=option], [tabindex]:not([tabindex="-1"])';
    const bad: string[] = [];
    let seen = 0;
    for (const el of document.querySelectorAll(selector)) {
      const style = getComputedStyle(el);
      if (style.visibility === 'hidden' || style.display === 'none' || el.closest('[aria-hidden=true],[inert]'))
        continue;
      const r = el.getBoundingClientRect();
      if (r.width < 2 || r.height < 2) continue; // the clipped skip link and similar
      // Inline text links inside a sentence are exempt.
      if (
        el.tagName === 'A' &&
        style.display === 'inline' &&
        el.parentElement &&
        /^(P|SPAN|LI|TD|DD|DIV)$/.test(el.parentElement.tagName) &&
        el.parentElement.textContent.trim() !== el.textContent.trim()
      )
        continue;
      // Radio/checkbox inputs are covered by their label.
      if (el.tagName === 'INPUT' && el.closest('label')) continue;
      seen += 1;
      if (r.width < min - 0.5 || r.height < min - 0.5)
        bad.push(`${sel(el)} ${Math.round(r.width)}x${Math.round(r.height)}`);
    }
    return { seen, bad };
  }, minimum);
  if (out.bad.length === 0) return pass('C3', `${out.seen} targets at least ${minimum}x${minimum}`);
  return fail('C3', `${out.bad.length} of ${out.seen} targets under ${minimum}x${minimum}: ${list(out.bad)}`);
}

/** C10: a table wider than the page scrolls inside its own labelled region. */
export async function c10WideTables(page: Page): Promise<Result> {
  const out = await page.evaluate(() => {
    const sel = (window as unknown as XdWindow).__sel;
    const bad: string[] = [];
    let wide = 0;
    for (const table of document.querySelectorAll('table, [role=grid], [role=table]')) {
      const r = table.getBoundingClientRect();
      if (r.width === 0) continue;
      let scroller: Element | null = null;
      for (let p = table.parentElement; p && p !== document.body; p = p.parentElement) {
        if (/(auto|scroll)/.test(getComputedStyle(p).overflowX)) {
          scroller = p;
          break;
        }
      }
      const container = scroller ?? document.documentElement;
      const overflows =
        (scroller ? scroller.scrollWidth > scroller.clientWidth + 1 : false) ||
        r.right > document.documentElement.clientWidth + 1;
      if (!overflows) continue;
      wide += 1;
      if (!scroller) {
        bad.push(`${sel(table)} overflows with no scrolling region`);
        continue;
      }
      const named = scroller.hasAttribute('aria-label') || scroller.hasAttribute('aria-labelledby');
      const focusable =
        scroller.getAttribute('tabindex') !== null || scroller.querySelector('a, button, input') !== null;
      if (!named || !focusable)
        bad.push(
          `${sel(container)} scrolls but is ${named ? '' : 'unlabelled'}${named || focusable ? '' : ' and '}${focusable ? '' : 'not keyboard reachable'}`,
        );
    }
    return { wide, bad };
  });
  if (out.wide === 0) return na('C10', 'no table wider than its card');
  return out.bad.length === 0
    ? pass('C10', `${out.wide} wide table(s) scroll in a labelled region`)
    : fail('C10', list(out.bad));
}

/** C12: reflow at 360 with 200 % text: nothing cut off (text clipped by overflow) and no page scroll. Overlap is not measured. */
export async function c12Reflow(page: Page): Promise<Result> {
  await page.addStyleTag({ content: 'html{font-size:200% !important}' });
  await page.evaluate(
    () =>
      new Promise<void>((done) => {
        requestAnimationFrame(() => {
          requestAnimationFrame(() => {
            done();
          });
        });
      }),
  );
  const scroll = await c1NoPageScroll(page);
  const clipped = await page.evaluate(() => {
    const sel = (window as unknown as XdWindow).__sel;
    const out: string[] = [];
    for (const el of document.querySelectorAll('main *')) {
      const s = getComputedStyle(el);
      if (!/(hidden|clip)/.test(s.overflowX) || s.textOverflow === 'ellipsis') continue;
      if (el.scrollWidth > el.clientWidth + 2 && el.textContent.trim())
        out.push(`${sel(el)} ${el.scrollWidth}>${el.clientWidth}`);
    }
    return out;
  });
  if (scroll.status === 'fail') return fail('C12', `at 200% text: ${scroll.note}`);
  return clipped.length === 0
    ? pass('C12', 'no sideways scroll and no clipped text at 200% text')
    : fail('C12', `clipped text at 200%: ${list(clipped)}`);
}
