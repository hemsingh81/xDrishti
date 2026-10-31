import { MenuItem, Stack, TextField } from '@mui/material';

interface SelectProps {
  label: string;
  value: string;
  options: readonly { value: string; label: string }[];
  onChange: (value: string) => void;
}

export function FilterSelect({ label, value, options, onChange }: SelectProps) {
  return (
    <TextField
      select
      size="small"
      label={label}
      value={value}
      onChange={(e) => {
        onChange(e.target.value);
      }}
      sx={{ minWidth: 160 }}
    >
      <MenuItem value="">All</MenuItem>
      {options.map((o) => (
        <MenuItem key={o.value} value={o.value}>
          {o.label}
        </MenuItem>
      ))}
    </TextField>
  );
}

export function DateFilter({ value, onChange }: { value: string; onChange: (value: string) => void }) {
  return (
    <TextField
      size="small"
      type="date"
      label="Date"
      value={value}
      onChange={(e) => {
        onChange(e.target.value);
      }}
      slotProps={{ inputLabel: { shrink: true } }}
    />
  );
}

export function FilterBar({ children }: { children: React.ReactNode }) {
  return (
    <Stack direction="row" role="search" aria-label="Filters" sx={{ gap: 1.5, flexWrap: 'wrap', mb: 2 }}>
      {children}
    </Stack>
  );
}
