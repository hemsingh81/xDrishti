import { http, HttpResponse } from 'msw';
import { server } from '@/test/server';
import { api, configureApiAuth } from './client';

// P9-04 AC 12: a wrong password on the assistant settings is an answer, not an ended session.
describe('the shared client and the assistant settings route', () => {
  afterEach(() => {
    configureApiAuth(undefined);
  });

  const put = () =>
    api.PUT('/api/assistant/settings', {
      body: {
        enabled: true,
        portfolioContext: true,
        maxToolCalls: 6,
        reason: 'r',
        expectedSettingsId: 1,
        password: 'x',
      },
    });

  it('does not sign out on auth.invalid_credentials', async () => {
    const onUnauthorized = vi.fn();
    configureApiAuth({ getCsrfToken: () => 'csrf', onUnauthorized });
    server.use(
      http.put('/api/assistant/settings', () =>
        HttpResponse.json({ title: 'Wrong', code: 'auth.invalid_credentials', status: 401 }, { status: 401 }),
      ),
    );
    await put();
    expect(onUnauthorized).not.toHaveBeenCalled();
  });

  it('still signs out on any other 401 from it', async () => {
    const onUnauthorized = vi.fn();
    configureApiAuth({ getCsrfToken: () => 'csrf', onUnauthorized });
    server.use(
      http.put('/api/assistant/settings', () =>
        HttpResponse.json({ title: 'Expired', code: 'auth.session_expired', status: 401 }, { status: 401 }),
      ),
    );
    await put();
    expect(onUnauthorized).toHaveBeenCalledTimes(1);
  });
});
