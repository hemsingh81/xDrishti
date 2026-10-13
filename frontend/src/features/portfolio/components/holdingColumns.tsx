import type { HoldingRow } from '@/shared/api';
import { formatInr, formatInrPrecise, formatNumber } from '@/shared/lib/format';
import type { GridColumn } from '@/shared/ui/grid';
import { Pnl, PnlPercent, Xirr } from './Figures';
import { Acquired, DayChange, LastPrice, Symbol } from './HoldingCells';

const base: readonly GridColumn<HoldingRow>[] = [
  { id: 'symbol', header: 'Symbol', sortable: true, minWidth: 150, render: (h) => <Symbol holding={h} /> },
  { id: 'name', header: 'Name', sortable: true, grow: true, minWidth: 180, hideBelow: 'lg', accessor: (h) => h.name },
  { id: 'qty', header: 'Qty', sortable: true, numeric: true, accessor: (h) => formatNumber(h.qty) },
  {
    id: 'avgCost',
    header: 'Avg cost',
    sortable: true,
    numeric: true,
    hideBelow: 'md',
    accessor: (h) => formatInrPrecise(h.avgCost),
  },
  { id: 'lastPrice', header: 'LTP', sortable: true, numeric: true, render: (h) => <LastPrice holding={h} /> },
  {
    id: 'dayChange',
    header: 'Day change',
    sortable: true,
    numeric: true,
    hideBelow: 'lg',
    render: (h) => <DayChange holding={h} />,
  },
  {
    id: 'invested',
    header: 'Invested',
    sortable: true,
    numeric: true,
    hideBelow: 'lg',
    accessor: (h) => formatInr(h.returns.invested),
  },
  {
    id: 'value',
    header: 'Value',
    sortable: true,
    numeric: true,
    accessor: (h) => (h.returns.value === null ? '—' : formatInr(h.returns.value)),
  },
  { id: 'pnl', header: 'P&L', sortable: true, numeric: true, render: (h) => <Pnl value={h.returns.pnl} /> },
  {
    id: 'pnlPct',
    header: 'P&L %',
    sortable: true,
    numeric: true,
    render: (h) => <PnlPercent value={h.returns.pnlPct} />,
  },
  { id: 'xirr', header: 'XIRR', sortable: true, numeric: true, render: (h) => <Xirr returns={h.returns} /> },
  {
    id: 'acquiredOn',
    header: 'Acquired',
    sortable: true,
    hideBelow: 'md',
    minWidth: 190,
    render: (h) => <Acquired holding={h} />,
  },
  {
    id: 'bucket',
    header: 'Bucket',
    sortable: true,
    hideBelow: 'xl',
    accessor: (h) => h.bucketName,
  },
];

const accountColumn: GridColumn<HoldingRow> = {
  id: 'account',
  header: 'Account',
  sortable: true,
  hideBelow: 'lg',
  accessor: (h) => h.accountName,
};

/** The columns of the holdings list (stable arrays: the grid memoises its rows). The ids of the sortable ones are the API's sort fields. */
export const holdingColumns: readonly GridColumn<HoldingRow>[] = base;
export const holdingColumnsWithAccount: readonly GridColumn<HoldingRow>[] = [...base, accountColumn];
