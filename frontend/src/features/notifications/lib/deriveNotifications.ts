import type { AccountSummary } from '@/shared/api';
import { formatDate, istDayIso } from '@/shared/lib/format';
import { keyExpiry } from '@/features/accounts';

export type NotificationKind =
  | 'key_expiring'
  | 'key_expired'
  | 'data_run_failed'
  | 'data_gaps_open'
  | 'data_quality_failed'
  | 'data_corporate_actions_pending'
  | 'service_failed'
  | 'price_alert_triggered'
  | 'actions_need_decision'
  | 'reconnect_dhan'
  | 'sync_failing'
  | 'holdings_need_review'
  | 'trades_unexplained'
  | 'dividends_pending'
  | 'securities_unmatched'
  | 'rows_dropped'
  | 'plan_awaiting_review'
  | 'plan_nightly_failed'
  | 'plan_tickets_lapsed'
  | 'plan_tickets_armed'
  | 'plan_tickets_invalidated'
  | 'plan_fills_unmatched';
export type NotificationSeverity = 'error' | 'warn' | 'info';

/**
 * The server-derived items that need the owner (ADR 0027), plus the API-key expiry the app derives itself: these pop up once
 * while the app is open; the other notices wait in the bell.
 */
export const attentionKinds: ReadonlySet<NotificationKind> = new Set<NotificationKind>([
  'reconnect_dhan',
  'sync_failing',
  'holdings_need_review',
  'trades_unexplained',
  'dividends_pending',
  'securities_unmatched',
  'rows_dropped',
  'key_expired',
  'key_expiring',
]);

/** What the owner can do about a notification: go to a place in the app, or run an existing endpoint of the API. */
export interface NotificationAction {
  label: string;
  kind: 'route' | 'command';
  /** `route`: an in-app path with its query. `command`: "METHOD /api/path". */
  target: string;
}

/** The API says `error`, `warning` (`warn` in the older notices) or `info`; anything else is read as information. */
export const severityOf = (severity: string): NotificationSeverity =>
  severity === 'error' ? 'error' : severity === 'warn' || severity === 'warning' ? 'warn' : 'info';

export interface AppNotification {
  /** `{accountId}:{kind}:{discriminator}` — a changed situation (new expiry) is a new id, hence unread again. */
  id: string;
  kind: NotificationKind;
  severity: NotificationSeverity;
  /** The account the notice is about; null for notices that are not about one (the data pipeline, the services). */
  accountId: string | null;
  accountName: string | null;
  text: string;
  /** Where "Open" leads (an in-app path). */
  href: string;
  /** When it happened or will happen (ms since epoch); null when unknown. Shown as a relative time. */
  at: number | null;
  /** A short bold first line ("Reconnect Dhan"); null for the notices that are a single sentence. */
  title: string | null;
  /** At most two buttons ("Retry now", "Open"); none for the notices that only have Open. */
  actions: NotificationAction[];
  /** When the situation began (ISO); with `id` it identifies the item, as the server re-raises a resolved one with a new `since`. */
  since?: string | null;
  /** The situation is over; it stays 24 h under "Resolved" and is not counted as unread. */
  resolved: boolean;
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
  ): AppNotification => ({
    ...base,
    id: `${account.id}:${kind}:${discriminator}`,
    kind,
    severity,
    text,
    at,
    title: null,
    actions: [],
    resolved: false,
  });
  const out: AppNotification[] = [];

  // The Dhan login (missing, expired, about to expire) is a server item now: `reconnect_dhan`.
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

/** Open before resolved; urgent (error) first, then warnings, then information; within a level the newest first; ties by id (stable). */
export function compareNotifications(a: AppNotification, b: AppNotification): number {
  return (
    Number(a.resolved) - Number(b.resolved) ||
    rank[b.severity] - rank[a.severity] ||
    (b.at ?? Number.NEGATIVE_INFINITY) - (a.at ?? Number.NEGATIVE_INFINITY) ||
    a.id.localeCompare(b.id)
  );
}

/**
 * Everything the owner should know about the accounts right now. Pure: the same accounts and clock always give the
 * same list. Later sources (alerts, plan tickets) add their own derivations next to this one.
 */
export function deriveNotifications(accounts: readonly AccountSummary[], nowMs: number): AppNotification[] {
  return accounts.flatMap((account) => forAccount(account, nowMs)).sort(compareNotifications);
}
