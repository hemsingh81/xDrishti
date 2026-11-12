import { Alert, Box } from '@mui/material';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef } from 'react';
import { ErrorState, LoadingState } from '@/shared/ui';
import { isReplayActive, replayRoot, replayRunQuery } from '../../api/replayApi';
import { HindsightBanner } from './HindsightBanner';
import { PairCard } from './PairCard';
import { ReplayCharts } from './ReplayCharts';
import { ReplayKpis } from './ReplayKpis';
import { ReplayProgressCard } from './ReplayProgressCard';
import { ReplaySessionTable } from './ReplaySessionTable';
import { ReplayTrades } from './ReplayTrades';

/** One run: the hindsight banner first, then the progress, the figures, the curve, the sessions, the trades and the pair. */
export function ReplayResults({ runId }: { runId: string }) {
  const run = useQuery(replayRunQuery(runId));
  const queryClient = useQueryClient();
  const state = run.data?.state;
  const wasActive = useRef(false);
  // When the run leaves an active state, read everything once more so the detail ends on its final figures.
  useEffect(() => {
    const active = isReplayActive(state);
    if (wasActive.current && !active) void queryClient.invalidateQueries({ queryKey: replayRoot });
    wasActive.current = active;
  }, [state, queryClient]);
  if (run.isPending) return <LoadingState label="Loading the replay…" />;
  if (run.isError) return <ErrorState error={run.error} />;
  const r = run.data;
  const active = isReplayActive(r.state);
  return (
    <Box sx={{ display: 'grid', gap: 2, minWidth: 0 }}>
      <HindsightBanner run={r} />
      <ReplayProgressCard run={r} />
      {!r.completeness.complete && r.state !== 'failed' && (
        <Alert severity="info">
          {`Partial: ${r.completeness.sessionsDone} of ${r.completeness.sessionsTotal} sessions are in the figures below.`}
        </Alert>
      )}
      <ReplayKpis run={r} />
      <ReplayCharts runId={r.id} active={active} />
      <ReplaySessionTable runId={r.id} active={active} />
      <ReplayTrades runId={r.id} active={active} />
      {r.pairId && <PairCard pairId={r.pairId} />}
    </Box>
  );
}
