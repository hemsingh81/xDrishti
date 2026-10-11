import { buildAccountDetail, buildConnected, buildConnection, buildSwingAccount, summaryOf } from '@/test/fixtures';
import { deriveNotifications, keyWarningDays } from './lib/deriveNotifications';

const now = Date.parse('2026-10-11T10:00:00Z');
const hour = 3_600_000;
const day = 24 * hour;
const iso = (ms: number) => new Date(ms).toISOString();

const main = (overrides = {}) => summaryOf(buildAccountDetail(overrides));
const connectedUntil = (ms: number) => buildConnected({ expiresAt: iso(ms) });
const kinds = (accounts: ReturnType<typeof main>[]) => deriveNotifications(accounts, now).map((n) => n.kind);

describe('deriveNotifications', () => {
  it('has nothing to say when every account is connected and its key is far from expiry', () => {
    expect(deriveNotifications([main({ connection: connectedUntil(now + 6 * hour) })], now)).toEqual([]);
    expect(deriveNotifications([], now)).toEqual([]);
  });

  it('flags an account that is not connected, with a stable id', () => {
    const swing = summaryOf(buildSwingAccount());
    const [item] = deriveNotifications([swing], now);
    expect(item).toMatchObject({
      kind: 'not_connected',
      severity: 'warn',
      accountId: swing.id,
      text: 'Test Dhan Swing is not connected',
      id: `${swing.id}:not_connected:never`,
    });
    expect(deriveNotifications([swing], now + 5 * hour)[0]?.id).toBe(item?.id);
  });

  it('flags an expired token (reported by the API, or past its expiry) as an error', () => {
    const reported = main({ connection: buildConnection({ status: 'expired', expiresAt: iso(now - hour) }) });
    const passed = main({ connection: connectedUntil(now - 1) });
    for (const account of [reported, passed]) {
      expect(deriveNotifications([account], now)[0]).toMatchObject({ kind: 'token_expired', severity: 'error' });
    }
  });

  it('warns when the token runs out within 2 hours, but not at exactly 2 hours', () => {
    expect(kinds([main({ connection: connectedUntil(now + 2 * hour - 1000) })])).toEqual(['token_expiring']);
    expect(kinds([main({ connection: connectedUntil(now + 2 * hour) })])).toEqual([]);
    const [item] = deriveNotifications([main({ connection: connectedUntil(now + 80 * 60_000) })], now);
    expect(item?.text).toBe('Test Dhan Main token expires in 1 h 20 min');
  });

  it('flags the API key within 30 days (inclusive), as expired from its expiry day', () => {
    const keyOn = (offsetDays: number) => iso(now + offsetDays * day).slice(0, 10);
    const key = (offsetDays: number) =>
      main({ apiKeyExpiresOn: keyOn(offsetDays), connection: connectedUntil(now + day) });
    expect(kinds([key(keyWarningDays)])).toEqual(['key_expiring']);
    expect(kinds([key(keyWarningDays + 1)])).toEqual([]);
    expect(kinds([key(1)])).toEqual(['key_expiring']);
    expect(kinds([key(0)])).toEqual(['key_expired']);
    expect(kinds([key(-5)])).toEqual(['key_expired']);
    expect(deriveNotifications([key(3)], now)[0]?.severity).toBe('warn');
    expect(deriveNotifications([key(20)], now)[0]?.severity).toBe('info');
    expect(deriveNotifications([key(-5)], now)[0]?.severity).toBe('error');
  });

  it('changes the id when the situation changes, so it is new again', () => {
    const before = deriveNotifications([main({ connection: connectedUntil(now + hour) })], now)[0];
    const after = deriveNotifications([main({ connection: connectedUntil(now + 90 * 60_000) })], now)[0];
    expect(before?.kind).toBe(after?.kind);
    expect(before?.id).not.toBe(after?.id);
  });

  it('puts errors first, then warnings, then information; newest first within a level; same order every time', () => {
    const expired = summaryOf(
      buildAccountDetail({
        id: 'a-expired',
        name: 'A',
        connection: connectedUntil(now - hour),
        apiKeyExpiresOn: '2030-01-01',
      }),
    );
    const never = summaryOf(buildSwingAccount({ id: 'a-never', name: 'B' }));
    const reconnectNeeded = summaryOf(
      buildSwingAccount({
        id: 'a-last',
        name: 'C',
        connection: buildConnection({ lastConnectedAt: iso(now - 2 * day) }),
      }),
    );
    const keySoon = summaryOf(
      buildAccountDetail({
        id: 'a-key',
        name: 'D',
        connection: connectedUntil(now + day),
        apiKeyExpiresOn: iso(now + 20 * day).slice(0, 10),
      }),
    );
    const list = deriveNotifications([keySoon, never, reconnectNeeded, expired], now);
    expect(list.map((n) => n.kind)).toEqual(['token_expired', 'not_connected', 'not_connected', 'key_expiring']);
    // Within warnings, the one with a time (2 days ago) is newer than the one never connected.
    expect(list.map((n) => n.accountId)).toEqual(['a-expired', 'a-last', 'a-never', 'a-key']);
    expect(deriveNotifications([expired, reconnectNeeded, never, keySoon], now)).toEqual(list);
  });

  it('derives for several accounts at once, one entry per problem', () => {
    const swing = summaryOf(buildSwingAccount({ apiKeyExpiresOn: iso(now + 10 * day).slice(0, 10) }));
    const list = deriveNotifications([main({ connection: connectedUntil(now + hour) }), swing], now);
    expect(list.map((n) => `${n.accountName}:${n.kind}`).sort()).toEqual([
      'Test Dhan Main:token_expiring',
      'Test Dhan Swing:key_expiring',
      'Test Dhan Swing:not_connected',
    ]);
    expect(new Set(list.map((n) => n.id)).size).toBe(3);
  });

  it('counts the key expiry day on the Indian calendar: 23:00Z the day before is already the expiry day (IST)', () => {
    const account = main({
      apiKeyExpiresOn: '2026-10-12',
      connection: connectedUntil(Date.parse('2026-10-13T00:00:00Z')),
    });
    // 22:59Z on 11 Oct = 04:29 IST on 12 Oct is already the expiry day... but 18:29Z on 11 Oct = 23:59 IST on 11 Oct is not.
    expect(deriveNotifications([account], Date.parse('2026-10-11T18:29:00Z'))[0]).toMatchObject({
      kind: 'key_expiring',
    });
    expect(deriveNotifications([account], Date.parse('2026-10-11T23:00:00Z'))[0]).toMatchObject({
      kind: 'key_expired',
    });
  });
});
