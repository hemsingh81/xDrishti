import { LinearProgress, Typography } from '@mui/material';
import { DataTable, MetricValue, metricOf, NonValue } from '@/shared/analytics';
import type { SubScore } from '../api/readinessQueries';
import { formatScore, subScoreStateWords } from '../lib/words';

function ScoreCell({ sub }: { sub: SubScore }) {
  if (sub.state === 'not_applicable') return <Typography variant="body2">Not applicable</Typography>;
  const state = sub.state === 'scored' ? metricOf(sub.score, 'The score is not a number.') : null;
  // Unavailable, or a scored sub-score whose number is missing: no digit and no bar, the reason is in the next column.
  if (state?.kind !== 'value') return <NonValue state={{ kind: 'unavailable', reason: '' }} />;
  return (
    <>
      <MetricValue state={state} format={formatScore} />
      <LinearProgress
        variant="determinate"
        value={Math.min(100, Math.max(0, state.value))}
        aria-label={`${sub.name}: ${formatScore(state.value)} of 100`}
        sx={{ mt: 0.5, minWidth: 80 }}
      />
    </>
  );
}

/** The six sub-scores. The score is their minimum; an unavailable one is a hard fail and shows no number, no bar. */
export function SubScoresTable({ subScores }: { subScores: readonly SubScore[] }) {
  return (
    <DataTable
      label="The six sub-scores"
      rows={subScores}
      rowKey={(row) => row.name}
      status={subScores.length === 0 ? { kind: 'empty', reason: 'The server returned no sub-score.' } : undefined}
      columns={[
        { kind: 'text', id: 'name', header: 'Sub-score', render: (row) => row.name },
        { kind: 'text', id: 'state', header: 'State', render: (row) => subScoreStateWords(row.state) },
        { kind: 'text', id: 'score', header: 'Score (of 100)', render: (row) => <ScoreCell sub={row} /> },
        { kind: 'text', id: 'reading', header: 'Reading', render: (row) => row.reading },
        {
          kind: 'text',
          id: 'move',
          header: 'What would move it',
          render: (row) => row.whatWouldMoveIt ?? 'Nothing to move',
        },
      ]}
    />
  );
}
