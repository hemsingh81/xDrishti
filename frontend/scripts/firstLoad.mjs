// First-load JavaScript of a production build: the entry script plus every modulepreload chunk of dist/index.html, each
// measured gzip-compressed (zlib level 6, the same as `gzip -c`). Pure apart from the compression: the caller supplies the
// HTML text and a function that reads an asset, so the guard is tested on a fixture without a build (src/test/bundleGuard.test.ts).
import { gzipSync } from 'node:zlib';

/** The rule of frontend/CLAUDE.md: the initial bundle stays at or under 200 KB gzip (1 KB = 1000 bytes, the stricter reading). */
export const BUDGET_BYTES = 200_000;

const attr = (tag, name) =>
  new RegExp(`\\s${name}\\s*=\\s*(?:"([^"]*)"|'([^']*)')`, 'i').exec(tag)?.slice(1).find(Boolean);

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
  const assets = firstLoadAssets(html);
  if (!html.match(/<script\b[^>]*type\s*=\s*["']module["'][^>]*>/i)) {
    throw new Error('index.html has no module entry script: nothing to measure.');
  }
  const chunks = assets.map((href) => ({ href, gzip: gzipBytes(readAsset(href)) }));
  const total = chunks.reduce((sum, c) => sum + c.gzip, 0);
  return { chunks, total, budget, withinBudget: total <= budget, headroom: budget - total };
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
