import { Button, Card, CardContent, Chip, FormControlLabel, Stack, Switch } from '@mui/material';
import type { InstrumentFacets } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';
import { Typeahead, type TypeaheadOption } from '@/shared/ui/Typeahead';
import { activeFilterCount, type InstrumentFilters } from '../lib/filters';
import { FacetSelect } from './FacetSelect';
import { SearchBox } from './SearchBox';
import { TypeChips } from './TypeChips';

interface FilterBarProps {
  filters: InstrumentFilters;
  /** Undefined while loading or when the counts could not be loaded; the filters still work. */
  facets: InstrumentFacets | undefined;
  onChange: (patch: Partial<InstrumentFilters>, options?: { replace?: boolean }) => void;
  onClear: () => void;
}

function Toggle({ label, on, onToggle }: { label: string; on: boolean; onToggle: () => void }) {
  return (
    <Chip
      clickable
      aria-pressed={on}
      variant={on ? 'filled' : 'outlined'}
      color={on ? 'primary' : 'default'}
      label={label}
      onClick={onToggle}
    />
  );
}

const sectorOptions = (facets: InstrumentFacets | undefined): TypeaheadOption[] =>
  (facets?.sectors ?? []).map((s) => ({ value: s.value, label: s.value, count: s.count }));

const withCount = (label: string, count: number | undefined) =>
  count === undefined ? label : `${label} ${formatNumber(count)}`;

/** Search box and every filter of the master list. Each change goes to the URL through `onChange`. */
export function FilterBar({ filters, facets, onChange, onClear }: FilterBarProps) {
  const active = activeFilterCount(filters);
  return (
    <Card sx={{ mb: 2, flexShrink: 0 }}>
      <CardContent>
        <Stack spacing={1.5}>
          <Stack direction="row" sx={{ flexWrap: 'wrap', gap: 1.5, alignItems: 'center' }}>
            <SearchBox
              value={filters.q}
              onCommit={(q) => {
                onChange({ q }, { replace: true });
              }}
            />
            <Typeahead
              label="Sector"
              placeholder="All sectors"
              options={sectorOptions(facets)}
              value={filters.sector}
              noOptionsText="No sector matches"
              onChange={(sector) => {
                onChange({ sector });
              }}
            />
            <FacetSelect
              label="Series"
              allLabel="All series"
              value={filters.series}
              options={facets?.series}
              onChange={(series) => {
                onChange({ series });
              }}
            />
            <Button size="small" disabled={active === 0} onClick={onClear} sx={{ ml: { sm: 'auto' } }}>
              Clear all filters
            </Button>
          </Stack>
          <Stack direction="row" sx={{ flexWrap: 'wrap', gap: 1.5, alignItems: 'center' }}>
            <TypeChips
              selected={filters.type}
              counts={facets?.types}
              onSelect={(type) => {
                onChange({ type });
              }}
            />
            <Toggle
              label={withCount('F&O only', facets?.fnoEligible)}
              on={filters.fno}
              onToggle={() => {
                onChange({ fno: !filters.fno });
              }}
            />
            <Toggle
              label={withCount('ASM/GSM only', facets?.asmGsm)}
              on={filters.asmGsm}
              onToggle={() => {
                onChange({ asmGsm: !filters.asmGsm });
              }}
            />
            <FormControlLabel
              label={facets && facets.retired > 0 ? `Show retired (${formatNumber(facets.retired)})` : 'Show retired'}
              control={
                <Switch
                  checked={filters.includeRetired}
                  onChange={(event) => {
                    onChange({ includeRetired: event.target.checked });
                  }}
                />
              }
            />
          </Stack>
        </Stack>
      </CardContent>
    </Card>
  );
}
