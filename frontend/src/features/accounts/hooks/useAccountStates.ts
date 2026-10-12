import { useQuery } from '@tanstack/react-query';
import { accountsQuery } from '../api/accountsQueries';
import { connectionState, needsClock } from '../lib/connection';
import type { AccountState } from '../lib/needingAction';
import { useNow } from './useNow';

/**
 * The accounts query with each account's connection status against the clock, re-checked every ~30 s while a connected
 * token can run out (so an expired token flips without a refetch). `states` is empty until the accounts are loaded.
 */
export function useAccountStates() {
  const query = useQuery(accountsQuery);
  const connections = query.data?.map((a) => a.connection) ?? [];
  const now = useNow(connections.some(needsClock), connections.map((c) => c.expiresAt ?? '').join('|'));
  const states: AccountState[] = (query.data ?? []).map((account) => ({
    account,
    status: connectionState(account.connection, now).status,
  }));
  return { query, states };
}
