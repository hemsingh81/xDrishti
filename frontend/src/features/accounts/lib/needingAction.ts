import type { AccountSummary } from '@/shared/api';
import type { ConnectionStatusKey } from './connection';

/** An account with its connection status against the clock (a connected token past its expiry reads "expired"). */
export interface AccountState {
  account: AccountSummary;
  status: ConnectionStatusKey;
}

export interface AccountNeedingAction {
  id: string;
  name: string;
  /** What is wrong: the token ran out, or the account was never connected (or was disconnected). */
  status: Exclude<ConnectionStatusKey, 'connected'>;
}

export interface AccountsNeedingAction {
  count: number;
  /** True when any account's connection has expired: the more urgent case of the two. */
  expired: boolean;
  items: AccountNeedingAction[];
}

export const nothingNeedsAction: AccountsNeedingAction = { count: 0, expired: false, items: [] };

/** The accounts that need connecting, from their states. The one place that decides this, shared by the top bar and the menu. */
export function accountsNeedingAction(states: readonly AccountState[]): AccountsNeedingAction {
  const items = states.flatMap(({ account, status }) =>
    status === 'connected' ? [] : [{ id: account.id, name: account.name, status }],
  );
  return { count: items.length, expired: items.some((i) => i.status === 'expired'), items };
}
