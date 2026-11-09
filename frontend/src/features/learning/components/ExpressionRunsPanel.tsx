import { Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { DataTable, formatCount, formatRate, reportedMetric, type Column } from '@/shared/analytics';
import type { Schemas } from '@/shared/api';
import { formatIstDate } from '@/shared/lib/format';
import { ErrorState, LoadingState } from '@/shared/ui';
import { expressionRunsQuery } from '../api/p14Queries';
import { Panel } from './Panel';

type Run = Schemas['ExpressionRunSummary'];

const words = (s: string) => s.replaceAll('_', ' ');

const columns: Column<Run>[] = [
  {
    kind: 'text',
    id: 'run',
    header: 'Run',
    render: (r) => `${r.profileHash.slice(0, 8)} · ${formatIstDate(r.createdAt)}`,
  },
  {
    kind: 'metric',
    id: 'trades',
    header: 'Trades',
    metric: (r) => ({ kind: 'value', value: r.trades }),
    format: formatCount,
  },
  {
    kind: 'metric',
    id: 'paired',
    header: 'Paired',
    metric: (r) => ({ kind: 'value', value: r.paired }),
    format: formatCount,
  },
  {
    kind: 'text',
    id: 'unpaired',
    header: 'Unpaired, by reason',
    render: (r) =>
      r.unpaired.length === 0
        ? 'none'
        : r.unpaired.map((u) => `${words(u.reason)} ${formatCount(u.trades)}`).join('; '),
  },
  {
    kind: 'metric',
    id: 'fallback',
    header: 'Model price',
    metric: (r) => reportedMetric(r.fallbackShare),
    format: formatRate,
  },
  {
    kind: 'metric',
    id: 'derived',
    header: 'Derived expiry',
    metric: (r) => reportedMetric(r.expiryDerivedShare),
    format: formatRate,
  },
  {
    kind: 'metric',
    id: 'spread',
    header: 'Recent-archive spread',
    metric: (r) => reportedMetric(r.spreadFromRecentArchiveShare),
    format: formatRate,
  },
];

/** The newest expression runs: what each paired and lost, and why. Counts and shares only, never a price or return. */
export function ExpressionRunsPanel() {
  const query = useQuery(expressionRunsQuery);
  const data = query.data;
  const empty = data && (data.state === 'unavailable' || data.items.length === 0);
  return (
    <Panel title="Expression runs">
      {query.isPending && <LoadingState label="Loading expression runs" />}
      {query.isError && <ErrorState error={query.error} />}
      {data && (
        <>
          <DataTable
            label="Expression runs"
            columns={columns}
            rows={data.items}
            rowKey={(r) => r.id}
            status={
              empty ? { kind: 'empty', reason: 'no expression run stored yet (xd options expression-eval)' } : undefined
            }
          />
          {!empty && (
            <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
              Counts are of the whole run, hold-out included. The newest 20 runs are listed.
            </Typography>
          )}
        </>
      )}
    </Panel>
  );
}
