import { http, HttpResponse } from 'msw';
import { server } from '@/test/server';
import { api, configureApiAuth } from './client';

// LC-01: a wrong password on the resume of a service that needs one is an answer, not an ended session.
describe('the shared client and POST /api/services/{id}/resume', () => {
  afterEach(() => {
    configureApiAuth(undefined);
  });
  const answer = (code: string) => {
    server.use(
      http.post('/api/services/:id/resume', () =>
        HttpResponse.json({ title: 't', code, status: 401 }, { status: 401 }),
      ),
    );
  };
  const call = () =>
    api.POST('/api/services/{id}/resume', { params: { path: { id: 'cells-refresh' } }, body: { password: 'x' } });

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
