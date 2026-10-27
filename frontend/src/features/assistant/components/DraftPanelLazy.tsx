import { lazy, Suspense } from 'react';
import { LoadingState } from '@/shared/ui';

const Panel = lazy(async () => ({ default: (await import('./DraftPanel')).DraftPanel }));

/** The panel in its own chunk: the assistant screen does not pay for it until the owner opens it (P9-07). */
export function DraftPanelLazy() {
  return (
    <Suspense fallback={<LoadingState label="Loading the drafting panel…" />}>
      <Panel />
    </Suspense>
  );
}
