// One-off: renders the app mark (public/favicon.svg) to the PWA icon PNGs in public/icons/ with the installed Chrome
// (Playwright, nothing is downloaded). The PNGs are committed; nothing in the build depends on this script.
// Run: node scripts/gen-pwa-icons.mjs
import { chromium } from '@playwright/test';
import { mkdir, writeFile } from 'node:fs/promises';

const mark =
  '<g fill="none" stroke="#fff" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M2 12s3.6-7 10-7 10 7 10 7-3.6 7-10 7S2 12 2 12z"/><circle cx="12" cy="12" r="3.2"/></g>';
const colour = '#4f46e5';
// rounded: the favicon (corner radius 8 of 32). full: a square with the mark inside the maskable safe zone (80 %).
const rounded = `<rect width="32" height="32" rx="8" fill="${colour}"/><g transform="translate(4 4)">${mark}</g>`;
const square = (scale) =>
  `<rect width="32" height="32" fill="${colour}"/><g transform="translate(16 16) scale(${scale}) translate(-12 -12)">${mark}</g>`;
const icons = [
  ['icon-192.png', 192, rounded],
  ['icon-512.png', 512, rounded],
  ['maskable-512.png', 512, square(0.95)],
  ['apple-touch-icon.png', 180, square(1.1)],
];

const browser = await chromium.launch({ channel: 'chrome' });
await mkdir(new URL('../public/icons/', import.meta.url), { recursive: true });
for (const [name, size, body] of icons) {
  const page = await browser.newPage({ viewport: { width: size, height: size } });
  await page.setContent(
    `<style>html,body{margin:0;background:transparent}</style><svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}" viewBox="0 0 32 32">${body}</svg>`,
  );
  await writeFile(new URL(`../public/icons/${name}`, import.meta.url), await page.screenshot({ omitBackground: true }));
  await page.close();
}
await browser.close();
