import { lazy, Suspense } from 'react';
import { LoadingState } from '@/shared/ui';

const Profile = lazy(async () => ({ default: (await import('./BasketProfilePage')).BasketProfilePage }));
const Ranking = lazy(async () => ({ default: (await import('./RankingPage')).RankingPage }));

/** The profile route: loads on demand (the chart library is a further lazy chunk inside it). */
export function BasketProfilePage() {
  return (
    <Suspense fallback={<LoadingState label="Loading the basket profile…" />}>
      <Profile />
    </Suspense>
  );
}

/** The ranking route: loads on demand. */
export function BasketRankingPage() {
  return (
    <Suspense fallback={<LoadingState label="Loading the ranking…" />}>
      <Ranking />
    </Suspense>
  );
}
