// First-load JavaScript of a production build: the entry script plus every modulepreload chunk of dist/index.html, each
// measured gzip-compressed (zlib level 6, the same as `gzip -c`). Pure apart from the compression: the caller supplies the
// HTML text and a function that reads an asset, so the guard is tested on a fixture without a build (src/test/bundleGuard.test.ts).
import { gzipSync } from 'node:zlib';

/** The rule of frontend/CLAUDE.md: the initial bundle stays at or under 200 KB gzip (1 KB = 1000 bytes, the stricter reading). */
export const BUDGET_BYTES = 200_000;

const attr = (tag, name) =>
  new RegExp(`\\s${name}\\s*=\\s*(?:"([^"]*)"|'([^']*)')`, 'i').exec(tag)?.slice(1).find(Boolean);

const scriptSources = (html) =>
  [...html.matchAll(/<script\b[^>]*>/gi)]
    .map(([tag]) => (attr(tag, 'type') === 'module' ? attr(tag, 'src') : undefined))
    .filter(Boolean);

/** The JavaScript files `index.html` loads before the first screen can render, in document order, without duplicates. */
export function firstLoadAssets(html) {
  const found = [];
  for (const [tag] of html.matchAll(/<script\b[^>]*>/gi)) {
    if (attr(tag, 'type') === 'module' && attr(tag, 'src')) found.push(attr(tag, 'src'));
  }
  for (const [tag] of html.matchAll(/<link\b[^>]*>/gi)) {
    if (attr(tag, 'rel')?.toLowerCase() === 'modulepreload' && attr(tag, 'href')) found.push(attr(tag, 'href'));
  }
  return [...new Set(found)];
}

/** Compressed size in bytes of one file's content. */
export const gzipBytes = (content) => gzipSync(content, { level: 6 }).length;

/**
 * Measures the first load. `readAsset(href)` returns the file's content for an href of index.html ("/assets/x.js") and
 * throws when it is missing: a guard that cannot read what the page loads must fail, never report a smaller number.
 * No entry script in the HTML is an error for the same reason.
 */
export function measureFirstLoad(html, readAsset, budget = BUDGET_BYTES) {
  if (!scriptSources(html).length) {
    throw new Error('index.html has no module entry script with a src: nothing to measure.');
  }
  const assets = firstLoadAssets(html);
  const chunks = assets.map((href) => ({ href, gzip: gzipBytes(readAsset(href)) }));
  const total = chunks.reduce((sum, c) => sum + c.gzip, 0);
  return { chunks, total, budget, withinBudget: total <= budget, headroom: budget - total };
}

/** The warning band: a first load with less headroom than this prints a WARNING (exit stays 0). */
export const WARN_HEADROOM_BYTES = 3_000;

/**
 * Lazy chunks every first paint fetches anyway (the app shell and the landing route), by file-name prefix. They are not part
 * of the guarded number (`measureFirstLoad`) but are measured and printed beside it. A prefix that matches no file, or more
 * than one, throws: renaming a chunk must not silently shrink the printed total.
 */
export const ALWAYS_NEEDED_CHUNKS = ['AppShell', 'today'];

const linksOf = (html) => [...html.matchAll(/<link\b[^>]*>/gi)].map(([tag]) => tag);
const hrefsWhere = (html, test) => [
  ...new Set(
    linksOf(html)
      .filter(test)
      .map((t) => attr(t, 'href'))
      .filter(Boolean),
  ),
];

/**
 * What the first paint really fetches: the guarded chunks, the always-needed lazy chunks, the stylesheets (gzip) and the
 * preloaded fonts (raw bytes: woff2 is already compressed, so they are reported separately and not added to the total).
 * `assetNames` is the file names in dist/assets.
 */
export function measureAlwaysNeeded(html, readAsset, assetNames, guarded) {
  const lazy = ALWAYS_NEEDED_CHUNKS.map((prefix) => {
    const matches = assetNames.filter((n) => new RegExp(`^${prefix}-[^.]+\\.js$`).test(n));
    if (matches.length !== 1) {
      throw new Error(`Expected one "${prefix}-*.js" chunk in dist/assets, found ${matches.length}.`);
    }
    const href = `/assets/${matches[0]}`;
    return { href, gzip: gzipBytes(readAsset(href)) };
  });
  const styles = hrefsWhere(html, (t) => attr(t, 'rel')?.toLowerCase() === 'stylesheet').map((href) => ({
    href,
    gzip: gzipBytes(readAsset(href)),
  }));
  const fonts = hrefsWhere(html, (t) => attr(t, 'rel')?.toLowerCase() === 'preload' && attr(t, 'as') === 'font').map(
    (href) => ({ href, bytes: readAsset(href).length }),
  );
  const extra = [...lazy, ...styles].reduce((sum, c) => sum + c.gzip, 0);
  return { lazy, styles, fonts, total: guarded.total + extra };
}

/** "195.3 KB": bytes in KB (1000) with one decimal. */
export const kb = (bytes) => `${(bytes / 1000).toFixed(1)} KB`;

/** The numbers as printed lines: each chunk, the total, the budget and the verdict. */
export function report(result) {
  const lines = result.chunks.map((c) => `  ${kb(c.gzip).padStart(9)}  ${c.href}`);
  lines.push(
    `First-load JavaScript (entry + modulepreload): ${kb(result.total)} gzip of ${kb(result.budget)} budget, ` +
      (result.withinBudget
        ? `${kb(result.headroom)} to spare.`
        : `OVER by ${kb(-result.headroom)}: lazy-load the biggest eager import.`),
  );
  return lines;
}

/** Lines for the always-needed total: the extra chunks, the stylesheet, the fonts (raw) and the sum. */
export function reportAlwaysNeeded(needed, guarded) {
  return [
    ...[...needed.lazy, ...needed.styles].map(
      (c) => `  ${kb(c.gzip).padStart(9)}  ${c.href} (also fetched at first paint)`,
    ),
    ...needed.fonts.map((f) => `  ${kb(f.bytes).padStart(9)}  ${f.href} (font, raw bytes, not in the total)`),
    `Always needed at first paint (guarded ${kb(guarded.total)} + app shell + landing route + CSS): ${kb(needed.total)} gzip. ` +
      `Only the guarded number is held to ${kb(guarded.budget)}.`,
  ];
}

/** The warning line when the guarded number has less than `WARN_HEADROOM_BYTES` left and is still within budget, else null. */
export function warning(result) {
  if (!result.withinBudget || result.headroom >= WARN_HEADROOM_BYTES) return null;
  return (
    `WARNING: only ${kb(result.headroom)} left under ${kb(result.budget)}. The next slice that adds eager code must first ` +
    `free space (the preload path, splitting the ${kb(result.chunks[0]?.gzip ?? 0)} entry chunk).`
  );
}
