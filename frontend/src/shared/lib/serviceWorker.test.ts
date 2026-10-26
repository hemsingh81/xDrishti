import { readFileSync } from 'node:fs';
import vm from 'node:vm';

const source = readFileSync('public/sw.js', 'utf8');
const origin = 'https://xd.test';

interface Fake {
  puts: string[];
  fetched: string[];
  deleted: string[];
  responded: Promise<unknown>[];
  listeners: Record<string, (event: unknown) => void>;
  ctx: Record<string, (...args: unknown[]) => unknown> & object;
  names: string[];
  online: boolean;
  offlinePage: unknown;
}

function load(overrides: { badPath?: string } = {}): Fake {
  const f = {
    puts: [],
    fetched: [],
    deleted: [],
    responded: [],
    listeners: {},
    names: ['xd-offline-old', 'other'],
    online: true,
    offlinePage: 'OFFLINE',
  } as unknown as Fake;
  const cache = {
    put: (path: string) => (f.puts.push(path), Promise.resolve()),
    match: () => Promise.resolve(f.offlinePage),
  };
  const response = (path: string) => {
    const html = path === '/offline.html' || path === overrides.badPath;
    return {
      status: 200,
      type: 'basic',
      redirected: false,
      headers: { get: () => (html ? 'text/html' : 'image/png') },
    };
  };
  const self = {
    location: { origin },
    addEventListener: (name: string, fn: (event: unknown) => void) => (f.listeners[name] = fn),
    skipWaiting: () => Promise.resolve(),
    clients: { claim: () => Promise.resolve() },
  };
  f.ctx = vm.createContext({
    self,
    URL,
    Response: { error: () => 'ERROR' },
    caches: {
      open: () => Promise.resolve(cache),
      keys: () => Promise.resolve(f.names),
      delete: (name: string) => (f.deleted.push(name), Promise.resolve(true)),
    },
    fetch: (input: string | { url: string }) => {
      const url = typeof input === 'string' ? input : input.url;
      f.fetched.push(url);
      return f.online ? Promise.resolve(response(url)) : Promise.reject(new TypeError('offline'));
    },
  }) as Fake['ctx'];
  vm.runInContext(source, f.ctx);
  return f;
}

const fire = (f: Fake, name: string, event: Record<string, unknown>) => {
  const waits: Promise<unknown>[] = [];
  f.listeners[name]?.({
    ...event,
    waitUntil: (p: Promise<unknown>) => waits.push(p),
    respondWith: (p: Promise<unknown>) => f.responded.push(p),
  });
  return Promise.all(waits);
};
const request = (path: string, init: { method?: string; mode?: string; range?: string | null } = {}) => ({
  url: path.startsWith('http') ? path : origin + path,
  method: init.method ?? 'GET',
  mode: init.mode ?? 'navigate',
  headers: { get: () => init.range ?? null },
});

describe('service worker request rules', () => {
  const answers = (path: string, init?: Parameters<typeof request>[1]) => {
    const r = request(path, init);
    return load().ctx.xdShouldAnswer?.(origin, r.url, r.method, r.mode, r.headers.get());
  };

  it.each(['/', '/today', '/reports/groups?x=1', '/plans/12#a', '/some/deep/route'])(
    'answers a page navigation to %s',
    (path) => {
      expect(answers(path)).toBe(true);
    },
  );

  it.each([
    '/api/accounts',
    '/api/auth/session',
    '/API/accounts',
    '//api/accounts',
    '/%61pi/accounts',
    '/hubs/events',
    '/health/live',
    '/prototype/',
    '/docs/x',
    '/Docs/x',
    '/assets/index-abc.js',
    '/assets',
    '/%zz',
    '/\\api/x',
    '/\\\\api',
    '/%2fapi',
    '/api%2fx',
    '/api?x=1',
    '/%2e%2e/api/x',
    '/ok/../api/x',
  ])('never answers the network-only path %s', (path) => {
    expect(answers(path)).toBe(false);
  });

  it('never answers a non-navigation, a non-GET, a range request or another origin', () => {
    expect(answers('/today', { mode: 'cors' })).toBe(false);
    expect(answers('/today', { mode: 'no-cors' })).toBe(false);
    expect(answers('/today', { mode: 'same-origin' })).toBe(false);
    for (const method of ['POST', 'PUT', 'DELETE', 'HEAD', 'PATCH']) expect(answers('/today', { method })).toBe(false);
    expect(answers('/today', { range: 'bytes=0-10' })).toBe(false);
    expect(answers('https://evil.test/today')).toBe(false);
    expect(answers('http://xd.test/today')).toBe(false);
    expect(load().ctx.xdShouldAnswer?.(origin, 'not a url', 'GET', 'navigate', null)).toBe(false);
  });
});

