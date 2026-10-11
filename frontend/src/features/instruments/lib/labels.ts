import type { RefreshRun } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';
import type { InstrumentType, SortField } from './filters';

export const typeLabels: Readonly<Record<InstrumentType, string>> = {
  share: 'Share',
  etf: 'ETF',
  index: 'Index',
  future: 'Future',
};

export const typeLabel = (type: string): string => (type in typeLabels ? typeLabels[type as InstrumentType] : type);

export const sectorNone = 'Unclassified';

export const columnLabels: Readonly<Record<SortField, string>> = {
  symbol: 'Symbol',
  name: 'Name',
  type: 'Type',
  series: 'Series',
  sector: 'Sector',
  lot: 'Lot',
  tick: 'Tick',
};

const phaseLabels: Readonly<Record<string, string>> = {
  downloading: 'Downloading the file from Dhan',
  parsing: 'Reading the file',
  enriching: 'Adding sectors and F&O flags',
  saving: 'Saving the list',
};

export const phaseLabel = (phase: string | null): string => (phase ? (phaseLabels[phase] ?? phase) : 'Starting');

const triggerLabels: Readonly<Record<string, string>> = {
  manual: 'Manual refresh',
  daily: 'Automatic daily refresh',
  forced: 'Forced full refresh',
};

export const triggerLabel = (trigger: string): string => triggerLabels[trigger] ?? trigger;

/** "+12 added · 3 updated · 5 retired · 4,381 active", or "No changes · 4,381 active" when nothing moved. */
export function summaryLine(run: RefreshRun): string {
  const changes = [
    run.added > 0 ? `+${formatNumber(run.added)} added` : null,
    run.updated > 0 ? `${formatNumber(run.updated)} updated` : null,
    run.retired > 0 ? `${formatNumber(run.retired)} retired` : null,
    run.revived > 0 ? `${formatNumber(run.revived)} revived` : null,
  ].filter((part): part is string => part !== null);
  return [...(changes.length > 0 ? changes : ['No changes']), `${formatNumber(run.totalActive)} active`].join(' · ');
}

/** The sentence for the toast and the screen reader when a run ends. */
export function outcomeText(run: RefreshRun): string {
  if (run.status === 'failed') return 'The refresh failed — the previous list is unchanged.';
  if (run.status === 'unchanged') return 'No changes — the file is unchanged.';
  return `Instrument list refreshed: ${summaryLine(run)}`;
}
