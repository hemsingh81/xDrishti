import { Box, Button, Chip } from '@mui/material';
import type { BarsOverview } from '@/shared/api';
import { SearchField } from '@/shared/ui/SearchField';
import { activeFilterCount, type CoverageFilters, type CoverageStatus } from '../lib/filters';
import { statusLabels } from '../lib/labels';
import { FilterChips, type ChipOption } from './FilterChips';

interface CoverageToolbarProps {
  filters: CoverageFilters;
  /** The counts behind the status chips (all instruments, whatever the other filters). */
  bars: BarsOverview;
  onChange: (patch: Partial<CoverageFilters>, options?: { replace?: boolean }) => void;
  onClear: () => void;
}

type StatusChoice = CoverageStatus | 'all';

function optionsOf(bars: BarsOverview): ChipOption<StatusChoice>[] {
  const current = Math.max(0, bars.instruments - bars.instrumentsBehind);
  return [
    { value: 'all', label: 'All', count: bars.instruments + bars.instrumentsEmpty },
    { value: 'current', label: statusLabels.current, count: current },
    { value: 'behind', label: statusLabels.behind, count: bars.instrumentsBehind },
    { value: 'empty', label: statusLabels.empty, count: bars.instrumentsEmpty },
  ];
}

/** Search box, the status chips with their counts, the "Has gaps" toggle and Clear; each change goes to the URL. */
export function CoverageToolbar({ filters, bars, onChange, onClear }: CoverageToolbarProps) {
  return (
    <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 1.25, alignItems: 'center', px: 1.5, py: 1 }}>
      <Box sx={{ width: { xs: '100%', sm: 320 } }}>
        <SearchField
          value={filters.q}
          onCommit={(q) => {
            onChange({ q }, { replace: true });
          }}
          label="Search coverage"
          placeholder="Search symbol or name"
        />
      </Box>
      <FilterChips
        label="Status"
        value={filters.status ?? 'all'}
        options={optionsOf(bars)}
        onChange={(value) => {
          onChange({ status: value === 'all' ? null : value });
        }}
      />
      <Chip
        clickable
        size="small"
        aria-pressed={filters.hasGaps}
        color={filters.hasGaps ? 'primary' : 'default'}
        variant={filters.hasGaps ? 'filled' : 'outlined'}
        label="Has gaps"
        onClick={() => {
          onChange({ hasGaps: !filters.hasGaps });
        }}
      />
      <Button size="small" disabled={activeFilterCount(filters) === 0} onClick={onClear} sx={{ ml: { sm: 'auto' } }}>
        Clear filters
      </Button>
    </Box>
  );
}
