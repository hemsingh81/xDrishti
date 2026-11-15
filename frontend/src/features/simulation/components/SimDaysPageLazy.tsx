import { lazy, Suspense } from 'react';
import { LoadingState } from '@/shared/ui';

const Page = lazy(async () => ({ default: (await import('./days/SimDaysPage')).SimDaysPage }));

/** The route's page: the day-by-day simulation loads on demand, in its own chunk. */
export function SimDaysPage() {
  return (
    <Suspense fallback={<LoadingState label="Loading the day-by-day simulation…" />}>
      <Page />
    </Suspense>
  );
}
