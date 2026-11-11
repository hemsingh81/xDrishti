import { Chip, Stack } from '@mui/material';
import { storedKinds, type KindFilter } from '../lib/system';

const options: readonly { value: KindFilter; label: string }[] = [
  { value: 'all', label: 'All' },
  ...storedKinds.map((kind) => ({ value: kind, label: `${kind.charAt(0).toUpperCase()}${kind.slice(1)}` })),
  { value: 'yours', label: 'Yours' },
];

interface KindFilterChipsProps {
  value: KindFilter;
  onChange: (value: KindFilter) => void;
}

/** Filters the cards by kind (kept in the address as `?kind=`). The chosen chip says so with `aria-pressed`. */
export function KindFilterChips({ value, onChange }: KindFilterChipsProps) {
  return (
    <Stack direction="row" role="group" aria-label="Filter baskets by kind" sx={{ gap: 1, flexWrap: 'wrap' }}>
      {options.map((option) => (
        <Chip
          key={option.value}
          label={option.label}
          clickable
          aria-pressed={value === option.value}
          color={value === option.value ? 'primary' : 'default'}
          variant={value === option.value ? 'filled' : 'outlined'}
          onClick={() => {
            onChange(option.value);
          }}
        />
      ))}
    </Stack>
  );
}
