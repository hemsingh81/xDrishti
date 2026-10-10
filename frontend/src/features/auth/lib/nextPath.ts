/** Where to go after signing in. `static` targets (docs, prototype) are not React routes: full page load. */
export interface NextTarget {
  kind: 'app' | 'static';
  path: string;
}

const staticRoots = ['/prototype', '/docs'];
const placeholderOrigin = 'http://xd.invalid';

/**
 * Validates the `next` query parameter. Only same-origin relative paths are accepted; anything else
 * (absolute URLs, protocol-relative `//host`, `javascript:`, backslash tricks, control characters, the login
 * page itself) falls back to the home page. This is the open-redirect guard.
 */
export function resolveNext(raw: string | null | undefined): NextTarget {
  const fallback: NextTarget = { kind: 'app', path: '/' };
  // eslint-disable-next-line no-control-regex
  if (!raw || !raw.startsWith('/') || raw.startsWith('//') || /[\\\u0000-\u001f]/.test(raw)) {
    return fallback;
  }
  let url: URL;
  try {
    url = new URL(raw, placeholderOrigin);
  } catch {
    return fallback;
  }
  if (url.origin !== placeholderOrigin || url.pathname === '/login' || url.pathname.startsWith('/login/')) {
    return fallback;
  }
  // Dot segments normalise away (`/.//evil.com` -> `//evil.com`), so re-check the *normalised* path: it must not be
  // protocol-relative, hold a backslash/control character, or smuggle a separator through percent-encoding.
  // eslint-disable-next-line no-control-regex
  if (url.pathname.startsWith('//') || /[\\\u0000-\u001f]|%2f|%5c/i.test(url.pathname)) {
    return fallback;
  }
  const path = `${url.pathname}${url.search}${url.hash}`;
  const isStatic = staticRoots.some((root) => url.pathname === root || url.pathname.startsWith(`${root}/`));
  return { kind: isStatic ? 'static' : 'app', path };
}

/** `/login?next=<current location>`; the home page needs no `next`. */
export function loginPath(next?: string): string {
  return next && next !== '/' ? `/login?next=${encodeURIComponent(next)}` : '/login';
}
