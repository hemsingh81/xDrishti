import { lazy, Suspense } from 'react';
import { LoadingState } from '@/shared/ui';

const Page = lazy(async () => ({ default: (await import('./replay/SystemReplayPage')).SystemReplayPage }));

/** The route's page: the System replay screen loads on demand, in its own chunk. */
export function SystemReplayPage() {
  return (
    <Suspense fallback={<LoadingState label="Loading the system replay…" />}>
      <Page />
    </Suspense>
  );
}
