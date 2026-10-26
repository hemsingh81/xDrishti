/* xDrishti service worker (P8-21, ADR 0132). It does ONE thing: when a page navigation fails because the network is down,
 * it answers with /offline.html. It never answers any other request, never reads or writes a cache for one, and has no
 * push, sync or message-to-fetch path. The cache holds exactly the files in XD_PRECACHE (static, no cookies, no data).
 * The build placeholder below is replaced at build time by a hash of those files, so a new build gets a new cache name. */
const XD_BUILD = '__XD_BUILD__';
const XD_PREFIX = 'xd-';
const XD_CACHE = XD_PREFIX + 'offline-' + XD_BUILD;
const XD_OFFLINE = '/offline.html';
const XD_PRECACHE = [
  XD_OFFLINE,
  '/manifest.webmanifest',
  '/icons/icon-192.png',
  '/icons/icon-512.png',
  '/icons/maskable-512.png',
  '/icons/apple-touch-icon.png',
];
// Paths the network always handles, whatever else is true (checked after decoding, case-folding and collapsing slashes).
const XD_NETWORK_ONLY = /^\/(api|hubs|health|prototype|docs|assets)(\/|$)/;

/** True only for a same-origin, plain GET page navigation outside the network-only paths. */
function xdShouldAnswer(origin, url, method, mode, range) {
  if (method !== 'GET' || mode !== 'navigate' || range) return false;
  let parsed;
  let path;
  try {
    parsed = new URL(url);
    path = decodeURIComponent(parsed.pathname)
      .toLowerCase()
      .replace(/\/{2,}/g, '/');
  } catch {
    return false;
  }
  return parsed.origin === origin && !XD_NETWORK_ONLY.test(path);
}

/** A precache response is storable only if it is the real static file: 200, same origin, not a redirect, not the SPA fallback page. */
function xdStorable(path, response) {
  const type = response.headers.get('content-type') || '';
  const isHtml = type.includes('text/html');
  return (
    response.status === 200 && response.type === 'basic' && !response.redirected && isHtml === (path === XD_OFFLINE)
  );
}

async function xdPrecache() {
  const cache = await caches.open(XD_CACHE);
  for (const path of XD_PRECACHE) {
    const response = await fetch(path, { credentials: 'omit', cache: 'reload' });
    if (!xdStorable(path, response)) throw new Error('not a static file: ' + path);
    await cache.put(path, response);
  }
}

async function xdDeleteCaches(keep) {
  const names = await caches.keys();
  await Promise.all(names.filter((n) => n.startsWith(XD_PREFIX) && n !== keep).map((n) => caches.delete(n)));
}

async function xdOfflineAnswer() {
  const cache = await caches.open(XD_CACHE);
  return (await cache.match(XD_OFFLINE)) || Response.error();
}

self.addEventListener('install', (event) => {
  event.waitUntil(xdPrecache().then(() => self.skipWaiting()));
});

self.addEventListener('activate', (event) => {
  event.waitUntil(xdDeleteCaches(XD_CACHE).then(() => self.clients.claim()));
});

self.addEventListener('message', (event) => {
  if (event.data && event.data.type === 'clear') event.waitUntil(xdDeleteCaches(''));
});

self.addEventListener('fetch', (event) => {
  const r = event.request;
  if (!xdShouldAnswer(self.location.origin, r.url, r.method, r.mode, r.headers.get('range'))) return;
  event.respondWith(fetch(r).catch(xdOfflineAnswer));
});
