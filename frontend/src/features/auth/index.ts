// Public API of the auth feature. Other code imports only from here.
import { lazy } from 'react';

export { sessionQuery, sessionQueryKey, useSession, type SessionState } from './api/sessionQuery';
export { StepUpProvider } from './components/StepUpProvider';
export { useStepUp, type WithStepUp } from './hooks/useStepUp';
export { isStepUpCancelled } from './lib/stepUpErrors';
export { RequireAuth } from './components/RequireAuth';
export { UserMenu } from './components/UserMenu';
export { connectAuth } from './lib/connectAuth';

/** Loaded on demand (route-level code splitting); render inside <Suspense>. */
export const LoginPage = lazy(async () => ({ default: (await import('./components/LoginPage')).LoginPage }));

export const AccountPage = lazy(async () => ({ default: (await import('./components/AccountPage')).AccountPage }));
