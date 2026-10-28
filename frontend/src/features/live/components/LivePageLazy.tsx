import { lazy, Suspense } from 'react';
import { LoadingState } from '@/shared/ui';

const Page = lazy(async () => ({ default: (await import('./LivePage')).LivePage }));

/** The route's page: the screen loads on demand. */
export function LivePage() {
  return (
    <Suspense fallback={<LoadingState label="Loading live trading…" />}>
      <Page />
    </Suspense>
  );
}
