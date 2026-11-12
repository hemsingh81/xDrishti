import { Alert, Box } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { ErrorState, LoadingState } from '@/shared/ui';
import { isReplayActive, replayRunQuery } from '../../api/replayApi';
import { HindsightBanner } from './HindsightBanner';
import { PairCard } from './PairCard';
import { ReplayCharts } from './ReplayCharts';
import { ReplayKpis } from './ReplayKpis';
import { ReplayProgressCard } from './ReplayProgressCard';
import { ReplaySessionTable } from './ReplaySessionTable';
import { ReplayTrades } from './ReplayTrades';

/** One run: the hindsight banner first, then the progress, the figures, the curve, the sessions, the trades and the pair. */
export function ReplayResults({ runId, bothModes }: { runId: string; bothModes: boolean }) {
  const run = useQuery(replayRunQuery(runId));
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
      {bothModes && r.pairId && <PairCard pairId={r.pairId} active={active} />}
    </Box>
  );
}
