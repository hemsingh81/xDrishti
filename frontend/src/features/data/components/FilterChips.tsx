import { Chip, Stack } from '@mui/material';
import { formatNumber } from '@/shared/lib/format';

export interface ChipOption<T extends string> {
  value: T;
  label: string;
  /** Shown after the label ("Behind 12"); left out when not known. */
  count?: number | undefined;
}

interface FilterChipsProps<T extends string> {
  /** Accessible name of the group, e.g. "Show files". */
  label: string;
  value: T;
  options: readonly ChipOption<T>[];
  onChange: (value: T) => void;
}

/** One-of-many filter as a row of chips with counts; the chosen chip is `aria-pressed` (and filled, not just coloured). */
export function FilterChips<T extends string>({ label, value, options, onChange }: FilterChipsProps<T>) {
  return (
    <Stack direction="row" role="group" aria-label={label} sx={{ gap: 0.75, flexWrap: 'wrap' }}>
      {options.map((option) => (
        <Chip
          key={option.value}
          clickable
          size="small"
          aria-pressed={value === option.value}
          color={value === option.value ? 'primary' : 'default'}
          variant={value === option.value ? 'filled' : 'outlined'}
          label={option.count === undefined ? option.label : `${option.label} ${formatNumber(option.count)}`}
          onClick={() => {
            onChange(option.value);
          }}
        />
      ))}
    </Stack>
  );
}
