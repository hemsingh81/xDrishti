import { Box, Checkbox, FormControlLabel, Stack } from '@mui/material';
import type { CsvFileCounts } from '@/shared/api';
import { SearchField } from '@/shared/ui/SearchField';
import { csvStateFilters, csvStateLabels, type CsvStateFilter } from '../lib/csvReview';
import { FilterChips } from './FilterChips';

interface CsvFileFiltersProps {
  q: string;
  onSearch: (q: string) => void;
  state: CsvStateFilter;
  onState: (state: CsvStateFilter) => void;
  counts: CsvFileCounts | undefined;
  /** "Select all loaded rows": every file in the list that can be checked is checked / some are. */
  allChosen: boolean;
  someChosen: boolean;
  /** Nothing in the list can be checked (no rows, or none maps to an instrument). */
  disableAll: boolean;
  onToggleAll: (on: boolean) => void;
}

/** Search by file name or symbol, the state chips (Pending, Changed, Loaded, All) with their counts and "Select all loaded rows". */
export function CsvFileFilters({
  q,
  onSearch,
  state,
  onState,
  counts,
  allChosen,
  someChosen,
  disableAll,
  onToggleAll,
}: CsvFileFiltersProps) {
  const options = csvStateFilters.map((value) => ({
    value,
    label: csvStateLabels[value],
    count: counts ? (value === 'all' ? counts.total : counts[value]) : undefined,
  }));
  return (
    <Stack direction={{ xs: 'column', sm: 'row' }} sx={{ gap: 1.5, alignItems: { sm: 'center' }, flexWrap: 'wrap' }}>
      <Box sx={{ width: { xs: '100%', sm: 320 } }}>
        <SearchField value={q} onCommit={onSearch} label="Search files" placeholder="File name or symbol" />
      </Box>
      <FilterChips label="Show files" value={state} options={options} onChange={onState} />
      <FormControlLabel
        sx={{ ml: { sm: 'auto' }, mr: 0 }}
        label="Select all loaded rows"
        control={
          <Checkbox
            size="small"
            checked={allChosen}
            indeterminate={someChosen && !allChosen}
            disabled={disableAll}
            onChange={(event) => {
              onToggleAll(event.target.checked);
            }}
          />
        }
      />
    </Stack>
  );
}
