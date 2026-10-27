import { lazy, Suspense } from 'react';
import { LoadingState } from '@/shared/ui';

const Page = lazy(async () => ({ default: (await import('./AssistantPage')).AssistantPage }));

/** The route's page: loaded on demand so none of the assistant code is in the entry bundle. */
export function AssistantPage() {
  return (
    <Suspense fallback={<LoadingState label="Loading assistant…" />}>
      <Page />
    </Suspense>
  );
}
