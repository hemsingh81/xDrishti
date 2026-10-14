import VisibilityIcon from '@mui/icons-material/VisibilityOutlined';
import VisibilityOffIcon from '@mui/icons-material/VisibilityOffOutlined';
import { IconButton, InputAdornment, TextField, type TextFieldProps } from '@mui/material';
import { useState, type Ref } from 'react';

interface PasswordFieldProps {
  label: string;
  name: string;
  value: string;
  onChange: (value: string) => void;
  autoComplete: 'current-password' | 'new-password';
  error?: string | undefined;
  /** Shown while there is no error. */
  hint?: string;
  /** Read-only (and aria-disabled) rather than disabled, so a keyboard user keeps their focus. */
  locked?: boolean;
  inputRef?: Ref<HTMLInputElement>;
  onBlur?: TextFieldProps['onBlur'];
}

/** A password input with a show/hide button named after the field ("Show new password"). */
export function PasswordField({
  label,
  name,
  value,
  onChange,
  autoComplete,
  error,
  hint,
  locked = false,
  inputRef,
  onBlur,
}: PasswordFieldProps) {
  const [visible, setVisible] = useState(false);
  const lower = label.toLowerCase();
  return (
    <TextField
      label={label}
      name={name}
      type={visible ? 'text' : 'password'}
      autoComplete={autoComplete}
      fullWidth
      value={value}
      onChange={(event) => {
        onChange(event.target.value);
      }}
      {...(onBlur ? { onBlur } : {})}
      error={Boolean(error)}
      helperText={error ?? hint}
      inputRef={inputRef}
      sx={{ '& .MuiInputBase-input[aria-disabled="true"]': { cursor: 'not-allowed', color: 'text.disabled' } }}
      slotProps={{
        htmlInput: {
          'aria-required': true,
          autoCapitalize: 'none',
          spellCheck: false,
          ...(locked ? { readOnly: true, 'aria-disabled': true } : {}),
        },
        input: {
          endAdornment: (
            <InputAdornment position="end">
              <IconButton
                edge="end"
                aria-label={visible ? `Hide ${lower}` : `Show ${lower}`}
                onClick={() => {
                  setVisible((shown) => !shown);
                }}
              >
                {visible ? <VisibilityOffIcon /> : <VisibilityIcon />}
              </IconButton>
            </InputAdornment>
          ),
        },
      }}
    />
  );
}
