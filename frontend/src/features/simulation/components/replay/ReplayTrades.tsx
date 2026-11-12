import {
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
import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router';
import { formatDate, formatInr, formatPercent, formatPrice } from '@/shared/lib/format';
import { ErrorState, LoadingState } from '@/shared/ui';
import { replayTradesQuery } from '../../api/replayApi';

/** The trades in booking order. */
export function ReplayTrades({ runId, active }: { runId: string; active: boolean }) {
  const trades = useQuery(replayTradesQuery(runId, active));
  return (
    <Card component="section" aria-labelledby="replay-trades-title">
      <CardContent sx={{ minWidth: 0 }}>
        <Typography id="replay-trades-title" variant="h2" component="h2" sx={{ mb: 1 }}>
          Trades
        </Typography>
        {trades.isPending && <LoadingState label="Loading the trades…" />}
        {trades.isError && <ErrorState error={trades.error} />}
        {trades.data &&
          (trades.data.trades.length === 0 ? (
            <Typography color="text.secondary">No trade is stored yet.</Typography>
          ) : (
            <TableContainer sx={{ maxHeight: 420 }}>
              <Table size="small" stickyHeader aria-label="Replay trades">
                <TableHead>
                  <TableRow>
                    <TableCell>Symbol</TableCell>
                    <TableCell>Side</TableCell>
                    <TableCell>Strategy</TableCell>
                    <TableCell>Entry</TableCell>
                    <TableCell>Exit</TableCell>
                    <TableCell align="right">Net P&amp;L</TableCell>
                    <TableCell align="right">R</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {trades.data.trades.map((t) => (
                    <TableRow key={t.tradeNo}>
                      <TableCell>
                        <Link
                          to={`/simulation/replay/${runId}/trades/${String(t.tradeNo)}`}
                          aria-label={`Chart of ${t.symbol} ${t.side}, trade ${String(t.tradeNo)}`}
                        >
                          {t.symbol}
                        </Link>
                      </TableCell>
                      <TableCell>{t.side}</TableCell>
                      <TableCell>{t.strategy}</TableCell>
                      <TableCell>{`${formatDate(t.entrySession)} at ${formatPrice(t.entryPrice)}`}</TableCell>
                      <TableCell>{t.exitSession ? formatDate(t.exitSession) : 'Open'}</TableCell>
                      <TableCell align="right">
                        {t.netPnl === null
                          ? t.grossPnl === null
                            ? 'n/a'
                            : `${formatInr(t.grossPnl)} gross`
                          : formatInr(t.netPnl)}
                      </TableCell>
                      <TableCell align="right">{t.r === null ? 'n/a' : formatPercent(t.r)}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </TableContainer>
          ))}
      </CardContent>
    </Card>
  );
}
