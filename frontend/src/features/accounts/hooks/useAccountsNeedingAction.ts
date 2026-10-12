import { accountsNeedingAction, nothingNeedsAction, type AccountsNeedingAction } from '../lib/needingAction';
import { useAccountStates } from './useAccountStates';

/**
 * Read-only: the accounts whose broker connection has expired or is not connected, for badges outside the accounts
 * screens (the left navigation). Nothing while loading or when the accounts cannot be loaded.
 */
export function useAccountsNeedingAction(): AccountsNeedingAction {
  const { query, states } = useAccountStates();
  return query.isError ? nothingNeedsAction : accountsNeedingAction(states);
}
