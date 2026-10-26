import type { ReportQuery } from './reportFilter';

/** What a cell is made of: the dimension and the value of each part (a value of `null` was not recorded). */
export interface CellPart {
  dimension: string;
  value: string | null;
}

/** The dimensions that are also filters of a report, by the name of the filter. */
const asFilter: Readonly<Record<string, keyof ReportQuery>> = {
  basket: 'basket',
  strategy: 'strategy',
  timeframe: 'timeframe',
  side: 'side',
  style: 'style',
  exit: 'exit',
};

/**
 * The address of the cell detail for a cell: the report's own filters plus one filter for each part of the cell. A cell that cannot be
 * said as filters (a month, a weekday, an instrument, a value not recorded) has no address, because a link with a wider filter would
 * show other trades under its name; the caller then says so.
 */
export function cellHref(query: ReportQuery, parts: readonly CellPart[], title: string): string | null {
  const search = new URLSearchParams(query);
  for (const part of parts) {
    const name = asFilter[part.dimension];
    if (name === undefined || part.value === null) return null;
    search.set(name, part.value);
  }
  search.set('cell', title);
  return `/reports/cell?${search.toString()}`;
}
