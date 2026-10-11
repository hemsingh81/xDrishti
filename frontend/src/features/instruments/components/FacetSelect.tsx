import { MenuItem, TextField } from '@mui/material';
import type { FacetCount } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';

interface FacetSelectProps {
  label: string;
  allLabel: string;
  value: string | null;
  options: readonly FacetCount[] | undefined;
  onChange: (value: string | null) => void;
}

/** A select over facet values ("Banking (93)"); the current value stays selectable even if it has no rows now. */
export function FacetSelect({ label, allLabel, value, options, onChange }: FacetSelectProps) {
  const listed = options ?? [];
  const missing = value !== null && !listed.some((o) => o.value === value);
  return (
    <TextField
      select
      size="small"
      label={label}
      value={value ?? ''}
      onChange={(event) => {
        onChange(event.target.value || null);
      }}
      slotProps={{ inputLabel: { shrink: true }, select: { displayEmpty: true } }}
      sx={{ minWidth: 190 }}
    >
      <MenuItem value="">{allLabel}</MenuItem>
      {missing && <MenuItem value={value}>{value}</MenuItem>}
      {listed.map((o) => (
        <MenuItem key={o.value} value={o.value}>
          {o.value} ({formatNumber(o.count)})
        </MenuItem>
      ))}
    </TextField>
  );
}
