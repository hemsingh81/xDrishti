import type { AccountSummary } from '@/shared/api';
import { formatDate, formatHoursMinutes, istDayIso } from '@/shared/lib/format';
import { connectionState, keyExpiry } from '@/features/accounts';

export type NotificationKind =
  | 'not_connected'
  | 'token_expired'
  | 'token_expiring'
  | 'key_expiring'
  | 'key_expired'
  | 'data_run_failed'
  | 'data_gaps_open'
  | 'data_quality_failed'
  | 'data_corporate_actions_pending';
export type NotificationSeverity = 'error' | 'warn' | 'info';

export interface AppNotification {
  /** `{accountId}:{kind}:{discriminator}` — a changed situation (new expiry) is a new id, hence unread again. */
  id: string;
  kind: NotificationKind;
  severity: NotificationSeverity;
  /** The account the notice is about; null for notices that are not about one (the data pipeline). */
  accountId: string | null;
  accountName: string | null;
  text: string;
  /** Where "Open" leads (an in-app path). */
  href: string;
  /** When it happened or will happen (ms since epoch); null when unknown. Shown as a relative time. */
  at: number | null;
}

/** API keys are flagged this many days ahead (the first day the key no longer works counts as day 0). */
export const keyWarningDays = 30;
const urgentKeyDays = 7;
const rank: Record<NotificationSeverity, number> = { error: 2, warn: 1, info: 0 };

function forAccount(account: AccountSummary, nowMs: number): AppNotification[] {
  const base = {
    accountId: account.id,
    accountName: account.name,
    href: `/accounts?account=${account.id}&tab=connection`,
  };
  const make = (
    kind: NotificationKind,
    severity: NotificationSeverity,
    discriminator: string,
    text: string,
    at: number | null,
  ): AppNotification => ({ ...base, id: `${account.id}:${kind}:${discriminator}`, kind, severity, text, at });
  const out: AppNotification[] = [];

  const state = connectionState(account.connection, nowMs);
  const expiry = state.expiresAt;
  const expiryMs = expiry === null ? null : Date.parse(expiry);
  if (state.status === 'not_connected') {
    const last = account.connection.lastConnectedAt;
    out.push(
      make(
        'not_connected',
        'warn',
        last ?? 'never',
        `${account.name} is not connected`,
        last === null ? null : Date.parse(last),
      ),
    );
  } else if (state.status === 'expired') {
    out.push(make('token_expired', 'error', expiry ?? 'unknown', `${account.name} token expired`, expiryMs));
  } else if (state.secondsLeft !== null) {
    out.push(
      make(
        'token_expiring',
        'warn',
        expiry ?? 'unknown',
        `${account.name} token expires in ${formatHoursMinutes(state.secondsLeft)}`,
        expiryMs,
      ),
    );
  }

  const key = keyExpiry(account.apiKeyExpiresOn, keyWarningDays, istDayIso(nowMs));
  const keyAt = Date.parse(`${account.apiKeyExpiresOn}T00:00:00+05:30`);
  const on = formatDate(account.apiKeyExpiresOn);
  if (key.state === 'expired') {
    out.push(make('key_expired', 'error', account.apiKeyExpiresOn, `${account.name} API key expired on ${on}`, keyAt));
  } else if (key.state === 'soon') {
    const severity = key.daysLeft <= urgentKeyDays ? 'warn' : 'info';
    out.push(
      make('key_expiring', severity, account.apiKeyExpiresOn, `${account.name} API key expires on ${on}`, keyAt),
    );
  }
  return out;
}

/** Urgent (error) first, then warnings, then information; within a level the newest first; ties by id (stable). */
export function compareNotifications(a: AppNotification, b: AppNotification): number {
  return (
    rank[b.severity] - rank[a.severity] ||
    (b.at ?? Number.NEGATIVE_INFINITY) - (a.at ?? Number.NEGATIVE_INFINITY) ||
    a.id.localeCompare(b.id)
  );
}

/**
 * Everything the owner should know about the accounts right now. Pure: the same accounts and clock always give the
 * same list. Later sources (alerts, services, plan tickets) add their own derivations next to this one.
 */
export function deriveNotifications(accounts: readonly AccountSummary[], nowMs: number): AppNotification[] {
  return accounts.flatMap((account) => forAccount(account, nowMs)).sort(compareNotifications);
}
