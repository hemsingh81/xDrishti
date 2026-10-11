import type { AccountSummary, BrokerConnection } from '@/shared/api';

export type ConnectionStatusKey = 'not_connected' | 'connected' | 'expired';

const statuses: readonly ConnectionStatusKey[] = ['not_connected', 'connected', 'expired'];

export const connectionStatusLabels: Record<ConnectionStatusKey, string> = {
  connected: 'Connected',
  expired: 'Expired',
  not_connected: 'Not connected',
};

/** An unknown status from an older or newer API is treated as "not connected" (the safe reading). */
export const toStatusKey = (status: string): ConnectionStatusKey =>
  statuses.find((s) => s === status) ?? 'not_connected';

export const connectionStatusLabel = (status: string): string => connectionStatusLabels[toStatusKey(status)];

/** Below this much time left, the status shows a countdown ("expires in 1 h 20 min"). */
export const countdownWindowSeconds = 2 * 3_600;

/** How often a connected status re-checks the clock (the countdown is in minutes). */
export const clockTickMs = 30_000;

export interface ConnectionState {
  /** As the API reported it, except that a connected token whose expiry has passed is "expired" (no refetch needed). */
  status: ConnectionStatusKey;
  expiresAt: string | null;
  /** Seconds until expiry while connected and within the countdown window; otherwise null. */
  secondsLeft: number | null;
}

export function connectionState(connection: BrokerConnection, nowMs: number): ConnectionState {
  const status = toStatusKey(connection.status);
  const { expiresAt } = connection;
  if (status !== 'connected' || expiresAt === null) {
    return { status, expiresAt, secondsLeft: null };
  }
  const secondsLeft = (Date.parse(expiresAt) - nowMs) / 1000;
  if (secondsLeft <= 0) {
    return { status: 'expired', expiresAt, secondsLeft: null };
  }
  return { status, expiresAt, secondsLeft: secondsLeft < countdownWindowSeconds ? secondsLeft : null };
}

/** Whether the clock matters for this connection (only a connected token with an expiry can run out). */
export const needsClock = (connection: BrokerConnection): boolean =>
  toStatusKey(connection.status) === 'connected' && connection.expiresAt !== null;

/** "Reconnect" once a token has run out, otherwise "Connect" (the sign-in at Dhan is the same). */
export const connectLabel = (status: ConnectionStatusKey): string => (status === 'expired' ? 'Reconnect' : 'Connect');

/** Connecting needs the broker app's API key and secret stored first. */
export const appConfigured = (account: Pick<AccountSummary, 'secrets'>): boolean =>
  account.secrets.length > 0 && account.secrets.every((s) => s.set);
