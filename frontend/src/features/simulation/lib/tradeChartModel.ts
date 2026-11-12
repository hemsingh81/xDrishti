import { formatPrice } from '@/shared/lib/format';
import type { PriceBar, PriceEvent, PriceLine } from '@/shared/ui/chart';
import { clock } from '@/shared/ui/chart/priceChartModel';
import type { ReplayTradeChart } from '../api/replayApi';

export const tradeTimeframes = ['1m', '5m', '15m', '1h', '1D'] as const;
export type TradeTimeframe = (typeof tradeTimeframes)[number];

const seconds = (iso: string) => Math.floor(new Date(iso).getTime() / 1000);

export const tradeBars = (view: ReplayTradeChart): PriceBar[] =>
  view.chart.bars.map((b) => ({ t: b.t, o: b.o, h: b.h, l: b.l, c: b.c, v: b.v }));

/** The entry and the stop as levels on the price axis. */
export function tradeLines(view: ReplayTradeChart): PriceLine[] {
  return view.markers
    .filter((m) => m.kind === 'entry' || m.kind === 'stop')
    .map((m) => {
      const kind = m.kind === 'stop' ? 'stop' : 'entry';
      return { id: kind, kind, price: m.price, title: `${kind === 'stop' ? 'Stop' : 'Entry'} ${formatPrice(m.price)}` };
    });
}

/** The entry and the exit as marks on the bars (the stop has no moment). The exit price is derived, and says so to a screen reader. */
export function tradeEvents(view: ReplayTradeChart): PriceEvent[] {
  const side = view.side === 'SELL' ? 'SELL' : 'BUY';
  return view.markers.flatMap((m): PriceEvent[] => {
    if (m.at === null || (m.kind !== 'entry' && m.kind !== 'exit')) return [];
    const t = seconds(m.at);
    const word = m.kind === 'entry' ? 'Entry' : 'Exit';
    const derived = m.kind === 'exit' && view.exitPriceDerived ? ' (derived)' : '';
    return [
      {
        id: m.kind,
        t,
        price: m.price,
        kind: m.kind,
        side: m.kind === 'entry' ? side : side === 'BUY' ? 'SELL' : 'BUY',
        text: word,
        announce: `${word}, ${clock(t)}, price ${formatPrice(m.price)}${derived}`,
      },
    ];
  });
}
