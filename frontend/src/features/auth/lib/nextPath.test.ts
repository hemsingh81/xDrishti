import { loginPath, resolveNext } from './nextPath';

describe('resolveNext', () => {
  it.each([
    ['/system', { kind: 'app', path: '/system' }],
    ['/system?tab=db#top', { kind: 'app', path: '/system?tab=db#top' }],
    ['/prototype/', { kind: 'static', path: '/prototype/' }],
    ['/prototype/delivery/index.html', { kind: 'static', path: '/prototype/delivery/index.html' }],
    ['/docs/04-accounts.html', { kind: 'static', path: '/docs/04-accounts.html' }],
    ['/system?x=1', { kind: 'app', path: '/system?x=1' }],
    ['/docs/18-roadmap.html', { kind: 'static', path: '/docs/18-roadmap.html' }],
  ])('accepts the same-origin path %s', (raw, expected) => {
    expect(resolveNext(raw)).toEqual(expected);
  });

  it.each([
    null,
    undefined,
    '',
    '//evil.test',
    '//evil.test/path',
    'https://evil.test',
    'http://evil.test/x',
    'javascript:alert(1)',
    'data:text/html,hi',
    'system',
    '/\\evil.test',
    '/.//evil.com',
    '/x/..//evil.com',
    '/./\\evil.com',
    '//evil.com',
    '/%2f/evil.com',
    '/%2F/evil.com',
    '/%5cevil.com',
    '/%0d%0a/x'.replace('%0d%0a', '\r\n'),
    '/login',
    '/login?next=/system',
  ])('falls back to the home page for %j', (raw) => {
    expect(resolveNext(raw)).toEqual({ kind: 'app', path: '/' });
  });

  it('does not treat look-alike prefixes as static roots', () => {
    expect(resolveNext('/docsify')).toEqual({ kind: 'app', path: '/docsify' });
  });
});

describe('loginPath', () => {
  it('encodes the target and omits it for the home page', () => {
    expect(loginPath('/system?x=1')).toBe('/login?next=%2Fsystem%3Fx%3D1');
    expect(loginPath('/')).toBe('/login');
    expect(loginPath()).toBe('/login');
  });
});
