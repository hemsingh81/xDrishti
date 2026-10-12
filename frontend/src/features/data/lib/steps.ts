import type { AggregatesOverview, CsvOverview, DhanOverview, GapsOverview } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';
import { gapsSummary } from './gaps';

/** The key figure of the CSV step in the strip: "214 of 214 files". */
export const csvSummary = (csv: CsvOverview): string =>
  csv.folderExists
    ? `${formatNumber(csv.filesLoaded)} of ${formatNumber(csv.filesTotal)} files`
    : 'CSV folder not found';

/** The key figures of the Dhan step in the strip: "4,500 bars added · 12 open · 3 unfillable". */
export function dhanSummary(dhan: DhanOverview, gaps: GapsOverview): string {
  const fetched =
    dhan.lastFetchBars !== null ? `${formatNumber(dhan.lastFetchBars)} bars added` : 'Nothing fetched yet';
  return `${fetched} · ${gapsSummary(gaps)}`;
}

/** The key figure of the aggregates step in the strip: "8 timeframes". */
export function aggregatesSummary({ timeframes }: AggregatesOverview): string {
  if (timeframes.length === 0) return 'No aggregates built yet';
  return `${formatNumber(timeframes.length)} ${timeframes.length === 1 ? 'timeframe' : 'timeframes'}`;
}

/** What each step counts while it runs ("107 of 214 files"). */
export const stepUnits = { csv: 'files', dhan: 'instruments', aggregate: 'instruments' } as const;
