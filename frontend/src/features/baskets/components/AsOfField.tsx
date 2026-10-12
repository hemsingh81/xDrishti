import { Button, Stack, TextField } from '@mui/material';
import { todayIso } from '@/shared/lib/dates';

interface AsOfFieldProps {
  /** yyyy-MM-dd, or null for today's members. */
  value: string | null;
  onChange: (value: string | null) => void;
}

/** "View as of": look at the members as they were at the end of a past day (read only). */
export function AsOfField({ value, onChange }: AsOfFieldProps) {
  return (
    <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
      <TextField
        type="date"
        size="small"
        label="View as of"
        value={value ?? ''}
        onChange={(event) => {
          const next = event.target.value;
          // The picker stops at today; a typed future date is ignored.
          if (next <= todayIso()) onChange(next === '' ? null : next);
        }}
        slotProps={{ inputLabel: { shrink: true }, htmlInput: { max: todayIso() } }}
        sx={{ width: 170 }}
      />
      {value && (
        <Button
          size="small"
          onClick={() => {
            onChange(null);
          }}
        >
          Back to today
        </Button>
      )}
    </Stack>
  );
}
