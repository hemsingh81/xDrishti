import { buildAccountDetail, buildConnected, buildConnection, buildSwingAccount, summaryOf } from '@/test/fixtures';
import { compareNotifications, deriveNotifications, keyWarningDays, severityOf } from './lib/deriveNotifications';

const now = Date.parse('2026-10-11T10:00:00Z');
const hour = 3_600_000;
const day = 24 * hour;
const iso = (ms: number) => new Date(ms).toISOString();

const main = (overrides = {}) => summaryOf(buildAccountDetail(overrides));
const connectedUntil = (ms: number) => buildConnected({ expiresAt: iso(ms) });
const keyOn = (offsetDays: number) => iso(now + offsetDays * day).slice(0, 10);
const kinds = (accounts: ReturnType<typeof main>[]) => deriveNotifications(accounts, now).map((n) => n.kind);

describe('deriveNotifications', () => {
  it('has nothing to say when every account has a far API key expiry', () => {
    expect(deriveNotifications([main({ connection: connectedUntil(now + 6 * hour) })], now)).toEqual([]);
    expect(deriveNotifications([], now)).toEqual([]);
  });

  it('leaves the Dhan login to the server: a missing, expired or expiring token derives nothing here', () => {
    const swing = summaryOf(buildSwingAccount());
    const reported = main({ connection: buildConnection({ status: 'expired', expiresAt: iso(now - hour) }) });
    const passed = main({ connection: connectedUntil(now - 1) });
    const expiring = main({ connection: connectedUntil(now + hour) });
    expect(deriveNotifications([swing, reported, passed, expiring], now)).toEqual([]);
  });

  it('flags the API key within 30 days (inclusive), as expired from its expiry day', () => {
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

  it('gives the key notice a stable id that carries the expiry, the Connection tab as where Open leads and no buttons', () => {
    const account = main({ apiKeyExpiresOn: keyOn(10) });
    const [item] = deriveNotifications([account], now);
    expect(item).toMatchObject({
      id: `${account.id}:key_expiring:${keyOn(10)}`,
      href: `/accounts?account=${account.id}&tab=connection`,
      title: null,
      actions: [],
      resolved: false,
    });
    expect(deriveNotifications([account], now + 5 * hour)[0]?.id).toBe(item?.id);
    // The expiry moved: another situation, hence another id (unread again).
    expect(deriveNotifications([main({ apiKeyExpiresOn: keyOn(12) })], now)[0]?.id).not.toBe(item?.id);
  });

  it('puts errors first, then warnings, then information; newest first within a level; same order every time', () => {
    const expired = summaryOf(buildAccountDetail({ id: 'a-expired', name: 'A', apiKeyExpiresOn: keyOn(-2) }));
    const soon = summaryOf(buildAccountDetail({ id: 'a-soon', name: 'B', apiKeyExpiresOn: keyOn(3) }));
    const sooner = summaryOf(buildAccountDetail({ id: 'a-sooner', name: 'C', apiKeyExpiresOn: keyOn(2) }));
    const later = summaryOf(buildAccountDetail({ id: 'a-later', name: 'D', apiKeyExpiresOn: keyOn(20) }));
    const list = deriveNotifications([later, soon, expired, sooner], now);
    expect(list.map((n) => n.accountId)).toEqual(['a-expired', 'a-soon', 'a-sooner', 'a-later']);
    expect(deriveNotifications([expired, sooner, later, soon], now)).toEqual(list);
  });

  it('puts what is resolved after everything that is open, whatever its severity', () => {
    const [open] = deriveNotifications([main({ apiKeyExpiresOn: keyOn(20) })], now);
    const error = { ...open!, id: 'e', severity: 'error' as const, resolved: true };
    const sorted = [error, open!].sort(compareNotifications);
    expect(sorted.map((n) => n.resolved)).toEqual([false, true]);
  });

  it('derives for several accounts at once, one entry per problem', () => {
    const swing = summaryOf(buildSwingAccount({ apiKeyExpiresOn: keyOn(10) }));
    const list = deriveNotifications([main({ apiKeyExpiresOn: keyOn(-1) }), swing], now);
    expect(list.map((n) => `${n.accountName}:${n.kind}`).sort()).toEqual([
      'Test Dhan Main:key_expired',
      'Test Dhan Swing:key_expiring',
    ]);
    expect(new Set(list.map((n) => n.id)).size).toBe(2);
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

describe('severityOf', () => {
  it('reads warning like warn, and anything unknown as information', () => {
    expect(['error', 'warning', 'warn', 'info', 'whatever'].map(severityOf)).toEqual([
      'error',
      'warn',
      'warn',
      'info',
      'info',
    ]);
  });
});
