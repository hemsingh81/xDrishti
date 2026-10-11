import { http, HttpResponse } from 'msw';
import { setupServer } from 'msw/node';
import {
  buildAccountDetail,
  buildBuckets,
  buildEvents,
  buildRiskProfiles,
  buildSession,
  buildSwingAccount,
  buildSystemStatus,
  summaryOf,
} from './fixtures';

/** A made-up Dhan consent page URL (the documented form, with a fake consentAppId). */
export const dhanAuthorizeUrl = 'https://auth.dhan.co/login/consentApp-login?consentAppId=fake-consent-0001';

/** The redirect URL Dhan's app must be registered with (made-up host). */
export const dhanRedirectUrl = 'http://localhost:8080/api/brokers/dhan/callback';

/** Default API mocks (network level): a signed-in owner. Override per test with server.use(...). */
export const handlers = [
  http.get('/api/auth/session', () => HttpResponse.json(buildSession())),
  http.get('/api/system/status', () => HttpResponse.json(buildSystemStatus())),
  // Accounts (read side). Writes are mocked per test, because each test cares about a different outcome.
  http.get('/api/accounts', () => HttpResponse.json([summaryOf(buildAccountDetail()), summaryOf(buildSwingAccount())])),
  http.get('/api/accounts/:id', ({ params }) => {
    const detail = [buildAccountDetail(), buildSwingAccount()].find((a) => a.id === params.id);
    return detail
      ? HttpResponse.json(detail)
      : problemResponse({ title: 'Account not found.', code: 'accounts.not_found' }, 404);
  }),
  http.get('/api/accounts/:id/activity', () => HttpResponse.json(buildEvents())),
  http.get('/api/brokers/dhan', () => HttpResponse.json({ redirectUrl: dhanRedirectUrl })),
  http.get('/api/risk-profiles', () => HttpResponse.json(buildRiskProfiles())),
  http.get('/api/portfolio-buckets', () => HttpResponse.json(buildBuckets())),
  // Connecting: the start answers with Dhan's page (tests replace the navigation).
  http.post('/api/accounts/:id/connect', () =>
    HttpResponse.json({ authorizeUrl: dhanAuthorizeUrl, expiresAt: '2026-10-11T10:10:00Z' }),
  ),
];

/** problem+json response as the API sends it. */
export const problemResponse = (body: object, status: number, headers?: Record<string, string>) =>
  HttpResponse.json(body, { status, headers: { 'Content-Type': 'application/problem+json', ...headers } });

export const server = setupServer(...handlers);

/**
 * Mimics the API's step-up rule for a test: `require()` answers 403 `auth.step_up_required` until the password was
 * accepted by POST /api/auth/step-up (the fake password is `correct-password`).
 */
export function createStepUpGate() {
  const gate = {
    elevated: false,
    stepUpCalls: 0,
    handler: http.post('/api/auth/step-up', async ({ request }) => {
      gate.stepUpCalls += 1;
      const { password } = (await request.json()) as { password: string };
      if (password !== 'correct-password') {
        return problemResponse({ title: 'Invalid credentials', code: 'auth.invalid_credentials', status: 401 }, 401);
      }
      gate.elevated = true;
      return HttpResponse.json({ elevatedUntil: '2026-10-10T12:05:00Z' });
    }),
    /** Returns the 403 response while not elevated, otherwise undefined (let the handler proceed). */
    require: () =>
      gate.elevated
        ? undefined
        : problemResponse(
            { title: 'Password confirmation required.', code: 'auth.step_up_required', status: 403 },
            403,
          ),
  };
  return gate;
}
