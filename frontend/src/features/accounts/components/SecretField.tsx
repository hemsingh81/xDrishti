import VisibilityIcon from '@mui/icons-material/VisibilityOutlined';
import VisibilityOffIcon from '@mui/icons-material/VisibilityOffOutlined';
import { IconButton, InputAdornment, TextField } from '@mui/material';
import { useState } from 'react';

interface SecretFieldProps {
  id: string;
  label: string;
  value: string;
  required?: boolean;
  error: string | undefined;
  helperText?: string;
  onChange: (value: string) => void;
}

/**
 * Write-only secret input: masked by default with a labelled show/hide toggle; never pre-filled, no autocomplete,
 * no spell-check (which would send the value to a dictionary service in some browsers).
 */
export function SecretField({ id, label, value, required = false, error, helperText, onChange }: SecretFieldProps) {
  const [visible, setVisible] = useState(false);
  return (
    <TextField
      fullWidth
      required={required}
      id={id}
      label={label}
      type={visible ? 'text' : 'password'}
      value={value}
      // "off" is ignored for password inputs; "new-password" keeps password managers from offering to save broker secrets.
      autoComplete="new-password"
      onChange={(e) => {
        onChange(e.target.value);
      }}
      error={Boolean(error)}
      helperText={error ?? helperText}
      slotProps={{
        htmlInput: { spellCheck: false, autoCapitalize: 'none', autoCorrect: 'off', 'data-1p-ignore': true },
        input: {
          endAdornment: (
            <InputAdornment position="end">
              <IconButton
                edge="end"
                aria-label={`${visible ? 'Hide' : 'Show'} ${label}`}
                aria-pressed={visible}
                onClick={() => {
                  setVisible((v) => !v);
                }}
              >
                {visible ? <VisibilityOffIcon fontSize="small" /> : <VisibilityIcon fontSize="small" />}
              </IconButton>
            </InputAdornment>
          ),
        },
      }}
    />
  );
}
