import { Box, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { DataTable, formatCount, type Column } from '@/shared/analytics';
import type { components } from '@/shared/api/schema';
import { formatDate } from '@/shared/lib/format';
import { ErrorState, LoadingState } from '@/shared/ui';
import { filingCoverageQuery } from '../../api/filingsQueries';
import { reasonText } from './words';

type Coverage = components['schemas']['FilingCoverageView'];
type Quarter = components['schemas']['FilingQuarterCoverage'];

interface Row {
  key: string;
  kind: string;
  /** Null when nothing of the kind is stored in the range. */
  quarter: Quarter | null;
  without: number;
}

const nothing = { kind: 'unavailable', reason: 'nothing of this kind is stored in the range' } as const;

const columns: readonly Column<Row>[] = [
  { kind: 'text', id: 'kind', header: 'Kind', render: (r) => r.kind },
  {
    kind: 'text',
    id: 'quarter',
    header: 'Quarter ending',
    render: (r) => (r.quarter ? formatDate(r.quarter.quarterEnd) : 'no quarter'),
  },
  {
    kind: 'metric',
    id: 'ok',
    header: 'With a broadcast time',
    metric: (r) => (r.quarter ? { kind: 'value', value: r.quarter.ok } : nothing),
    format: formatCount,
  },
  {
    kind: 'text',
    id: 'unavailable',
    header: 'Without a broadcast time',
    render: (r) =>
      !r.quarter
        ? 'unavailable'
        : r.quarter.unavailable.length === 0
          ? 'none'
          : r.quarter.unavailable.map((u) => `${reasonText(u.reason)}: ${String(u.count)}`).join('; '),
  },
  {
    kind: 'metric',
    id: 'superseded',
    header: 'Superseded',
    metric: (r) => (r.quarter ? { kind: 'value', value: r.quarter.superseded } : nothing),
    format: formatCount,
  },
  {
    kind: 'metric',
    id: 'out-of-order',
    header: 'Out of order',
    metric: (r) => (r.quarter ? { kind: 'value', value: r.quarter.outOfOrder } : nothing),
    format: formatCount,
  },
  {
    kind: 'metric',
    id: 'without',
    header: 'Instruments without a filing',
    metric: (r) => ({ kind: 'value', value: r.without }),
    format: formatCount,
  },
];

function rowsOf(data: Coverage): Row[] {
  return data.kinds.flatMap((k): Row[] =>
    k.state !== 'value' || k.quarters.length === 0
      ? [{ key: k.kind, kind: k.kind, quarter: null, without: k.instrumentsWithout }]
      : k.quarters.map((q) => ({
          key: `${k.kind}-${q.quarterEnd}`,
          kind: k.kind,
          quarter: q,
          without: k.instrumentsWithout,
        })),
  );
}

/** Counts of the stored filings per kind and quarter: with a broadcast time, without one (by reason), superseded, out of order. */
export function FilingCoverageSection() {
  const query = useQuery(filingCoverageQuery);
  return (
    <Box component="section" aria-labelledby="filing-coverage-title">
      <Typography id="filing-coverage-title" variant="h3" component="h2" sx={{ mb: 1 }}>
        Filing coverage
      </Typography>
      {query.isPending && <LoadingState label="Loading filing coverage" />}
      {query.isError && <ErrorState error={query.error} />}
      {query.data && (
        <>
          <DataTable
            label="Filing coverage per kind and quarter"
            columns={columns}
            rows={rowsOf(query.data)}
            rowKey={(r) => r.key}
            status={query.data.kinds.length === 0 ? { kind: 'empty', reason: 'no filings stored yet' } : undefined}
            evidence={{
              n: query.data.kinds.reduce((sum, k) => sum + k.quarters.reduce((s, q) => s + q.ok, 0), 0),
              range: { from: query.data.from, to: query.data.to },
              basis: 'unknown',
              basisText: `Counts of stored filings, not returns; ${String(query.data.trackedInstruments)} tracked instruments`,
            }}
          />
        </>
      )}
    </Box>
  );
}
