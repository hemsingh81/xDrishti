import { lazy, Suspense } from 'react';
import { LoadingState } from '@/shared/ui';

const Page = lazy(async () => ({ default: (await import('./replay/ReplayTradeChartPage')).ReplayTradeChartPage }));

/** The route's page: the replay trade chart loads on demand, in its own chunk. */
export function ReplayTradeChartPage() {
  return (
    <Suspense fallback={<LoadingState label="Loading the trade chart…" />}>
      <Page />
    </Suspense>
  );
}
