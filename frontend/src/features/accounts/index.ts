// Public API of the accounts feature. Other code imports only from here.
import { lazy } from 'react';

export { AccountsPage } from './components/AccountsPage';
// Shared views shown on the Settings page: risk profiles (shared by Trading accounts) and the accounts × roles matrix.
export { RiskProfilesView } from './components/RiskProfilesView';
export { RoleMatrixView } from './components/RoleMatrixView';

/** The top-bar accounts control. Loaded on demand so the accounts code stays out of the shell; render inside <Suspense>. */
export const HeaderAccounts = lazy(async () => ({
  default: (await import('./components/HeaderAccounts')).HeaderAccounts,
}));
