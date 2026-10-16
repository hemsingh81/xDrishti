import { FormControl, FormHelperText, FormLabel, OutlinedInput, type OutlinedInputProps } from '@mui/material';
import { useId, type InputHTMLAttributes, type ReactNode, type Ref } from 'react';
import { rem } from '@/theme';

interface LoginFieldProps extends Pick<
  OutlinedInputProps,
  'name' | 'type' | 'autoComplete' | 'value' | 'onChange' | 'onKeyDown' | 'onKeyUp' | 'onBlur' | 'endAdornment'
> {
  label: string;
  /** The message when the field is invalid; it also marks the field invalid. */
  error?: string | undefined;
  inputRef?: Ref<HTMLInputElement>;
  /** Extra attributes for the native `<input>` (read-only while the form is blocked, `autocapitalize`, …). */
  inputProps?: InputHTMLAttributes<HTMLInputElement>;
}

/**
 * A text field with its label above it (the prototype's `label.field`: 12.5 px / 550 label, 5 px gap, 36 px input) — a real
 * `<label for>` pair, so the input's accessible name is the label text, and an error is linked with `aria-describedby`.
 */
export function LoginField({ label, error, inputRef, inputProps, ...input }: LoginFieldProps): ReactNode {
  const id = useId();
  const helperId = `${id}-help`;
  return (
    <FormControl fullWidth error={Boolean(error)} sx={{ gap: '5px' }}>
      <FormLabel
        htmlFor={id}
        sx={{
          fontSize: rem(12.5),
          fontWeight: 550,
          color: 'text.secondary',
          '&.Mui-error': { color: 'text.secondary' },
        }}
      >
        {label}
      </FormLabel>
      <OutlinedInput
        {...input}
        id={id}
        fullWidth
        sx={{ '& .MuiInputBase-input[aria-disabled="true"]': { cursor: 'not-allowed', color: 'text.disabled' } }}
        inputRef={inputRef}
        slotProps={{ input: { 'aria-describedby': error ? helperId : undefined, ...inputProps } }}
      />
      {error && (
        <FormHelperText id={helperId} sx={{ m: 0 }}>
          {error}
        </FormHelperText>
      )}
    </FormControl>
  );
}
