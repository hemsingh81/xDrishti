import { Typography } from '@mui/material';
import type { ReactNode } from 'react';
import { formatNumber } from '@/shared/lib/format';
import { numeric } from '@/theme';
import type { GroupSortField } from '../lib/groups';
import {
  GroupDayChange,
  GroupInvested,
  GroupPnl,
  GroupValue,
  GroupWeight,
  GroupXirr,
  UnpricedFlag,
  type GroupFigures,
} from './GroupFigures';

/** One sort button in a column header. */
export interface GroupSortKey {
  field: GroupSortField;
  /** Accessible name when it differs from the header ("P&L %"). */
  label: string;
  /** The text shown on the button when it differs from the header. */
  text?: string;
}

export interface GroupColumn {
  id: string;
  header: string;
  /** The API sort fields this column's header can sort by. */
  sorts?: readonly GroupSortKey[];
  /** Width of the column (the table's layout is fixed: the name column takes what is left). */
  width: number;
  /** Hidden below this breakpoint, like the same figures in the holdings list. */
  hideBelow?: 'md' | 'lg';
  /** The cell: a group's figures, or the scope total's (`total`). */
  render: (figures: GroupFigures) => ReactNode;
}

/** The figure columns after the name: same hiding rules as the holdings columns (invested and day change from lg). */
export const groupColumns: readonly GroupColumn[] = [
  {
    id: 'holdings',
    header: 'Holdings',
    width: 120,
    render: (g) => (
      <>
        <Typography component="span" sx={numeric}>
          {formatNumber(g.holdings)}
        </Typography>
        <UnpricedFlag holdings={g.holdings} unpriced={g.unpriced} />
      </>
    ),
  },
  {
    id: 'invested',
    header: 'Invested',
    width: 120,
    hideBelow: 'lg',
    sorts: [{ field: 'invested', label: 'Invested' }],
    render: (g) => <GroupInvested group={g} />,
  },
  {
    id: 'value',
    header: 'Value',
    width: 120,
    sorts: [{ field: 'value', label: 'Value' }],
    render: (g) => <GroupValue group={g} />,
  },
  {
    id: 'pnl',
    header: 'P&L',
    width: 120,
    sorts: [
      { field: 'pnl', label: 'P&L', text: 'P&L' },
      { field: 'pnlPct', label: 'P&L %', text: '%' },
    ],
    render: (g) => <GroupPnl group={g} />,
  },
  {
    id: 'xirr',
    header: 'XIRR',
    width: 120,
    sorts: [{ field: 'xirr', label: 'XIRR' }],
    render: (g) => <GroupXirr group={g} />,
  },
  {
    id: 'dayChange',
    header: 'Day change',
    width: 120,
    hideBelow: 'lg',
    sorts: [{ field: 'dayChange', label: 'Day change' }],
    render: (g) => <GroupDayChange group={g} />,
  },
  {
    id: 'weight',
    header: 'Weight',
    width: 170,
    render: (g) => <GroupWeight weightPct={g.weightPct} />,
  },
];

/** Layout of a figure cell (header, group row or total row): right-aligned tabular figures, hidden on narrow screens. */
export function figureCellSx(column: GroupColumn) {
  return {
    width: column.width,
    paddingInline: 1.25,
    textAlign: 'right',
    verticalAlign: 'top',
    whiteSpace: 'nowrap',
    ...numeric,
    ...(column.hideBelow ? { display: { xs: 'none', [column.hideBelow]: 'table-cell' } } : {}),
  };
}

const nameColumnWidth = 200;
const widthOf = (columns: readonly GroupColumn[]) => columns.reduce((sum, c) => sum + c.width, nameColumnWidth);

/** The table's width without and with the columns that appear from lg: a narrow window scrolls sideways instead of squeezing the figures. */
export const groupTableMinWidth = {
  xs: widthOf(groupColumns.filter((c) => c.hideBelow === undefined)),
  lg: widthOf(groupColumns),
};
