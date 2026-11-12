import CloseIcon from '@mui/icons-material/Close';
import { Drawer, IconButton, Stack, Typography } from '@mui/material';
import { queryOptions, useQuery } from '@tanstack/react-query';
import { api, unwrap, type Schemas } from '@/shared/api';
import { istDayIso } from '@/shared/lib/format';
import { ErrorState, LoadingState } from '@/shared/ui';
import { PriceChart, type PriceEvent } from '@/shared/ui/chart';

type Trade = Schemas['ReplayBookTradeView'];

const idOf = (route: string) => /instruments\/([0-9a-f-]{36})\/bars/i.exec(route)?.[1] ?? '';

const barsQuery = (route: string) =>
  queryOptions({
    queryKey: ['simulation', 'replay', 'bars', route],
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/data/instruments/{id}/bars', {
          params: { path: { id: idOf(route) }, query: { timeframe: '1D', take: 1000 } },
          signal,
        }),
      ),
  });

/** The day bars of the instrument (the trade's `barsRoute`), with the entry and the exit marked where the bars reach. */
export function TradeBarsDrawer({ trade, onClose }: { trade: Trade | null; onClose: () => void }) {
  const route = trade?.barsRoute ?? '';
  const query = useQuery({ ...barsQuery(route), enabled: route !== '' });
  const bars = (query.data?.items ?? []).map((b) => ({
    t: Math.floor(new Date(b.ts).getTime() / 1000),
    o: b.open,
    h: b.high,
    l: b.low,
    c: b.close,
    v: b.volume,
  }));
  const at = (day: string | null) => bars.find((b) => istDayIso(b.t * 1000) === day);
  const events: PriceEvent[] = [];
  if (trade) {
    const entry = at(trade.entrySession);
    const exit = at(trade.exitSession);
    const side = trade.side.toUpperCase() === 'SELL' ? 'SELL' : 'BUY';
    const other = side === 'BUY' ? 'SELL' : 'BUY';
    if (entry)
      events.push({
        id: 'entry',
        t: entry.t,
        price: trade.entryPrice,
        kind: 'entry',
        side,
        text: 'Entry',
        announce: `Entry at ${trade.entryPrice}`,
      });
    if (exit && trade.exitSession)
      events.push({
        id: 'exit',
        t: exit.t,
        price: exit.c,
        kind: 'exit',
        side: other,
        text: 'Exit',
        announce: `Exit on ${trade.exitSession}`,
      });
  }
  return (
    <Drawer
      anchor="right"
      open={trade !== null}
      onClose={onClose}
      slotProps={{ paper: { sx: { width: { xs: '100%', md: 640 }, p: 2 } } }}
    >
      <Stack direction="row" sx={{ alignItems: 'center', justifyContent: 'space-between', mb: 1 }}>
        <Typography variant="h2" component="h2">
          {trade ? `${trade.symbol} day bars` : 'Day bars'}
        </Typography>
        <IconButton aria-label="Close the day bars" onClick={onClose} sx={{ minWidth: 44, minHeight: 44 }}>
          <CloseIcon />
        </IconButton>
      </Stack>
      {query.isPending && route !== '' && <LoadingState label="Loading the bars…" />}
      {query.isError && <ErrorState error={query.error} />}
      {query.data && (
        <PriceChart
          label={`${query.data.symbol} day bars`}
          bars={bars}
          events={events}
          dayOnly
          height={320}
          empty="No bars are stored."
        />
      )}
    </Drawer>
  );
}
