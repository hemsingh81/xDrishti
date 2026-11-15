import {
  Box,
  Card,
  CardContent,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Typography,
} from '@mui/material';
import type { Schemas } from '@/shared/api';
import { formatInr, formatIstTime, formatPrice } from '@/shared/lib/format';

type Board = Schemas['SimBoard'];
type Event = Schemas['SimTapeEvent'];
type Day = Schemas['SimDaySnapshotView'];

const eventText = (e: Event) =>
  e.kind === 'entry'
    ? `${formatIstTime(e.at)} ${e.symbol} ${e.side} ${e.qty} entered${e.price === null ? '' : ` at ${formatPrice(e.price)}`}, stop ${formatPrice(e.stop)}`
    : `${formatIstTime(e.at)} ${e.symbol} exited${e.price === null ? '' : ` at ${formatPrice(e.price)}`}${e.reason ? ` (${e.reason})` : ''}: ${formatInr(e.grossPnl ?? 0)}`;

/** The board at the clock minute: open positions with running P&L, the day's events so far, its picks and blocked. */
export function SimLiveBoard({ board, events, day }: { board: Board; events: readonly Event[]; day: Day }) {
  return (
    <Card component="section" aria-labelledby="sim-board-title">
      <CardContent sx={{ display: 'grid', gap: 1.5, minWidth: 0 }}>
        <Typography id="sim-board-title" variant="h2" component="h2">
          {`Live board at ${formatIstTime(board.at)}`}
        </Typography>
        <Typography>
          {`Realised equity ${formatInr(board.equity)} · realised today ${formatInr(board.realised)} · running ${formatInr(board.unrealised)}`}
        </Typography>
        {board.open.length === 0 ? (
          <Typography color="text.secondary">No open position at this minute.</Typography>
        ) : (
          <TableContainer>
            <Table size="small" aria-label="Open positions">
              <TableHead>
                <TableRow>
                  <TableCell>Symbol</TableCell>
                  <TableCell align="right">Qty</TableCell>
                  <TableCell align="right">Entry</TableCell>
                  <TableCell align="right">Stop</TableCell>
                  <TableCell align="right">Last</TableCell>
                  <TableCell align="right">Running P&amp;L</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {board.open.map((p) => (
                  <TableRow key={p.tradeNo}>
                    <TableCell>{`${p.symbol} ${p.side}`}</TableCell>
                    <TableCell align="right">{p.qty}</TableCell>
                    <TableCell align="right">{formatPrice(p.entryPrice)}</TableCell>
                    <TableCell align="right">{formatPrice(p.stop)}</TableCell>
                    <TableCell align="right">
                      {p.lastPrice === null ? 'no bar yet' : formatPrice(p.lastPrice)}
                    </TableCell>
                    <TableCell align="right">{formatInr(p.runningPnl)}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </TableContainer>
        )}
        <Box component="section" aria-label="Events so far">
          <Typography variant="h3" component="h3" sx={{ fontSize: '1rem' }}>
            {`Events so far (${events.length})`}
          </Typography>
          <Box component="ul" sx={{ m: 0, pl: 2.5 }}>
            {events.map((e) => (
              <li key={`${e.kind}-${e.tradeNo}`}>{eventText(e)}</li>
            ))}
          </Box>
        </Box>
        <Box sx={{ display: 'grid', gap: 1.5, gridTemplateColumns: { xs: 'minmax(0, 1fr)', md: '1fr 1fr' } }}>
          <Box component="section" aria-label="Picks">
            <Typography variant="h3" component="h3" sx={{ fontSize: '1rem' }}>
              {`Picks for the next day (${day.picks.length})`}
            </Typography>
            <Box component="ul" sx={{ m: 0, pl: 2.5 }}>
              {day.picks.map((c) => (
                <li key={`${c.symbol}-${c.cellKey}-${c.rank}`}>{`${c.symbol} ${c.side} · rank ${c.rank}`}</li>
              ))}
            </Box>
          </Box>
          <Box component="section" aria-label="Blocked">
            <Typography variant="h3" component="h3" sx={{ fontSize: '1rem' }}>
              {`Blocked (${day.blocked.length})`}
            </Typography>
            <Box component="ul" sx={{ m: 0, pl: 2.5 }}>
              {day.blocked.map((b) => (
                <li
                  key={`${b.symbol}-${b.cellKey}`}
                >{`${b.symbol}: ${b.reason}${b.detail ? ` — ${b.detail}` : ''}`}</li>
              ))}
            </Box>
          </Box>
        </Box>
      </CardContent>
    </Card>
  );
}
