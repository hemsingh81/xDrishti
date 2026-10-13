import { FormControlLabel, FormHelperText, Switch } from '@mui/material';

interface EnabledSwitchProps {
  enabled: boolean;
  onChange: (enabled: boolean) => void;
}

/** Enabled, or paused: a paused service does not run by itself (Run now still works). */
export function EnabledSwitch({ enabled, onChange }: EnabledSwitchProps) {
  return (
    <div>
      <FormControlLabel
        control={
          <Switch
            checked={enabled}
            onChange={(event) => {
              onChange(event.target.checked);
            }}
          />
        }
        label="Enabled"
      />
      <FormHelperText sx={{ mt: -0.5, ml: 6 }}>
        A paused service does not run by itself (Run now still works).
      </FormHelperText>
    </div>
  );
}
