// P8-21: the build stamp of public/sw.js. Pure `stampSw` (tested without a build) and `stampDist` (the vite plugin's file work).
// `swFiles` is the list of files the worker caches (relative to dist/); src/shared/lib/pwaStatic.test.ts keeps it in step with
// XD_PRECACHE in public/sw.js.
import { createHash } from 'node:crypto';
import { readFileSync, writeFileSync } from 'node:fs';

export const swFiles = [
  'offline.html',
  'manifest.webmanifest',
  'icons/icon-192.png',
  'icons/icon-512.png',
  'icons/maskable-512.png',
  'icons/apple-touch-icon.png',
];

export const placeholder = "'__XD_BUILD__'";

/** The worker source with its one quoted placeholder replaced by a hash of the cached files; throws if there is not exactly one. */
export function stampSw(source, contents) {
  if (source.split(placeholder).length !== 2) {
    throw new Error(`sw.js must contain exactly one ${placeholder} to stamp`);
  }
  const hash = createHash('sha256');
  contents.forEach((content) => hash.update(content));
  return source.replace(placeholder, `'${hash.digest('hex').slice(0, 10)}'`);
}

/** Stamps `<dist>/sw.js` in place from the files in `<dist>`. `dist` ends with a slash. */
export function stampDist(dist) {
  const source = readFileSync(`${dist}sw.js`, 'utf8');
  writeFileSync(
    `${dist}sw.js`,
    stampSw(
      source,
      swFiles.map((file) => readFileSync(dist + file)),
    ),
  );
}
