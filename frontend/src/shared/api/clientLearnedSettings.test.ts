import { http, HttpResponse } from 'msw';
import { server } from '@/test/server';
import { api, configureApiAuth } from './client';

// AU-10: a wrong password on the four routes that ask for one is an answer, not an ended session.
const wrong = () =>
  HttpResponse.json({ title: 'Wrong', code: 'auth.invalid_credentials', status: 401 }, { status: 401 });
const expired = () =>
  HttpResponse.json({ title: 'Expired', code: 'auth.session_expired', status: 401 }, { status: 401 });

const calls = {
  'the notices switch': () =>
    api.PUT('/api/settings/essentials/{key}', {
      params: { path: { key: 'notices.failure' } },
      body: { on: false, password: 'x' },
    }),
  'a reset': () =>
    api.POST('/api/settings/essentials/{key}/reset', {
      params: { path: { key: 'notices.failure' } },
      body: { password: 'x' },
    }),
  'an approve': () =>
    api.POST('/api/settings/learned/{id}/approve', { params: { path: { id: 4 } }, body: { password: 'x' } }),
  'an undo': () =>
    api.POST('/api/settings/learned/{id}/undo', { params: { path: { id: 4 } }, body: { password: 'x' } }),
} as const;

const handlerFor = (name: keyof typeof calls, answer: () => Response) => {
  switch (name) {
    case 'the notices switch':
      return http.put('/api/settings/essentials/notices.failure', answer);
    case 'a reset':
      return http.post('/api/settings/essentials/notices.failure/reset', answer);
    case 'an approve':
      return http.post('/api/settings/learned/4/approve', answer);
    case 'an undo':
      return http.post('/api/settings/learned/4/undo', answer);
  }
};

describe('the shared client and the learned-setting password routes', () => {
  afterEach(() => {
    configureApiAuth(undefined);
  });

  it.each(Object.keys(calls) as (keyof typeof calls)[])('%s: a wrong password does not sign out', async (name) => {
    const onUnauthorized = vi.fn();
    configureApiAuth({ getCsrfToken: () => 'csrf', onUnauthorized });
    server.use(handlerFor(name, wrong));
    await calls[name]();
    expect(onUnauthorized).not.toHaveBeenCalled();
  });

  it.each(Object.keys(calls) as (keyof typeof calls)[])('%s: any other 401 still signs out', async (name) => {
    const onUnauthorized = vi.fn();
    configureApiAuth({ getCsrfToken: () => 'csrf', onUnauthorized });
    server.use(handlerFor(name, expired));
    await calls[name]();
    expect(onUnauthorized).toHaveBeenCalledTimes(1);
  });
});
