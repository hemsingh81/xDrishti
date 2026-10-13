import { lazy, Suspense } from 'react';
import { ActionCenterSkeleton } from './ActionCenterSkeleton';
import type { ActionCenterProps } from './ActionCenterCard';

// The card (its list, rows, menus and the basket picker) loads on its own, after the Portfolio screen is up.
const Card = lazy(async () => ({ default: (await import('./ActionCenterCard')).ActionCenterCard }));

/** The Action center card of the Portfolio screen, loaded on demand. */
export function ActionCenter(props: ActionCenterProps) {
  return (
    <Suspense fallback={<ActionCenterSkeleton />}>
      <Card {...props} />
    </Suspense>
  );
}
