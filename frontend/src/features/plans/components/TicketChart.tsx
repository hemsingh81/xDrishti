import { Box, Button, Stack, Table, TableBody, TableCell, TableHead, TableRow, Typography } from '@mui/material';
import { useTheme } from '@mui/material/styles';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { formatDate, formatPrice } from '@/shared/lib/format';
import { ErrorState, LoadingState } from '@/shared/ui';
import { ticketChartQuery, type TicketChart as Chart } from '../api/planQueries';
import { chartLines, priceDomain } from '../lib/chartLines';

const W = 640;
const H = 240;
const PAD = { left: 6, right: 190, top: 8, bottom: 8 };

function Candles({ chart }: { chart: Chart }) {
  const theme = useTheme();
  const lines = chartLines(chart.levels);
  const [lo, hi] = priceDomain(chart.bars, lines);
  const y = (v: number) => PAD.top + ((hi - v) / (hi - lo)) * (H - PAD.top - PAD.bottom);
  const slot = (W - PAD.left - PAD.right) / chart.bars.length;
  const ink = theme.palette.text.primary;
  return (
    <svg
      viewBox={`0 0 ${String(W)} ${String(H)}`}
      role="img"
      aria-label={`${chart.symbol} daily bars up to ${chart.session}`}
      style={{ width: '100%', height: 'auto' }}
    >
      {chart.bars.map((b, i) => {
        const x = PAD.left + slot * (i + 0.5);
        const up = b.c >= b.o;
        return (
          <g key={b.t} stroke={ink} strokeWidth={1}>
            <line x1={x} x2={x} y1={y(b.h)} y2={y(b.l)} />
            {/* a rising bar is hollow, a falling bar is filled: the shape says it, not the colour */}
            <rect
              x={x - slot * 0.3}
              width={slot * 0.6}
              y={y(Math.max(b.o, b.c))}
              height={Math.max(1, Math.abs(y(b.o) - y(b.c)))}
              fill={up ? theme.palette.background.paper : ink}
            />
          </g>
        );
      })}
      {lines.map((l) => (
        <g key={l.key} stroke={ink}>
          <line
            x1={PAD.left}
            x2={W - PAD.right}
            y1={y(l.value)}
            y2={y(l.value)}
            strokeWidth={1.25}
            strokeDasharray={l.key === 'entry' ? '5 4' : l.key === 'stop' ? '2 3' : undefined}
          />
          <text x={W - PAD.right + 6} y={y(l.value) + 4} fontSize={11} fill={ink} stroke="none">
            {`${l.label} ${formatPrice(l.value)}`}
          </text>
        </g>
      ))}
    </svg>
  );
}

function BarsTable({ chart }: { chart: Chart }) {
  return (
    <Table size="small" aria-label={`Last bars of ${chart.symbol}`}>
      <TableHead>
        <TableRow>
          {['Date', 'Open', 'High', 'Low', 'Close', 'Volume'].map((h) => (
            <TableCell key={h}>{h}</TableCell>
          ))}
        </TableRow>
      </TableHead>
      <TableBody>
        {chart.bars.slice(-10).map((b) => (
          <TableRow key={b.t}>
            <TableCell>{formatDate(b.t)}</TableCell>
            <TableCell>{formatPrice(b.o)}</TableCell>
            <TableCell>{formatPrice(b.h)}</TableCell>
            <TableCell>{formatPrice(b.l)}</TableCell>
            <TableCell>{formatPrice(b.c)}</TableCell>
            <TableCell>{b.v}</TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}

/**
 * The daily bars a ticket was planned on, as plain SVG (no chart library; a new one is the owner's decision): candles up to the planning session, and a labelled line for each number the ticket carries.
 * Without numbers the rule texts are printed instead. Read only.
 */
export default function TicketChart({ planId, itemId }: { planId: number; itemId: number }) {
  const query = useQuery(ticketChartQuery(planId, itemId));
  const [table, setTable] = useState(false);
  if (query.isPending) return <LoadingState label="Loading chart…" />;
  if (query.isError) return <ErrorState error={query.error} />;
  const chart = query.data;
  if (chart.resolution !== 'exact' || chart.bars.length === 0) {
    return (
      <Typography variant="body2">
        {chart.resolution === 'ambiguous'
          ? 'No chart: the symbol matches more than one active instrument.'
          : chart.resolution === 'not_found'
            ? 'No chart: the symbol matches no active instrument.'
            : 'No chart: no daily bars are stored for this instrument.'}
      </Typography>
    );
  }
  const lines = chartLines(chart.levels);
  return (
    <Stack spacing={1}>
      <Typography variant="caption" color="text.secondary">
        {chart.barCount} daily bars up to {formatDate(chart.session)}, adjusted as of that session
        {chart.barCount < 60 ? ' (fewer than 60 sessions of history exist; nothing is padded)' : ''}. Hollow candle:
        closed up; filled: closed down.
      </Typography>
      <Candles chart={chart} />
      {lines.length > 0 ? (
        <Box component="ul" aria-label="Price lines" sx={{ m: 0, pl: 2 }}>
          {lines.map((l) => (
            <li key={l.key}>
              <Typography variant="caption">
                {l.label}: {formatPrice(l.value)}
              </Typography>
            </li>
          ))}
        </Box>
      ) : (
        <Typography variant="caption">
          Levels are rules, not numbers, on this ticket. Trigger: {chart.rules.trigger} · Stop: {chart.rules.stop} ·
          Target: {chart.rules.target}
        </Typography>
      )}
      {chart.levels.basis && (
        <Typography variant="caption" color="text.secondary">
          {chart.levels.basis[0]?.toUpperCase()}
          {chart.levels.basis.slice(1)}. No order is placed.
        </Typography>
      )}
      <div>
        <Button
          size="small"
          aria-expanded={table}
          onClick={() => {
            setTable((v) => !v);
          }}
        >
          {table ? 'Hide table' : 'Show as table'}
        </Button>
      </div>
      {table && <BarsTable chart={chart} />}
    </Stack>
  );
}
