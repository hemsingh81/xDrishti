import { Box, Chip, Stack } from '@mui/material';
import type { InstrumentSummary } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';
import type { GridColumn } from '@/shared/ui/grid';
import { monoFontFamily } from '@/theme';
import { columnLabels, typeLabel } from '../lib/labels';

const dash = '—';

/**
 * The columns of the master list. The ids of the sortable ones are the API's sort fields. Series, sector (and tick, below
 * `xl`) are hidden on narrow screens; the detail drawer shows them.
 */
export const instrumentColumns: readonly GridColumn<InstrumentSummary>[] = [
  {
    id: 'symbol',
    header: columnLabels.symbol,
    sortable: true,
    render: (item) => (
      <Box component="span" sx={{ fontFamily: monoFontFamily, fontWeight: 600 }}>
        {item.symbol}
      </Box>
    ),
  },
  { id: 'name', header: columnLabels.name, sortable: true, grow: true, minWidth: 200, accessor: (i) => i.displayName },
  {
    id: 'type',
    header: columnLabels.type,
    sortable: true,
    render: (item) => <Chip size="small" variant="outlined" label={typeLabel(item.type)} />,
  },
  {
    id: 'series',
    header: columnLabels.series,
    sortable: true,
    hideBelow: 'md',
    accessor: (i) => i.series ?? dash,
  },
  {
    id: 'sector',
    header: columnLabels.sector,
    sortable: true,
    hideBelow: 'md',
    accessor: (i) => i.sector ?? dash,
  },
  { id: 'lot', header: columnLabels.lot, sortable: true, numeric: true, accessor: (i) => formatNumber(i.lotSize) },
  {
    id: 'tick',
    header: columnLabels.tick,
    sortable: true,
    numeric: true,
    hideBelow: 'xl',
    accessor: (i) => (i.tickSize === null ? dash : formatNumber(i.tickSize)),
  },
  {
    id: 'flags',
    header: 'Flags',
    render: (i) => (
      <Stack direction="row" spacing={0.5}>
        {i.fnoEligible && <Chip size="small" variant="outlined" color="info" label="F&O" />}
        {i.asmGsm && <Chip size="small" variant="outlined" color="warning" label="ASM/GSM" />}
        {i.retired && <Chip size="small" label="Retired" />}
      </Stack>
    ),
  },
];
