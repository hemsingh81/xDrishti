import { lazy, Suspense } from 'react';
import { LoadingState } from '@/shared/ui';

const Page = lazy(async () => ({ default: (await import('./PaperPage')).PaperPage }));

/** The route's page: the screen loads on demand. */
export function PaperPage() {
  return (
    <Suspense fallback={<LoadingState label="Loading paper trading…" />}>
      <Page />
    </Suspense>
  );
}
