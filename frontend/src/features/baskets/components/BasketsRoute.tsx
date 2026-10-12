import { lazy, Suspense } from 'react';
import { LoadingState } from '@/shared/ui';

// The screen (drag and drop, the members grid, its dialogs) loads on its own: the Instruments screen imports this
// feature for the Add to basket dialog and the drawer's basket chips, and must not download all of that with them.
const Page = lazy(async () => ({ default: (await import('./BasketsPage')).BasketsPage }));

/** The Baskets route element: the screen, loaded on demand. */
export function BasketsPage() {
  return (
    <Suspense fallback={<LoadingState label="Loading baskets" />}>
      <Page />
    </Suspense>
  );
}
