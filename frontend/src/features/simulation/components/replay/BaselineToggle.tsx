import { Checkbox, FormControlLabel, FormGroup, FormHelperText } from '@mui/material';

/** The frozen-mode option that adds the no-selection baseline arm (SR-02). */
export function BaselineToggle({ checked, onChange }: { checked: boolean; onChange: (value: boolean) => void }) {
  return (
    <FormGroup>
      <FormControlLabel
        control={
          <Checkbox
            checked={checked}
            onChange={(e) => {
              onChange(e.target.checked);
            }}
          />
        }
        label="With no-selection baseline"
      />
      <FormHelperText sx={{ mt: -0.5, ml: 4 }}>
        Also runs a second replay that takes every candidate the system finds, at equal risk, and compares the two. It
        is not random stocks. It adds one more replay look.
      </FormHelperText>
    </FormGroup>
  );
}
