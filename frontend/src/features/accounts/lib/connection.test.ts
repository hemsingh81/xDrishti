import { ApiError } from '@/shared/api';
import { buildConnected, buildConnection } from '@/test/fixtures';
import { connectErrorMessage, connectionProblem } from './connectErrors';
import { appConfigured, connectionState, connectLabel, toStatusKey } from './connection';

const at = (iso: string) => Date.parse(iso);

describe('connectionState', () => {
  const connection = buildConnected({ expiresAt: '2026-10-10T22:00:00Z' });

  it('has no countdown with two hours or more left', () => {
    expect(connectionState(connection, at('2026-10-10T20:00:00Z'))).toEqual({
      status: 'connected',
      expiresAt: '2026-10-10T22:00:00Z',
      secondsLeft: null,
    });
  });

  it('counts down within the last two hours', () => {
    expect(connectionState(connection, at('2026-10-10T20:40:00Z')).secondsLeft).toBe(80 * 60);
  });

  it('reads a passed expiry as expired, without asking the server', () => {
    expect(connectionState(connection, at('2026-10-10T22:00:00Z')).status).toBe('expired');
  });

  it('leaves other statuses as they are', () => {
    expect(connectionState(buildConnection(), at('2026-10-10T20:00:00Z')).status).toBe('not_connected');
    expect(toStatusKey('unknown')).toBe('not_connected');
  });
});

describe('connection rules', () => {
  it('connects only with both secrets stored', () => {
    expect(appConfigured({ secrets: [{ name: 'apiKey', set: true, updatedAt: null }] })).toBe(true);
    expect(
      appConfigured({
        secrets: [
          { name: 'apiKey', set: true, updatedAt: null },
          { name: 'apiSecret', set: false, updatedAt: null },
        ],
      }),
    ).toBe(false);
    expect(appConfigured({ secrets: [] })).toBe(false);
  });

  it('says Reconnect only once the token has run out', () => {
    expect(connectLabel('not_connected')).toBe('Connect');
    expect(connectLabel('expired')).toBe('Reconnect');
    expect(connectLabel('connected')).toBe('Connect');
  });
});

describe('connect errors', () => {
  it('maps known codes and never repeats an unknown one', () => {
    expect(connectErrorMessage('accounts.connect_expired')).toMatch(/no longer valid .* another browser session/);
    expect(connectErrorMessage('<script>')).toBe('Connecting to Dhan failed. Try again.');
    // Codes from the URL never reach inherited object members.
    for (const code of ['__proto__', 'constructor', 'toString', 'hasOwnProperty']) {
      expect(connectErrorMessage(code)).toBe('Connecting to Dhan failed. Try again.');
    }
    expect(connectErrorMessage(null)).toBe('Connecting to Dhan failed. Try again.');
  });

  it('gives the consent limit with the time until the next try', () => {
    const error = new ApiError(429, { code: 'accounts.consent_limit', title: 'Limit', retryAfterSeconds: 45 * 60 });
    expect(connectionProblem(error)).toMatch(/Try again in 45 min\.$/);
    expect(connectionProblem(new ApiError(429, { code: 'accounts.consent_limit' }))).toMatch(/Try again tomorrow/);
  });

  it("uses the API's own words for broker failures", () => {
    const error = new ApiError(502, { code: 'accounts.broker_error', title: 'Dhan did not answer in 15 s.' });
    expect(connectionProblem(error)).toBe('Dhan did not answer in 15 s.');
  });

  it('leaves field errors to the field', () => {
    const error = new ApiError(400, { code: 'accounts.invalid', errors: { clientId: ['Refused.'] } });
    expect(connectionProblem(error)).toBeUndefined();
  });
});
