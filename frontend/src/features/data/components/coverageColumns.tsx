import GapsIcon from '@mui/icons-material/PlaylistRemoveOutlined';
import { Box, Chip } from '@mui/material';
import type { DataCoverageRow } from '@/shared/api';
import { formatIstDateTime, formatNumber } from '@/shared/lib/format';
import type { GridColumn } from '@/shared/ui/grid';
import { monoFontFamily } from '@/theme';
import { columnLabels, sourceLabel } from '../lib/labels';
import { RowStatus } from './RowStatus';

const dash = '—';
type Column = GridColumn<DataCoverageRow>;

/**
 * Ids of the sortable columns are the API's sort fields. The last bar is the point of the table: bold and in the warning
 * colour when behind (the status chip says it in words too). The first bar is the tooltip of the symbol. The last bar is
 * hidden below `sm`, name and bars below `md`, source and gaps below `lg`; the drawer shows what a row hides.
 */
export const coverageColumns: readonly Column[] = [
  {
    id: 'symbol',
    header: columnLabels.symbol,
    sortable: true,
    render: (row) => (
      <Box
        component="span"
        title={row.firstBar ? `First bar ${formatIstDateTime(row.firstBar)}` : 'No bars yet'}
        sx={{ fontFamily: monoFontFamily, fontWeight: 600 }}
      >
        {row.symbol}
      </Box>
    ),
  },
  {
    id: 'name',
    header: columnLabels.name,
    sortable: true,
    grow: true,
    minWidth: 160,
    hideBelow: 'md',
    accessor: (r) => r.name,
  },
  {
    id: 'lastBar',
    header: columnLabels.lastBar,
    sortable: true,
    hideBelow: 'sm',
    render: (r) =>
      r.lastBar ? (
        <Box component="span" sx={r.status === 'behind' ? { color: 'warning.main', fontWeight: 700 } : undefined}>
          <time dateTime={r.lastBar}>{formatIstDateTime(r.lastBar)}</time>
        </Box>
      ) : (
        dash
      ),
  },
  {
    id: 'bars',
    header: columnLabels.bars,
    sortable: true,
    numeric: true,
    hideBelow: 'md',
    accessor: (r) => formatNumber(r.bars),
  },
  {
    id: 'source',
    header: 'Source',
    hideBelow: 'lg',
    render: (r) => (r.source ? <Chip size="small" variant="outlined" label={sourceLabel(r.source)} /> : dash),
  },
  {
    id: 'gaps',
    header: 'Gaps',
    hideBelow: 'lg',
    render: (r) =>
      r.openGaps > 0 ? (
        <Chip
          size="small"
          variant="outlined"
          color="warning"
          icon={<GapsIcon />}
          label={`${formatNumber(r.openGaps)} open`}
        />
      ) : (
        dash
      ),
  },
  { id: 'status', header: 'Status', minWidth: 130, render: (row) => <RowStatus row={row} /> },
];
