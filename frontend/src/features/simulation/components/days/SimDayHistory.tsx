import {
  Box,
  Button,
  Card,
  CardContent,
  Chip,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Typography,
} from '@mui/material';
import { useInfiniteQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { formatDate, formatInr } from '@/shared/lib/format';
import { ErrorState, LoadingState } from '@/shared/ui';
import { TimeSeriesChart, type ChartSeries } from '@/shared/ui/chart';
import { simDayListQuery, type SimDays } from '../../api/simDaysApi';
import { SimDayDrawer } from './SimDayDrawer';

/** The stored days (newest first) and the equity over them; a day opens its drawer, from where it can be played. */
export function SimDayHistory({ run, onPlay }: { run: SimDays; onPlay: (day: string) => void }) {
  const list = useInfiniteQuery(simDayListQuery(run.id));
  const [open, setOpen] = useState<string | null>(null);
  const days = list.data?.pages.flatMap((p) => p.items) ?? [];
  const series: ChartSeries[] = [
    { id: 'equity', name: 'Equity', color: 'accent', data: days.map((d) => [d.session, d.equityClose] as const) },
  ];
  return (
    <Card component="section" aria-labelledby="sim-history-title">
      <CardContent sx={{ display: 'grid', gap: 1.5, minWidth: 0 }}>
        <Typography id="sim-history-title" variant="h2" component="h2">
          Day history
        </Typography>
        {list.isPending && <LoadingState label="Loading the days…" />}
        {list.isError && <ErrorState error={list.error} />}
        {list.data && days.length === 0 && <Typography color="text.secondary">No day is stored yet.</Typography>}
        {days.length > 0 && (
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
                    <TableCell align="right">Equity</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {[...days].reverse().map((d) => (
                    <TableRow key={d.session}>
                      <TableCell>
                        <Button
                          size="small"
                          onClick={() => {
                            setOpen(d.session);
                          }}
                          sx={{ minHeight: 44, textTransform: 'none' }}
                        >
                          {formatDate(d.session)}
                        </Button>
                        {d.seenHoldOut && <Chip size="small" label="hold-out" sx={{ ml: 0.5 }} />}
                      </TableCell>
                      <TableCell align="right">{d.trades}</TableCell>
                      <TableCell align="right">{d.picks}</TableCell>
                      <TableCell align="right">{formatInr(d.equityClose)}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </TableContainer>
            {list.hasNextPage && (
              <Button
                onClick={() => {
                  void list.fetchNextPage();
                }}
                disabled={list.isFetchingNextPage}
                sx={{ minHeight: 44, justifySelf: 'start' }}
              >
                Load later days
              </Button>
            )}
          </>
        )}
        <SimDayDrawer
          runId={run.id}
          session={open}
          onPlay={(day) => {
            setOpen(null);
            onPlay(day);
          }}
          onClose={() => {
            setOpen(null);
          }}
        />
      </CardContent>
    </Card>
  );
}
