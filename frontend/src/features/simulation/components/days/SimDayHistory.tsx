import {
  Box,
  Button,
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
import { useQueries } from '@tanstack/react-query';
import { useState } from 'react';
import { formatDate, formatInr, formatPercent } from '@/shared/lib/format';
import { ErrorState, LoadingState } from '@/shared/ui';
import { TimeSeriesChart, type ChartSeries } from '@/shared/ui/chart';
import { simDayQuery, type SimDays, type SimDaySnapshot } from '../../api/simDaysApi';
import { dayBefore, weekdaysBack } from '../../lib/simDaysForm';
import { SimDayDrawer } from './SimDayDrawer';

/** How many recent weekdays are read: there is no list of the stored days, so each weekday is asked (a holiday is a 404). */
export const historyDays = 30;

/** The stored days (newest first) and the equity over them; a day opens its drawer. */
export function SimDayHistory({ run }: { run: SimDays }) {
  const last = run.phase === 'complete' ? run.cursor : dayBefore(run.cursor);
  const dates = run.daysDone === 0 ? [] : weekdaysBack(run.start, last, historyDays);
  const days = useQueries({ queries: dates.map((date) => simDayQuery(run.id, date, true)) });
  const [open, setOpen] = useState<SimDaySnapshot | null>(null);
  const stored = days.flatMap((d) => (d.data?.stored ? [d.data] : []));
  const pending = days.some((d) => d.isPending);
  const error = days.find((d) => d.isError)?.error;
  const series: ChartSeries[] = [
    {
      id: 'equity',
      name: 'Equity',
      color: 'accent',
      data: [...stored].reverse().map((d) => [d.session, d.equityClose] as const),
    },
  ];
  return (
    <Card component="section" aria-labelledby="sim-history-title">
      <CardContent sx={{ display: 'grid', gap: 1.5, minWidth: 0 }}>
        <Typography id="sim-history-title" variant="h2" component="h2">
          Day history
        </Typography>
        {pending && <LoadingState label="Loading the days…" />}
        {error && <ErrorState error={error} />}
        {!pending && stored.length === 0 && <Typography color="text.secondary">No day is stored yet.</Typography>}
        {stored.length > 0 && (
          <>
            <Box sx={{ minWidth: 0 }}>
              <TimeSeriesChart
                label="Simulation equity"
                series={series}
                valueFormatter={formatInr}
                height={240}
                empty="No day stored yet."
              />
            </Box>
            <TableContainer sx={{ maxHeight: 420 }}>
              <Table size="small" stickyHeader aria-label="Stored days">
                <TableHead>
                  <TableRow>
                    <TableCell>Day</TableCell>
                    <TableCell align="right">Trades</TableCell>
                    <TableCell align="right">Picks</TableCell>
                    <TableCell align="right">Blocked</TableCell>
                    <TableCell align="right">Open</TableCell>
                    <TableCell align="right">Equity</TableCell>
                    <TableCell align="right">Drawdown</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {stored.map((d) => (
                    <TableRow key={d.session}>
                      <TableCell>
                        <Button
                          size="small"
                          onClick={() => {
                            setOpen(d);
                          }}
                          sx={{ minHeight: 44, textTransform: 'none' }}
                        >
                          {formatDate(d.session)}
                        </Button>
                      </TableCell>
                      <TableCell align="right">{d.trades.length}</TableCell>
                      <TableCell align="right">{d.picks.length}</TableCell>
                      <TableCell align="right">{d.blocked.length}</TableCell>
                      <TableCell align="right">{d.openPositions}</TableCell>
                      <TableCell align="right">{formatInr(d.equityClose)}</TableCell>
                      <TableCell align="right">
                        {d.drawdownPct === null ? '—' : `${formatPercent(d.drawdownPct)}%`}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </TableContainer>
            {run.daysDone > stored.length && (
              <Typography variant="body2" color="text.secondary">
                {`The latest ${stored.length} of ${run.daysDone} stored days are shown.`}
              </Typography>
            )}
          </>
        )}
        <SimDayDrawer
          day={open}
          onClose={() => {
            setOpen(null);
          }}
        />
      </CardContent>
    </Card>
  );
}
