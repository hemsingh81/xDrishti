import { http, HttpResponse } from 'msw';
import { setupServer } from 'msw/node';
import {
  buildAccountDetail,
  buildFacets,
  buildLots,
  buildSummary,
  buildSyncStatus,
  sampleHoldings,
  searchHoldings,
  buildInstrumentDetail,
  buildRefreshStatus,
  sampleInstruments,
  searchInstruments,
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
  // Instruments: the search runs over a small in-memory list; refresh answers a finished run.
  http.get('/api/instruments', ({ request }) =>
    HttpResponse.json(searchInstruments(sampleInstruments, new URL(request.url).searchParams)),
  ),
  http.get('/api/instruments/facets', () => HttpResponse.json(buildFacets())),
  http.get('/api/instruments/refresh/status', () => HttpResponse.json(buildRefreshStatus())),
  http.get('/api/instruments/refresh/history', () => HttpResponse.json([])),
  http.post('/api/instruments/refresh', () =>
    HttpResponse.json({ refreshId: '00000000-0000-4000-8000-0000000003c3' }, { status: 202 }),
  ),
  http.get('/api/instruments/:id', ({ params }) => {
    const found = sampleInstruments.find((i) => i.id === params.id);
    return found
      ? HttpResponse.json(
          buildInstrumentDetail({
            ...found,
            underlyingSymbol: found.symbol.replace('NSE:', ''),
            asmGsmFlag: found.asmGsm ? 'Y' : 'N',
            asmGsmCategory: found.asmGsm ? 'ASM Stage 1' : null,
          }),
        )
      : problemResponse({ title: 'Instrument not found.', code: 'instruments.not_found' }, 404);
  }),
  // Portfolio (read side): holdings are searched in memory; writes (import, sync, date edits) are mocked per test.
  http.get('/api/holdings', ({ request }) =>
    HttpResponse.json(searchHoldings(sampleHoldings, new URL(request.url).searchParams)),
  ),
  http.get('/api/holdings/summary', () => HttpResponse.json(buildSummary())),
  http.get('/api/holdings/:id/lots', ({ params }) => {
    const found = sampleHoldings.find((h) => h.id === params.id);
    return found
      ? HttpResponse.json(buildLots({ holdingId: found.id, symbol: found.symbol }))
      : problemResponse({ title: 'The holding does not exist.', code: 'portfolio.not_found' }, 404);
  }),
  http.get('/api/accounts/:id/holdings/sync-status', () => HttpResponse.json(buildSyncStatus())),
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
