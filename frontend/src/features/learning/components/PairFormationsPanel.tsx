import { Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { DataTable, formatCount, formatRatio, metricOf, reportedMetric, type Column } from '@/shared/analytics';
import type { Schemas } from '@/shared/api';
import { formatIstDate } from '@/shared/lib/format';
import { ErrorState, LoadingState } from '@/shared/ui';
import { pairFormationsQuery } from '../api/p14Queries';
import { Panel } from './Panel';

type Formation = Schemas['PairFormationSummary'];

const count = (id: string, header: string, pick: (f: Formation) => number): Column<Formation> => ({
  kind: 'metric',
  id,
  header,
  metric: (f) => metricOf(pick(f)),
  format: formatCount,
});

/** The half-life (in sessions) as min / median / max, or the word and reason when the formation selected none. */
const halfLife = (pick: 'min' | 'median' | 'max'): Column<Formation> => ({
  kind: 'metric',
  id: `half-${pick}`,
  header: `Half-life ${pick} (sessions)`,
  metric: (f) =>
    f.halfLives.state === 'ok'
      ? metricOf(f.halfLives[pick])
      : { kind: 'unavailable', reason: (f.halfLives.reason ?? 'none selected').replaceAll('_', ' ') },
  format: formatRatio,
});

const columns: Column<Formation>[] = [
  {
    kind: 'text',
    id: 'quarter',
    header: 'Quarter',
    render: (f) => `${f.quarter} (${formatIstDate(f.windowFrom)} to ${formatIstDate(f.windowTo)})`,
  },
  count('candidates', 'Candidates', (f) => f.candidates),
  count('tested', 'Tested', (f) => f.tested),
  count('unavailable', 'Unavailable', (f) => f.unavailable),
  count('selected', 'Selected', (f) => f.selected),
  {
    kind: 'metric',
    id: 'bh',
    header: 'BH threshold',
    metric: (f) => reportedMetric(f.bhThreshold),
    format: formatRatio,
  },
  halfLife('min'),
  halfLife('median'),
  halfLife('max'),
];

/** Pair formations per quarter: candidates, tested, unavailable, selected, threshold and half-lives. No spread or price. */
export function PairFormationsPanel() {
  const query = useQuery(pairFormationsQuery);
  const data = query.data;
  const empty = data && (data.state === 'unavailable' || data.items.length === 0);
  return (
    <Panel title="Pair formations">
      {query.isPending && <LoadingState label="Loading pair formations" />}
      {query.isError && <ErrorState error={query.error} />}
      {data && (
        <>
          <DataTable
            label="Pair formations"
            columns={columns}
            rows={data.items}
            rowKey={(f) => f.id}
            status={empty ? { kind: 'empty', reason: 'no pair formation stored yet (xd pairs run)' } : undefined}
          />
          {data.truncated && (
            <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
              More formations exist than are listed; the oldest are listed first.
            </Typography>
          )}
        </>
      )}
    </Panel>
  );
}
