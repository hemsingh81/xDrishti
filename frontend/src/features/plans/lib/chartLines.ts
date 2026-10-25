import type { TicketChart } from '../api/planQueries';

export interface ChartLine {
  key: 'entry' | 'stop' | 'target';
  label: string;
  value: number;
}

/** One labelled price line per non-null number the ticket carries (never a line at 0 for a missing one). Pure. */
export function chartLines(levels: TicketChart['levels']): ChartLine[] {
  if (levels.source !== 'ticket') return [];
  const lines: ChartLine[] = [];
  if (levels.entryRef !== null)
    lines.push({
      key: 'entry',
      label: `Entry reference, close of ${levels.session ?? 'the session'}`,
      value: levels.entryRef,
    });
  if (levels.stop !== null)
    lines.push({
      key: 'stop',
      label: levels.stopAnchor === 'level' ? 'Stop level' : 'Stop, indicative from the close',
      value: levels.stop,
    });
  if (levels.target !== null) lines.push({ key: 'target', label: 'Target', value: levels.target });
  return lines;
}

/** The price range the chart shows: every bar's range and every line, with a little room. */
export function priceDomain(bars: TicketChart['bars'], lines: readonly ChartLine[]): [number, number] {
  const values = [...bars.flatMap((b) => [b.h, b.l]), ...lines.map((l) => l.value)];
  const lo = Math.min(...values);
  const hi = Math.max(...values);
  const room = (hi - lo || hi || 1) * 0.05;
  return [lo - room, hi + room];
}
