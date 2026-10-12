import { Box } from '@mui/material';
import type { QualityInstrumentRow } from '@/shared/api';
import { formatIstDateTime, formatNumber, formatPercent } from '@/shared/lib/format';
import type { GridColumn } from '@/shared/ui/grid';
import { monoFontFamily } from '@/theme';
import { columnLabels } from '../lib/labels';
import { QualityStatusChip } from './QualityStatusChip';

const dash = '—';
type Column = GridColumn<QualityInstrumentRow>;

/**
 * Ids of the sortable columns are the API's sort fields. On a phone only symbol, status and score show; from `md` the
 * name, coverage and missing minutes, the time of the check from `xl`. The bars checked, the source and the aggregates
 * (a mark per timeframe) are in the Inspect drawer.
 */
export const qualityColumns: readonly Column[] = [
  {
    id: 'symbol',
    header: columnLabels.symbol,
    sortable: true,
    render: (row) => (
      <Box component="span" sx={{ fontFamily: monoFontFamily, fontWeight: 600 }}>
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
    id: 'status',
    header: columnLabels.status,
    sortable: true,
    minWidth: 130,
    render: (r) => <QualityStatusChip status={r.status} />,
  },
  {
    id: 'score',
    header: columnLabels.score,
    sortable: true,
    numeric: true,
    accessor: (r) => (r.score === null ? dash : formatNumber(r.score)),
  },
  {
    id: 'coverage',
    header: columnLabels.coverage,
    sortable: true,
    numeric: true,
    hideBelow: 'md',
    accessor: (r) => (r.coverage === null ? dash : `${formatPercent(r.coverage)}%`),
  },
  {
    id: 'missingMinutes',
    header: columnLabels.missingMinutes,
    sortable: true,
    numeric: true,
    hideBelow: 'md',
    render: (r) =>
      r.missingMinutes !== null && r.missingMinutes > 0 ? (
        <Box component="span" sx={{ color: 'warning.main', fontWeight: 700 }}>
          {formatNumber(r.missingMinutes)}
        </Box>
      ) : r.missingMinutes === null ? (
        dash
      ) : (
        formatNumber(r.missingMinutes)
      ),
  },
  {
    id: 'checkedAt',
    header: columnLabels.checkedAt,
    sortable: true,
    hideBelow: 'xl',
    render: (r) => (r.checkedAt ? <time dateTime={r.checkedAt}>{formatIstDateTime(r.checkedAt)}</time> : dash),
  },
];
