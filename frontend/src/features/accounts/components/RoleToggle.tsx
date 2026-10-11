import { Box, Checkbox, FormControlLabel, FormHelperText, Radio, RadioGroup, Typography } from '@mui/material';
import type { ReactNode } from 'react';
import { roleInfo, type RoleKey } from '../lib/labels';
import type { DataKind } from '../lib/roleChanges';

interface RoleToggleProps {
  roleKey: RoleKey;
  checked: boolean;
  disabled?: boolean;
  /** Why the box cannot be changed (shown under the description). */
  note?: ReactNode;
  onChange: (checked: boolean) => void;
  /** Data only: primary/standby choice. */
  dataKind?: { value: DataKind; disabled: boolean; onChange: (kind: DataKind) => void };
}

/** One role card: checkbox, what the role does, and (for Data) the Primary/Standby choice. */
export function RoleToggle({ roleKey, checked, disabled, note, onChange, dataKind }: RoleToggleProps) {
  const info = roleInfo[roleKey];
  return (
    <Box
      sx={{
        border: 1,
        borderColor: checked ? 'primary.main' : 'divider',
        borderRadius: 2,
        p: 1.5,
        bgcolor: checked ? 'action.selected' : 'transparent',
      }}
    >
      <FormControlLabel
        label={<strong>{info.label}</strong>}
        control={
          <Checkbox
            checked={checked}
            disabled={disabled}
            onChange={(e) => {
              onChange(e.target.checked);
            }}
          />
        }
      />
      <Typography variant="body2" color="text.secondary" sx={{ ml: 4 }}>
        {info.description}
      </Typography>
      {note && <FormHelperText sx={{ ml: 4 }}>{note}</FormHelperText>}
      {dataKind && (
        <RadioGroup
          row
          aria-label="Data role kind"
          value={dataKind.value}
          sx={{ ml: 3 }}
          onChange={(e) => {
            dataKind.onChange(e.target.value === 'primary' ? 'primary' : 'standby');
          }}
        >
          <FormControlLabel
            value="primary"
            label="Primary"
            control={<Radio size="small" disabled={!checked || dataKind.disabled} />}
          />
          <FormControlLabel
            value="standby"
            label="Standby (automatic failover)"
            control={<Radio size="small" disabled={!checked || dataKind.disabled} />}
          />
        </RadioGroup>
      )}
    </Box>
  );
}
