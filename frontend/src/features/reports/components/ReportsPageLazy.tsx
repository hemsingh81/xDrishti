import { lazy, Suspense } from 'react';
import { LoadingState } from '@/shared/ui';

const Page = lazy(async () => ({ default: (await import('./ReportsPage')).ReportsPage }));

/** The route's page: the screen loads on demand. */
export function ReportsPage() {
  return (
    <Suspense fallback={<LoadingState label="Loading reports…" />}>
      <Page />
    </Suspense>
  );
}
