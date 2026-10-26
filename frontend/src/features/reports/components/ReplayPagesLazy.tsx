import { lazy, Suspense } from 'react';
import { LoadingState } from '@/shared/ui';

const Cell = lazy(async () => ({ default: (await import('./CellDetailPage')).CellDetailPage }));
const Replay = lazy(async () => ({ default: (await import('./TradeReplayPage')).TradeReplayPage }));

/** The cell detail route: loads on demand. */
export function CellDetailPage() {
  return (
    <Suspense fallback={<LoadingState label="Loading the cell…" />}>
      <Cell />
    </Suspense>
  );
}

/** The trade replay route: loads on demand (the chart library is a further lazy chunk inside it). */
export function TradeReplayPage() {
  return (
    <Suspense fallback={<LoadingState label="Loading the trade…" />}>
      <Replay />
    </Suspense>
  );
}
