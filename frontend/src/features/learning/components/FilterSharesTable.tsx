import { Typography } from '@mui/material';
import { DataTable, NonValue, formatCount, formatRate, metricOf, type Column } from '@/shared/analytics';
import { filterShares, type ReasonShare } from '../lib/extensions';
import type { ExtensionEvaluation } from '../api/extensionQueries';

interface Row {
  key: string;
  label: string;
  trades: number | null;
  share: number | null;
}

const columns: readonly Column<Row>[] = [
  { kind: 'text', id: 'class', header: 'Trades', render: (r) => r.label },
  { kind: 'metric', id: 'n', header: 'Count', metric: (r) => metricOf(r.trades, 'not stored'), format: formatCount },
  { kind: 'metric', id: 'share', header: 'Share', metric: (r) => metricOf(r.share, 'not stored'), format: formatRate },
];

const reasonLabel = (r: ReasonShare) => `unavailable: ${r.reason.replaceAll('_', ' ')}`;

/** Skipped, taken and unavailable (by reason) of a filter record, with the share lost to no_upcoming_entry on its own line. */
export function FilterSharesTable({ evaluation }: { evaluation: ExtensionEvaluation }) {
  const s = filterShares(evaluation);
  const rows: Row[] = [
    { key: 'skipped', label: 'skipped (inside the window)', trades: s.skipped, share: s.skippedShare },
    { key: 'taken', label: 'taken (outside the window)', trades: s.taken, share: s.takenShare },
    { key: 'unavailable', label: 'unavailable (all reasons)', trades: s.unavailable, share: s.unavailableShare },
    ...s.byReason.map((r) => ({ key: r.reason, label: reasonLabel(r), trades: r.trades, share: r.share })),
  ];
  return (
    <>
      <DataTable label="Filter shares" columns={columns} rows={rows} rowKey={(r) => r.key} />
      <Typography variant="body2" sx={{ mt: 1 }}>
        Share lost to no_upcoming_entry:{' '}
        {s.lostToNoUpcomingEntry === null ? (
          <NonValue state={{ kind: 'unavailable', reason: 'not stored in this record' }} />
        ) : (
          <strong>{formatRate(s.lostToNoUpcomingEntry)}</strong>
        )}
      </Typography>
    </>
  );
}
