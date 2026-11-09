import { http, HttpResponse } from 'msw';
import { server } from '@/test/server';
import { api, configureApiAuth } from './client';

// CF-12: a wrong password on the three kill-act routes that ask for one is an answer, not an ended session.
const routes: [string, string, () => Promise<unknown>][] = [
  [
    'post',
    '/api/confidence/kill-proposals/:id/dismiss',
    () =>
      api.POST('/api/confidence/kill-proposals/{id}/dismiss', {
        params: { path: { id: 1 } },
        body: { reason: 'r', password: 'x' },
      }),
  ],
  [
    'post',
    '/api/learning/cells/:key/re-enable',
    () =>
      api.POST('/api/learning/cells/{key}/re-enable', {
        params: { path: { key: 'a|b' } },
        body: { reason: 'r', password: 'x' },
      }),
  ],
  [
    'put',
    '/api/confidence/kill-settings',
    () => api.PUT('/api/confidence/kill-settings', { body: { auto: true, reason: 'r', password: 'x' } }),
  ],
];

describe.each(routes)('the shared client and the kill act %s %s', (method, path, call) => {
  afterEach(() => {
    configureApiAuth(undefined);
  });
  const answer = (code: string) => {
    const handler = method === 'put' ? http.put : http.post;
    server.use(handler(path, () => HttpResponse.json({ title: 't', code, status: 401 }, { status: 401 })));
  };

  it('does not sign out on auth.invalid_credentials', async () => {
    const onUnauthorized = vi.fn();
    configureApiAuth({ getCsrfToken: () => 'csrf', onUnauthorized });
    answer('auth.invalid_credentials');
    await call();
    expect(onUnauthorized).not.toHaveBeenCalled();
  });

  it('still signs out on any other 401', async () => {
    const onUnauthorized = vi.fn();
    configureApiAuth({ getCsrfToken: () => 'csrf', onUnauthorized });
    answer('auth.session_expired');
    await call();
    expect(onUnauthorized).toHaveBeenCalledTimes(1);
  });
});
