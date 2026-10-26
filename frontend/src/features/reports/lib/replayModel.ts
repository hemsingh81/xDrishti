import { formatPrice, formatShare } from '@/shared/lib/format';
import type { PriceBar, PriceEvent, PriceLine } from '@/shared/ui/chart';
import { clock } from '@/shared/ui/chart/priceChartModel';
import type { TradeReplay } from '../api/reportQueries';

const seconds = (iso: string): number => Math.floor(new Date(iso).getTime() / 1000);
const percent = (share: number): string => `${formatShare(share * 100).replace(/\.0$/, '')}%`;
const kindWord = { entry: 'Entry', add: 'Add', exit: 'Exit' } as const;

/** The bars for the chart. */
export const replayBars = (view: TradeReplay): PriceBar[] =>
  view.chart.bars.map((b) => ({ t: b.t, o: b.o, h: b.h, l: b.l, c: b.c, v: b.v }));

/** The levels: stop, target (none when the trade has none) and the entry, each with its text label on the axis. */
export function replayLines(view: TradeReplay): PriceLine[] {
  const { stopPrice, targetPrice, entryPrice } = view.trade;
  const lines: PriceLine[] = [
    { id: 'entry', kind: 'entry', price: entryPrice, title: `Entry ${formatPrice(entryPrice)}` },
  ];
  lines.push({ id: 'stop', kind: 'stop', price: stopPrice, title: `Stop ${formatPrice(stopPrice)}` });
  if (targetPrice !== null)
    lines.push({ id: 'target', kind: 'target', price: targetPrice, title: `Target ${formatPrice(targetPrice)}` });
  return lines;
}

/** One row of the events table and one marker: a trade without legs has its entry and its exit; one with legs, every leg. */
export interface ReplayEvent extends PriceEvent {
  /** Seconds, share of the position (1 = all), and the reason of an exit. */
  share: number;
  reason: string;
  /** The words under the row, such as "gap through the stop 415.00". */
  note: string | null;
}

/** "exit at 412.30, gap through the stop 415.00": a stop whose exit price lies beyond the stored stop. */
export function gapNote(view: TradeReplay): string | null {
  const { side, exitReason, exitPrice, stopPrice } = view.trade;
  if (exitReason !== 'stop') return null;
  const beyond = side === 'BUY' ? exitPrice < stopPrice : exitPrice > stopPrice;
  return beyond ? `exit at ${formatPrice(exitPrice)}, gap through the stop ${formatPrice(stopPrice)}` : null;
}

export function replayEvents(view: TradeReplay): ReplayEvent[] {
  const side = view.trade.side === 'SELL' ? 'SELL' : 'BUY';
  const gap = gapNote(view);
  const legs =
    view.legs.length > 0
      ? view.legs.map((l, i) => ({
          at: l.at,
          kind: l.kind as 'entry' | 'add' | 'exit',
          price: l.price,
          share: l.share,
          reason: l.reason,
          id: `leg-${String(i)}`,
        }))
      : [
          {
            at: view.trade.entryAt,
            kind: 'entry' as const,
            price: view.trade.entryPrice,
            share: 1,
            reason: '',
            id: 'entry',
          },
          {
            at: view.trade.exitAt,
            kind: 'exit' as const,
            price: view.trade.exitPrice,
            share: 1,
            reason: view.trade.exitReason,
            id: 'exit',
          },
        ];
  const last = legs.length - 1;
  return legs.map((l, i): ReplayEvent => {
    const t = seconds(l.at);
    const word = kindWord[l.kind];
    const note = l.kind === 'exit' && i === last ? gap : null;
    return {
      id: l.id,
      t,
      price: l.price,
      kind: l.kind,
      side,
      share: l.share,
      reason: l.reason,
      note,
      text: `${word} ${percent(l.share)}`,
      announce: `${word}, ${clock(t)}, price ${formatPrice(l.price)}, share ${percent(l.share)}`,
    };
  });
}

/** The ladder of timeframes, for the "use a larger timeframe" offer after a refused window. */
export const replayTimeframes = ['1m', '5m', '15m', '1h', '1D'] as const;
export type ReplayTimeframe = (typeof replayTimeframes)[number];
export const isReplayTimeframe = (value: string | null): value is ReplayTimeframe =>
  replayTimeframes.some((t) => t === value);
export const largerTimeframe = (t: ReplayTimeframe): ReplayTimeframe | null =>
  replayTimeframes[replayTimeframes.indexOf(t) + 1] ?? null;
