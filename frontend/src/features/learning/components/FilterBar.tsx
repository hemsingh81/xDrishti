import { ToggleButton, ToggleButtonGroup } from '@mui/material';

interface FilterBarProps<T extends string> {
  label: string;
  values: readonly T[];
  value: T;
  onChange: (next: T) => void;
}

/** A one-of-many filter held in the address; the first value is the default. 44 px targets that wrap on a phone. */
export function FilterBar<T extends string>({ label, values, value, onChange }: FilterBarProps<T>) {
  return (
    <ToggleButtonGroup
      exclusive
      size="small"
      aria-label={label}
      value={value}
      onChange={(_event, next: T | null) => {
        if (next !== null) onChange(next);
      }}
      sx={{ flexWrap: 'wrap', '& .MuiToggleButton-root': { minHeight: 44, textTransform: 'capitalize' } }}
    >
      {values.map((code) => (
        <ToggleButton key={code} value={code}>
          {code}
        </ToggleButton>
      ))}
    </ToggleButtonGroup>
  );
}
