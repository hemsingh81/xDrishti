import { Button, Stack } from '@mui/material';
import { SearchField } from '@/shared/ui/SearchField';
import { activeFilterCount, type QualityFilters } from '../lib/filters';

interface InstrumentsToolbarProps {
  filters: QualityFilters;
  onChange: (patch: Partial<QualityFilters>, options?: { replace?: boolean }) => void;
  onClear: () => void;
}

/** Search box of the instruments table (the result filter is the chips of the summary ribbon); each change goes to the URL. */
export function InstrumentsToolbar({ filters, onChange, onClear }: InstrumentsToolbarProps) {
  return (
    <Stack direction="row" sx={{ flexWrap: 'wrap', gap: 1.5, alignItems: 'center', p: 1.5 }}>
      <SearchField
        value={filters.q}
        onCommit={(q) => {
          onChange({ q }, { replace: true });
        }}
        label="Search instruments"
        placeholder="Search symbol or name"
      />
      {activeFilterCount(filters) > 0 && (
        <Button size="small" onClick={onClear}>
          Clear filters
        </Button>
      )}
    </Stack>
  );
}
