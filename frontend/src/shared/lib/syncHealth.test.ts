import { deepenText, failingText, failureReasonText, healthText, retryText } from './syncHealth';

const now = Date.parse('2026-10-15T10:00:00Z');
const at = (minutes: number) => new Date(now + minutes * 60_000).toISOString();

describe('failureReasonText', () => {
  it('puts every reason code of the API in plain words', () => {
    const codes = [
      'unavailable',
      'rate_limited',
      'interrupted',
      'rejected',
      'history_too_large',
      'token_expired',
      'token_refused',
      'not_connected',
      'unexpected',
    ];
    const words = codes.map(failureReasonText);
    expect(new Set(words).size).toBe(codes.length);
    expect(words).toEqual([
      'Dhan could not be reached',
      'Dhan asked for fewer requests',
      'the run was interrupted',
      'Dhan rejected the request',
      'the history was too large to read at once',
      'the Dhan login has expired',
      'Dhan refused the login',
      'the account is not connected to Dhan',
      'something unexpected went wrong',
    ]);
  });

  it('does not show a code this version does not know', () => {
    expect(failureReasonText('brand_new_reason')).toBe('an unknown problem');
    expect(failureReasonText(null)).toBe('an unknown problem');
    expect(failureReasonText(undefined)).toBe('an unknown problem');
  });
});

describe('retryText', () => {
  it('counts the minutes up and numbers the attempt out of three', () => {
    expect(retryText({ retryAt: at(5), attempts: 2 }, now)).toBe('retrying in 5 minutes (attempt 2 of 3)');
    expect(retryText({ retryAt: at(0.2), attempts: 1 }, now)).toBe('retrying in 1 minute (attempt 1 of 3)');
    expect(retryText({ retryAt: at(15), attempts: 3 }, now)).toBe('retrying in 15 minutes (attempt 3 of 3)');
  });

  it('keeps the attempt between 1 and 3 and says "now" for a time that has come', () => {
    expect(retryText({ retryAt: at(1), attempts: 0 }, now)).toBe('retrying in 1 minute (attempt 1 of 3)');
    expect(retryText({ retryAt: at(1), attempts: 4 }, now)).toBe('retrying in 1 minute (attempt 3 of 3)');
    expect(retryText({ retryAt: at(-2), attempts: 1 }, now)).toBe('retrying now (attempt 1 of 3)');
  });

  it('is null when no retry is pending', () => {
    expect(retryText({ retryAt: null, attempts: 2 }, now)).toBeNull();
    expect(retryText({}, now)).toBeNull();
  });
});

describe('failingText and healthText', () => {
  it('names the days and the reason once the failure goes on and no retry is pending', () => {
    expect(failingText({ consecutiveFailedDays: 1, lastFailureReason: 'unavailable' })).toBe(
      'failed for 1 day: Dhan could not be reached',
    );
    expect(failingText({ consecutiveFailedDays: 4, lastFailureReason: 'history_too_large' })).toBe(
      'failed for 4 days: the history was too large to read at once',
    );
  });

  it('says nothing while healthy, and nothing of the failure while a retry is still to come', () => {
    expect(failingText({ consecutiveFailedDays: 0, lastFailureReason: null })).toBeNull();
    expect(failingText({ consecutiveFailedDays: 2, lastFailureReason: 'unexpected', retryAt: at(5) })).toBeNull();
    expect(healthText({}, now)).toBeNull();
  });

  it('prefers the retry over the failure', () => {
    expect(
      healthText({ consecutiveFailedDays: 2, lastFailureReason: 'unavailable', retryAt: at(5), attempts: 2 }, now),
    ).toBe('retrying in 5 minutes (attempt 2 of 3)');
  });
});

describe('deepenText', () => {
  it('says how far back the history was read, and that there is no more', () => {
    expect(deepenText('2023-03-03', false)).toBe('History read back to 3 Mar 2023');
    expect(deepenText('2023-03-03', true)).toBe('History read back to 3 Mar 2023 — no earlier history found');
    expect(deepenText(null, true)).toBe('No earlier history found');
  });

  it('is null before any deepening', () => {
    expect(deepenText(null, false)).toBeNull();
    expect(deepenText(undefined, undefined)).toBeNull();
  });
});
