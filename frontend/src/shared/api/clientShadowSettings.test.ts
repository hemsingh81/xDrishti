import { http, HttpResponse } from 'msw';
import { server } from '@/test/server';
import { api, configureApiAuth } from './client';

// OPS-01: a wrong password on the owner's shadow switch is an answer, not an ended session.
describe('the shared client and PUT /api/paper/shadow/settings', () => {
  afterEach(() => {
    configureApiAuth(undefined);
  });
  const answer = (code: string) => {
    server.use(
      http.put('/api/paper/shadow/settings', () =>
        HttpResponse.json({ title: 't', code, status: 401 }, { status: 401 }),
      ),
    );
  };
  const call = () =>
    api.PUT('/api/paper/shadow/settings', {
      body: { enabled: true, reason: 'r', password: 'x', acknowledgeGap: false },
    });

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
