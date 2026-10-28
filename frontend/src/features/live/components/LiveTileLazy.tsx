import { lazy, Suspense } from 'react';

const Tile = lazy(async () => ({ default: (await import('./LiveTile')).LiveTile }));

/** The Today tile of the live phase: its code loads on demand, so the landing route carries only this wrapper. */
export function LiveTile() {
  return (
    <Suspense fallback={null}>
      <Tile />
    </Suspense>
  );
}
