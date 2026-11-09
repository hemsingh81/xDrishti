import { useQuery } from '@tanstack/react-query';
import { DataTable, formatCount, NonValue, reportedMetric, type Column } from '@/shared/analytics';
import type { Schemas } from '@/shared/api';
import { formatIstDate } from '@/shared/lib/format';
import { ErrorState, LoadingState } from '@/shared/ui';
import { optionSummaryQuery } from '../api/qualityQueries';

type Row = Schemas['UnderlyingArchiveSummary'];

const words = (s: string) => s.replaceAll('_', ' ');

function DateCell({ r }: { r: Schemas['ReportedDate'] }) {
  return r.state === 'ok' && r.value ? (
    <>{formatIstDate(r.value)}</>
  ) : (
    <NonValue state={{ kind: 'unavailable', reason: words(r.reason ?? 'not reported') }} />
  );
}

const count = (id: string, header: string, pick: (r: Row) => Schemas['ReportedCount']): Column<Row> => ({
  kind: 'metric',
  id,
  header,
  metric: (r) => reportedMetric(pick(r)),
  format: formatCount,
});

const columns: Column<Row>[] = [
  { kind: 'text', id: 'underlying', header: 'Underlying', render: (r) => r.underlying },
  count('recorded', 'Recorded sessions', (r) => r.recordedSessions),
  count('usable', 'Usable sessions', (r) => r.usableSessions),
  { kind: 'text', id: 'first', header: 'First', render: (r) => <DateCell r={r.firstDate} /> },
  { kind: 'text', id: 'last', header: 'Last', render: (r) => <DateCell r={r.lastDate} /> },
  {
    kind: 'text',
    id: 'gaps',
    header: 'Gaps, by outcome',
    render: (r) =>
      r.gaps.state !== 'ok' ? (
        <NonValue state={{ kind: 'unavailable', reason: words(r.gaps.reason ?? 'not reported') }} />
      ) : r.gaps.outcomes.length === 0 ? (
        'none'
      ) : (
        r.gaps.outcomes
          .map((o) => `${words(o.outcome)}${o.reason ? ` (${words(o.reason)})` : ''} ${formatCount(o.sessions)}`)
          .join('; ')
      ),
  },
  {
    kind: 'text',
    id: 'backfill',
    header: 'Backfill keys',
    render: (r) =>
      r.backfill.state !== 'ok' ? (
        <NonValue state={{ kind: 'unavailable', reason: words(r.backfill.reason ?? 'not reported') }} />
      ) : (
        `done ${formatCount(r.backfill.done)}; empty ${formatCount(r.backfill.empty)}; no data ${formatCount(r.backfill.noData)}; given up ${formatCount(r.backfill.givenUp)}`
      ),
  },
  count('spread', 'Spread-model sessions', (r) => r.spreadModelSessions),
];

/** What the option archive holds per underlying and where it has holes. Counts and dates only; never a price, IV or Greek. */
export function OptionsSummary() {
  const query = useQuery(optionSummaryQuery);
  if (query.isPending) return <LoadingState label="Loading the options archive summary" />;
  if (query.isError) return <ErrorState error={query.error} />;
  return (
    <DataTable
      label="Options archive summary"
      columns={columns}
      rows={query.data.underlyings}
      rowKey={(r) => r.underlying}
      status={query.data.underlyings.length === 0 ? { kind: 'empty', reason: 'nothing recorded yet' } : undefined}
    />
  );
}
