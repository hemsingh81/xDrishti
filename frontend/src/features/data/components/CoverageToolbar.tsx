import { Button, MenuItem, Stack, TextField } from '@mui/material';
import { SearchField } from '@/shared/ui/SearchField';
import { activeFilterCount, coverageStatuses, type CoverageFilters, type CoverageStatus } from '../lib/filters';
import { statusLabels } from '../lib/labels';

interface CoverageToolbarProps {
  filters: CoverageFilters;
  onChange: (patch: Partial<CoverageFilters>, options?: { replace?: boolean }) => void;
  onClear: () => void;
}

const all = 'all';
const isStatus = (value: string): value is CoverageStatus => coverageStatuses.some((s) => s === value);

/** Search box and the status filter of the coverage table; each change goes to the URL through `onChange`. */
export function CoverageToolbar({ filters, onChange, onClear }: CoverageToolbarProps) {
  return (
    <Stack direction="row" sx={{ flexWrap: 'wrap', gap: 1.5, alignItems: 'center' }}>
      <SearchField
        value={filters.q}
        onCommit={(q) => {
          onChange({ q }, { replace: true });
        }}
        label="Search coverage"
        placeholder="Search symbol or name"
      />
      <TextField
        select
        size="small"
        label="Status"
        value={filters.status ?? all}
        onChange={(event) => {
          onChange({ status: isStatus(event.target.value) ? event.target.value : null });
        }}
        sx={{ minWidth: 160 }}
      >
        <MenuItem value={all}>All statuses</MenuItem>
        {coverageStatuses.map((status) => (
          <MenuItem key={status} value={status}>
            {statusLabels[status]}
          </MenuItem>
        ))}
      </TextField>
      <Button size="small" disabled={activeFilterCount(filters) === 0} onClick={onClear} sx={{ ml: { sm: 'auto' } }}>
        Clear filters
      </Button>
    </Stack>
  );
}
