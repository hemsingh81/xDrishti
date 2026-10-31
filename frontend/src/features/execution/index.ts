// Public API of the execution feature. Other code imports only from here.
// Everything heavy is loaded on demand, so this file stays tiny wherever it is imported (the app bar, the bell, the router).
import { lazy } from 'react';

export { executionNoticesQuery, executionStatusQuery, pollIntervalMs, type ExecutionNotice } from './api/statusApi';

/** The route component, loaded on demand (the router awaits it). */
export const loadExecutionPage = async () => (await import('./components/ExecutionPage')).ExecutionPage;

/** The stop control of the app bar. Loaded on demand; render inside <Suspense>. */
export const StopChip = lazy(async () => ({ default: (await import('./components/StopChip')).StopChip }));
