import { lazy, Suspense } from 'react';
import { LoadingState } from '@/shared/ui';
import type { BriefingKind } from '../api/briefingApi';

const Card = lazy(async () => ({ default: (await import('./BriefingCard')).BriefingCard }));

/** The card in its own chunk, so the screens that show it do not pay for it before it is on the page (P9-06, ADR 0098). */
export function BriefingCard({ kind, subject }: { kind: BriefingKind; subject: string }) {
  return (
    <Suspense fallback={<LoadingState label="Loading the briefing…" />}>
      <Card kind={kind} subject={subject} />
    </Suspense>
  );
}
