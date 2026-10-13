import { TextField } from '@mui/material';

interface TimeFieldProps {
  label: string;
  value: string;
  error?: string;
  onChange: (value: string) => void;
}

/** A time of day in India time (HH:mm). */
export function TimeField({ label, value, error, onChange }: TimeFieldProps) {
  return (
    <TextField
      type="time"
      label={label}
      value={value}
      error={Boolean(error)}
      helperText={error ?? 'India time (IST)'}
      onChange={(event) => {
        onChange(event.target.value);
      }}
      slotProps={{ inputLabel: { shrink: true }, htmlInput: { step: 60 } }}
      sx={{ minWidth: 150 }}
    />
  );
}
