import { Box, Typography } from '@mui/material';
import { DataTable, formatR, type Column } from '@/shared/analytics';
import { formatInr, formatIstDayAndTime } from '@/shared/lib/format';
import type { PaperClosedTrade } from '../api/paperQueries';

const signedInr = (value: number) => `${value > 0 ? '+' : ''}${formatInr(value)}`;

const columns: readonly Column<PaperClosedTrade>[] = [
  { kind: 'text', id: 'symbol', header: 'Symbol', render: (t) => t.symbol },
  { kind: 'text', id: 'side', header: 'Side', render: (t) => t.side },
  { kind: 'text', id: 'qty', header: 'Qty', align: 'right', render: (t) => t.qty },
  { kind: 'text', id: 'entry', header: 'Entered', render: (t) => formatIstDayAndTime(t.entryAt, { weekday: false }) },
  { kind: 'text', id: 'exit', header: 'Exited', render: (t) => formatIstDayAndTime(t.exitAt, { weekday: false }) },
  { kind: 'text', id: 'gross', header: 'Gross', align: 'right', render: (t) => signedInr(t.gross) },
  {
    kind: 'text',
    id: 'charges',
    header: 'Charges',
    align: 'right',
    render: (t) => (t.charges === null ? 'none stored' : formatInr(t.charges)),
  },
  { kind: 'text', id: 'result', header: 'After stored charges', align: 'right', render: (t) => signedInr(t.result) },
  { kind: 'text', id: 'r', header: 'R', align: 'right', render: (t) => formatR(t.r) },
];

/** Closed trades, newest exit first. */
export function ClosedTable({ closed }: { closed: readonly PaperClosedTrade[] }) {
  return (
    <Box component="section" aria-labelledby="paper-closed-heading">
      <Typography variant="h2" id="paper-closed-heading" sx={{ fontSize: '1.1rem', mb: 1 }}>
        Closed trades
      </Typography>
      <DataTable
        label="Closed paper trades"
        columns={columns}
        rows={closed}
        rowKey={(t) => `${t.symbol}-${t.entryAt}-${t.exitAt}`}
      />
    </Box>
  );
}
