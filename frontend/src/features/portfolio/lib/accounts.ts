import { connectionState } from '@/features/accounts';
import type { AccountSummary } from '@/shared/api';

/** The accounts whose holdings the Portfolio screen shows: the ones with the Portfolio role on. */
export const portfolioAccounts = (accounts: readonly AccountSummary[]): AccountSummary[] =>
  accounts.filter((account) => account.roles.portfolio.enabled);

/**
 * The one account that Sync now acts on: the chosen account, or the only one there is. With
 * several accounts and "All accounts" there is none: the owner picks one first.
 */
export function actingAccount(accounts: readonly AccountSummary[], selectedId: string | null): AccountSummary | null {
  const selected = accounts.find((account) => account.id === selectedId);
  if (selected) return selected;
  return accounts.length === 1 ? (accounts[0] ?? null) : null;
}

/** Whether Dhan is signed in for the account right now (a token whose expiry has passed counts as not connected). */
export const isConnected = (account: AccountSummary, nowMs: number): boolean =>
  connectionState(account.connection, nowMs).status === 'connected';

/** The account the Trades panel and the Add trade dialog start with: the page's account, or the only one; '' = the owner chooses. */
export function initialTradesAccount(accounts: readonly { id: string }[], scopeAccountId: string | null): string {
  return (
    accounts.find((account) => account.id === scopeAccountId)?.id ??
    (accounts.length === 1 ? (accounts[0]?.id ?? '') : '')
  );
}
