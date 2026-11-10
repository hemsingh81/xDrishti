import { lazy, Suspense } from 'react';
import { LoadingState } from '@/shared/ui';

const Page = lazy(async () => ({ default: (await import('./SimulationReportPage')).SimulationReportPage }));

/** The route's page: the screen loads on demand. */
export function SimulationReportPage() {
  return (
    <Suspense fallback={<LoadingState label="Loading simulation reports…" />}>
      <Page />
    </Suspense>
  );
}
