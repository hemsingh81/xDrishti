import { Chip, Stack } from '@mui/material';
import type { FacetCount } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';
import { instrumentTypes, type InstrumentType } from '../lib/filters';
import { typeLabels } from '../lib/labels';

interface TypeChipsProps {
  selected: InstrumentType | null;
  /** Counts per type; absent while the counts load or when they could not be loaded. */
  counts: readonly FacetCount[] | undefined;
  onSelect: (type: InstrumentType | null) => void;
}

/** One toggle chip per instrument type, with the number of active instruments of that type. */
export function TypeChips({ selected, counts, onSelect }: TypeChipsProps) {
  return (
    <Stack direction="row" role="group" aria-label="Type" sx={{ flexWrap: 'wrap', gap: 1 }}>
      {instrumentTypes.map((type) => {
        const count = counts?.find((c) => c.value === type)?.count;
        const on = selected === type;
        return (
          <Chip
            key={type}
            clickable
            aria-pressed={on}
            variant={on ? 'filled' : 'outlined'}
            color={on ? 'primary' : 'default'}
            label={count === undefined ? typeLabels[type] : `${typeLabels[type]} ${formatNumber(count)}`}
            onClick={() => {
              onSelect(on ? null : type);
            }}
          />
        );
      })}
    </Stack>
  );
}
