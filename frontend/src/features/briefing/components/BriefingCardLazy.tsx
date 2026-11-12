import { Box } from '@mui/material';
import { visuallyHidden } from '@mui/utils';
import { lazy, Suspense } from 'react';
import { LoadingState } from '@/shared/ui';
import type { BriefingKind } from '../api/briefingApi';

const Card = lazy(async () => ({ default: (await import('./BriefingCard')).BriefingCard }));

/** The card in its own chunk, so the screens that show it do not pay for it before it is on the page (P9-06, ADR 0098). */
export function BriefingCard({ kind, subject }: { kind: BriefingKind; subject: string }) {
  return (
    <Suspense
      fallback={
        <Box sx={{ minHeight: 85 }}>
          <Box sx={visuallyHidden}>
            <LoadingState label="Loading the briefing…" />
          </Box>
        </Box>
      }
    >
      <Card kind={kind} subject={subject} />
    </Suspense>
  );
}
