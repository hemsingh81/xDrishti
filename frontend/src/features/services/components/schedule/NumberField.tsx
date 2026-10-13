import { TextField } from '@mui/material';

interface NumberFieldProps {
  label: string;
  value: string;
  min: number;
  max: number;
  /** "any" for a number with decimals. */
  step?: number | 'any';
  help?: string;
  error?: string;
  onChange: (value: string) => void;
}

/** A number with its bounds (also on the input, so the keyboard and the spinner keep to them). */
export function NumberField({ label, value, min, max, step = 1, help, error, onChange }: NumberFieldProps) {
  return (
    <TextField
      type="number"
      label={label}
      value={value}
      error={Boolean(error)}
      helperText={error ?? help}
      onChange={(event) => {
        onChange(event.target.value);
      }}
      slotProps={{ htmlInput: { min, max, step } }}
      sx={{ minWidth: 150 }}
    />
  );
}