describe('service worker request rules: pinned edge spellings', () => {
  const answers = (path: string) => {
    const r = request(path);
    return load().ctx.xdShouldAnswer?.(origin, r.url, r.method, r.mode, null);
  };

  it('a path that merely starts with the same letters, or is decoded only once, is a page', () => {
    expect(answers('/apiary')).toBe(true);
    expect(answers('/%2561pi')).toBe(true); // decoded once, as the server does: the literal path "/%61pi", not /api
  });

  it('a double-slash path on this origin is a same-origin page; a protocol-relative string is not a URL at all', () => {
    expect(answers('//evil.test/x')).toBe(true); // origin + path: still this origin
    expect(load().ctx.xdShouldAnswer?.(origin, '//evil.test/x', 'GET', 'navigate', null)).toBe(false);
  });
});

describe('xdStorable', () => {
  const response = (init: { status?: number; type?: string; redirected?: boolean; contentType?: string }) => ({
    status: init.status ?? 200,
    type: init.type ?? 'basic',
    redirected: init.redirected ?? false,
    headers: { get: () => init.contentType ?? 'image/png' },
  });
  const storable = (path: string, r: ReturnType<typeof response>) => load().ctx.xdStorable?.(path, r);

  it('accepts only the real static file', () => {
    expect(storable('/icons/icon-192.png', response({}))).toBe(true);
    expect(storable('/offline.html', response({ contentType: 'text/html; charset=utf-8' }))).toBe(true);
  });

  it.each([
    ['404', { status: 404 }],
    ['206 partial', { status: 206 }],
    ['redirected', { redirected: true }],
    ['opaque', { type: 'opaque', status: 0 }],
    ['cors', { type: 'cors' }],
    ['an icon that is HTML (the SPA fallback)', { contentType: 'text/html' }],
  ])('refuses %s', (_name, init) => {
    expect(storable('/icons/icon-192.png', response(init))).toBe(false);
  });

  it('refuses the offline page when it is not HTML', () => {
    expect(storable('/offline.html', response({ contentType: 'image/png' }))).toBe(false);
  });
});

describe('service worker events', () => {
  it('does not answer a plain page navigation to /api/x either (mode navigate)', async () => {
    const f = load();
    await fire(f, 'fetch', { request: request('/api/x', { mode: 'navigate' }) });
    expect(f.responded).toHaveLength(0);
    expect(f.fetched).toHaveLength(0);
  });

  it('does not call respondWith for data requests and never touches the network or cache for them', async () => {
    const f = load();
    for (const path of ['/api/accounts', '/hubs/events', '/assets/a.js']) {
      await fire(f, 'fetch', { request: request(path, { mode: 'cors' }) });
    }
    await fire(f, 'fetch', { request: request('/api/x', { method: 'POST' }) });
    expect(f.responded).toHaveLength(0);
    expect(f.fetched).toHaveLength(0);
    expect(f.puts).toHaveLength(0);
  });

  it('passes a navigation to the network and falls back to the offline page only when the fetch rejects', async () => {
    const f = load();
    await fire(f, 'fetch', { request: request('/today') });
    expect(await f.responded[0]).toMatchObject({ status: 200 });
    expect(f.puts).toHaveLength(0); // the page answer is never stored
    f.online = false;
    await fire(f, 'fetch', { request: request('/today') });
    expect(await f.responded[1]).toBe('OFFLINE');
    f.offlinePage = undefined;
    await fire(f, 'fetch', { request: request('/today') });
    expect(await f.responded[2]).toBe('ERROR');
  });

  it('precaches exactly the allow-list at install', async () => {
    const f = load();
    await fire(f, 'install', {});
    expect(f.puts).toEqual([
      '/offline.html',
      '/manifest.webmanifest',
      '/icons/icon-192.png',
      '/icons/icon-512.png',
      '/icons/maskable-512.png',
      '/icons/apple-touch-icon.png',
    ]);
    expect(f.fetched).toEqual(f.puts);
  });

  it('refuses to store an icon that came back as the single-page app fallback', async () => {
    const f = load({ badPath: '/icons/icon-192.png' });
    await expect(fire(f, 'install', {})).rejects.toThrow('not a static file');
  });

  it('deletes older xd- caches at activate and all of them on a clear message, never others', async () => {
    const f = load();
    await fire(f, 'activate', {});
    expect(f.deleted).toEqual(['xd-offline-old']);
    f.deleted.length = 0;
    await fire(f, 'message', { data: { type: 'clear' } });
    expect(f.deleted).toEqual(['xd-offline-old']);
    await fire(f, 'message', { data: { type: 'other' } });
    expect(f.deleted).toHaveLength(1);
  });

  it('source guard: one cache write, no add, no push, no sync, no external URL', () => {
    expect(source.match(/\.put\(/g)).toHaveLength(1);
    expect(source).not.toMatch(/\.add(All)?\(/);
    expect(source).not.toMatch(/addEventListener\('(push|sync|periodicsync|notificationclick)'/);
    expect(source).not.toMatch(/https?:\/\//);
    expect(source).not.toContain('importScripts');
  });
});
