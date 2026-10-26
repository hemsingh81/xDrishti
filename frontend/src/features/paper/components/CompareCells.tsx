import { DataTable, formatCount, type Column } from '@/shared/analytics';
import type { CompareCell } from '../api/compareQueries';
import { toMetric } from '../lib/metric';

const columns: readonly Column<CompareCell>[] = [
  { kind: 'text', id: 'cell', header: 'Cell', render: (c) => c.cellKey },
  { kind: 'text', id: 'paperN', header: 'Paper n', align: 'right', render: (c) => formatCount(c.paperN) },
  { kind: 'metric', id: 'paperMean', header: 'Paper mean R', metric: (c) => toMetric(c.paperMean) },
  { kind: 'text', id: 'refN', header: 'Backtest n', align: 'right', render: (c) => formatCount(c.referenceN) },
  { kind: 'metric', id: 'refMean', header: 'Backtest mean R', metric: (c) => toMetric(c.referenceMean) },
  {
    kind: 'text',
    id: 'state',
    header: 'Reference',
    render: (c) => (c.state === 'ok' ? 'usable' : `${c.state.replace('_', ' ')}: ${c.reason ?? ''}`),
  },
];

/** Every cell of the counted set against its reference; a cell is never dropped silently. */
export function CompareCells({ cells }: { cells: readonly CompareCell[] }) {
  return (
    <DataTable label="Cells of the counted paper trades" columns={columns} rows={cells} rowKey={(c) => c.cellKey} />
  );
}
