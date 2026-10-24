import { lazy, Suspense } from 'react';
import { LoadingState } from '@/shared/ui';

const Page = lazy(async () => ({ default: (await import('./PlansPage')).PlansPage }));

/** The route's page: the heavy screen loads on demand, so the light Today tile does not drag it into its own chunk. */
export function PlansPage() {
  return (
    <Suspense fallback={<LoadingState label="Loading plan review…" />}>
      <Page />
    </Suspense>
  );
}
