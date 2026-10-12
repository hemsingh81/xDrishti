import { Box } from '@mui/material';
import type { GapRow } from '@/shared/api';
import { formatDate, formatIstDateTime, formatNumber } from '@/shared/lib/format';
import type { GridColumn } from '@/shared/ui/grid';
import { monoFontFamily } from '@/theme';
import { GapStatusChip } from './GapStatusChip';

const dash = '—';

/**
 * The Gaps list: instrument, session day, bars found against expected, refetch attempts, state and the last attempt.
 * Name, attempts and the last attempt are hidden on narrow screens.
 */
export const gapColumns: readonly GridColumn<GapRow>[] = [
  {
    id: 'symbol',
    header: 'Instrument',
    render: (g) => (
      <Box component="span" sx={{ fontFamily: monoFontFamily, fontWeight: 600 }}>
        {g.symbol}
      </Box>
    ),
  },
  { id: 'name', header: 'Name', grow: true, minWidth: 140, hideBelow: 'md', accessor: (g) => g.name },
  { id: 'date', header: 'Session', render: (g) => <time dateTime={g.date}>{formatDate(g.date)}</time> },
  {
    id: 'bars',
    header: 'Bars found / expected',
    numeric: true,
    accessor: (g) => `${formatNumber(g.found)} / ${formatNumber(g.expected)}`,
  },
  { id: 'attempts', header: 'Attempts', numeric: true, hideBelow: 'sm', accessor: (g) => formatNumber(g.attempts) },
  { id: 'status', header: 'Status', minWidth: 120, render: (g) => <GapStatusChip status={g.status} /> },
  {
    id: 'lastAttempt',
    header: 'Last attempt',
    hideBelow: 'md',
    render: (g) =>
      g.lastAttemptAt ? <time dateTime={g.lastAttemptAt}>{formatIstDateTime(g.lastAttemptAt)}</time> : dash,
  },
];
