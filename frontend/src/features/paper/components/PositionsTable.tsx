import { Box, Typography } from '@mui/material';
import { DataTable, type Column } from '@/shared/analytics';
import { formatDate, formatInr, formatPrice } from '@/shared/lib/format';
import { toMetric } from '../lib/metric';
import type { PaperPosition } from '../api/paperQueries';

const signedInr = (value: number) => `${value > 0 ? '+' : ''}${formatInr(value)}`;

/** A stale mark is the words "mark unavailable" and the reason, never a number and never 0. */
function Mark({ mark }: { mark: PaperPosition['mark'] }) {
  if (mark.state === 'value' && mark.price !== null) {
    return (
      <>
        {formatPrice(mark.price)}
        {mark.asOf && (
          <Typography component="span" variant="caption" color="text.secondary" sx={{ display: 'block' }}>
            as of {formatDate(mark.asOf)}
          </Typography>
        )}
      </>
    );
  }
  return (
    <>
      <strong>mark unavailable</strong>
      {mark.reason && (
        <Typography component="span" variant="caption" color="text.secondary" sx={{ display: 'block' }}>
          {mark.reason}
        </Typography>
      )}
    </>
  );
}

const columns: readonly Column<PaperPosition>[] = [
  { kind: 'text', id: 'symbol', header: 'Symbol', render: (p) => p.symbol },
  { kind: 'text', id: 'side', header: 'Side', render: (p) => p.side },
  { kind: 'text', id: 'qty', header: 'Qty', align: 'right', render: (p) => p.qty },
  { kind: 'text', id: 'entry', header: 'Entry', align: 'right', render: (p) => formatPrice(p.entryPrice) },
  { kind: 'text', id: 'stop', header: 'Stop', align: 'right', render: (p) => formatPrice(p.stopPrice) },
  { kind: 'text', id: 'mark', header: 'Mark', align: 'right', render: (p) => <Mark mark={p.mark} /> },
  { kind: 'metric', id: 'unrealised', header: 'Unrealised', metric: (p) => toMetric(p.unrealised), format: signedInr },
  { kind: 'text', id: 'risk', header: 'Risk', align: 'right', render: (p) => formatInr(p.riskAmount) },
  { kind: 'text', id: 'held', header: 'Held (sessions)', align: 'right', render: (p) => p.heldSessions },
];

/** The open positions with their marks as of the last closed session. */
export function PositionsTable({ open, asOfSession }: { open: readonly PaperPosition[]; asOfSession: string }) {
  return (
    <Box component="section" aria-labelledby="paper-open-heading">
      <Typography variant="h2" id="paper-open-heading" sx={{ fontSize: '1.1rem', mb: 1 }}>
        Open positions
      </Typography>
      <Typography variant="caption" color="text.secondary" component="p" sx={{ mt: 0, mb: 1 }}>
        Marks are the close of {formatDate(asOfSession)}.
      </Typography>
      <DataTable
        label="Open paper positions"
        columns={columns}
        rows={open}
        rowKey={(p) => `${p.symbol}-${p.entryAt}`}
      />
    </Box>
  );
}
