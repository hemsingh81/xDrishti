// Public API of the accounts feature. Other code imports only from here.
import { lazy } from 'react';

// Read-only pieces other features (notifications, the search palette) build on.
export { accountsQuery, bucketsQuery } from './api/accountsQueries';
export { useNow } from './hooks/useNow';
export { useAccountsNeedingAction } from './hooks/useAccountsNeedingAction';
export type { AccountNeedingAction, AccountsNeedingAction } from './lib/needingAction';
export { connectionState } from './lib/connection';
export { keyExpiry } from './lib/secrets';
export { AccountsPage } from './components/AccountsPage';
// Shared views shown on the Settings page: risk profiles (shared by Trading accounts) and the accounts × roles matrix.
export { RiskProfilesView } from './components/RiskProfilesView';
export { RoleMatrixView } from './components/RoleMatrixView';

/** The top-bar accounts control. Loaded on demand so the accounts code stays out of the shell; render inside <Suspense>. */
export const HeaderAccounts = lazy(async () => ({
  default: (await import('./components/HeaderAccounts')).HeaderAccounts,
}));

/** The automatic refresh of the accounts with the Portfolio role, as a dialog. Loaded on demand; render inside <Suspense>. */
export const PortfolioRefreshDialog = lazy(async () => ({
  default: (await import('./components/PortfolioRefreshDialog')).PortfolioRefreshDialog,
}));
