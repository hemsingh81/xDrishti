import WarningIcon from '@mui/icons-material/WarningAmberOutlined';
import { Box, Chip, Stack } from '@mui/material';
import type { InstrumentSummary } from '@/shared/api';
import { formatDate, formatDateTime, formatNumber } from '@/shared/lib/format';
import type { GridColumn } from '@/shared/ui/grid';
import { monoFontFamily } from '@/theme';
import type { InstrumentTab } from '../lib/filters';
import { columnLabels, typeLabel } from '../lib/labels';
import { TrackToggle } from './TrackToggle';

const dash = '—';
type Column = GridColumn<InstrumentSummary>;

const symbolColumn: Column = {
  id: 'symbol',
  header: columnLabels.symbol,
  sortable: true,
  render: (item) => (
    <Box component="span" sx={{ fontFamily: monoFontFamily, fontWeight: 600 }}>
      {item.symbol}
    </Box>
  ),
};

const nameColumn: Column = {
  id: 'name',
  header: columnLabels.name,
  sortable: true,
  grow: true,
  minWidth: 200,
  accessor: (i) => i.displayName,
};

const typeColumn: Column = {
  id: 'type',
  header: columnLabels.type,
  sortable: true,
  render: (item) => <Chip size="small" variant="outlined" label={typeLabel(item.type)} />,
};

const seriesColumn: Column = {
  id: 'series',
  header: columnLabels.series,
  sortable: true,
  hideBelow: 'md',
  accessor: (i) => i.series ?? dash,
};

const sectorColumn: Column = {
  id: 'sector',
  header: columnLabels.sector,
  sortable: true,
  hideBelow: 'md',
  accessor: (i) => i.sector ?? dash,
};

const lotColumn: Column = {
  id: 'lot',
  header: columnLabels.lot,
  sortable: true,
  numeric: true,
  accessor: (i) => formatNumber(i.lotSize),
};

const tickColumn: Column = {
  id: 'tick',
  header: columnLabels.tick,
  sortable: true,
  numeric: true,
  hideBelow: 'xl',
  accessor: (i) => (i.tickSize === null ? dash : formatNumber(i.tickSize)),
};

/** F&O, ASM/GSM and (master list) the tracked and retired markers. Each is text, not colour alone. */
const flagsColumn: Column = {
  id: 'flags',
  header: 'Flags',
  render: (i) => (
    <Stack direction="row" spacing={0.5}>
      {i.fnoEligible && <Chip size="small" variant="outlined" color="info" label="F&O" />}
      {i.asmGsm && <Chip size="small" variant="outlined" color="warning" label="ASM/GSM" />}
      {i.tracked.active && <Chip size="small" variant="outlined" color="success" label="Tracked" />}
      {i.retired && <Chip size="small" label="Retired" />}
    </Stack>
  ),
};

/** The Track / Untrack button of the row. A `handle` column: clicking it never opens the drawer. */
const trackingColumn: Column = {
  id: 'tracking',
  header: 'Tracking',
  kind: 'handle',
  align: 'right',
  render: (i) => <TrackToggle id={i.id} symbol={i.symbol} tracked={i.tracked.active} retired={i.retired} />,
};

const coverageColumns: readonly Column[] = [
  {
    id: 'from',
    header: 'Data from',
    hideBelow: 'lg',
    accessor: (i) => (i.tracked.dataFrom ? formatDate(i.tracked.dataFrom) : dash),
  },
  {
    id: 'to',
    header: 'Data to',
    hideBelow: 'lg',
    accessor: (i) => (i.tracked.dataTo ? formatDate(i.tracked.dataTo) : dash),
  },
  {
    id: 'updated',
    header: 'Last update',
    hideBelow: 'md',
    accessor: (i) => (i.tracked.lastUpdate ? formatDateTime(i.tracked.lastUpdate) : dash),
  },
  // Quality scores come with the data-quality checks; until bars exist there is nothing to score.
  { id: 'quality', header: 'Quality', hideBelow: 'xl', accessor: () => dash },
  {
    id: 'status',
    header: 'Status',
    minWidth: 180,
    render: (i) => (
      <Stack direction="row" sx={{ flexWrap: 'wrap', gap: 0.5 }}>
        <Chip
          size="small"
          variant="outlined"
          label={i.tracked.dataTo ? 'History collected' : 'History not collected yet'}
        />
        {i.tracked.retiredButTracked && (
          <Chip
            size="small"
            variant="outlined"
            color="warning"
            icon={<WarningIcon />}
            label="No longer in Dhan's list"
          />
        )}
      </Stack>
    ),
  },
];

/**
 * Extension point of the Tracked tab: columns added here appear between the coverage columns and the Track button. A
 * basket chips column is not here on purpose: the API gives the baskets of one instrument per request, and a column
 * would fire one request per row. The detail drawer shows the baskets instead; a column waits for a list endpoint that
 * returns them with the page.
 */
const trackedExtraColumns: readonly Column[] = [];

const masterColumns: readonly Column[] = [
  symbolColumn,
  nameColumn,
  typeColumn,
  seriesColumn,
  sectorColumn,
  lotColumn,
  tickColumn,
  flagsColumn,
  trackingColumn,
];

const trackedColumns: readonly Column[] = [
  symbolColumn,
  nameColumn,
  typeColumn,
  sectorColumn,
  ...coverageColumns,
  ...trackedExtraColumns,
  trackingColumn,
];

/**
 * The columns of a tab (stable arrays: the grid re-renders its rows when they change). The ids of the sortable ones are
 * the API's sort fields. Series, sector (and tick, below `xl`) are hidden on narrow screens; the detail drawer shows them.
 */
export const columnsFor = (tab: InstrumentTab): readonly Column[] =>
  tab === 'tracked' ? trackedColumns : masterColumns;
