import { Grid, TextField } from '@mui/material';
import type { IdentityFormState } from '../lib/connectionForm';
import type { FieldErrors } from '../lib/fieldErrors';

interface IdentityFieldsProps {
  idPrefix: string;
  state: IdentityFormState;
  errors: FieldErrors;
  /** Shown under the name, e.g. that it was suggested from the broker. */
  nameHint?: string;
  onChange: (patch: Partial<IdentityFormState>) => void;
}

/** Display name, colour and notes: shared by the Connection tab and the wizard's Identity step. */
export function IdentityFields({ idPrefix, state, errors, nameHint, onChange }: IdentityFieldsProps) {
  return (
    <Grid container spacing={2}>
      <Grid size={{ xs: 12, sm: 8, md: 6 }}>
        <TextField
          fullWidth
          required
          label="Display name"
          id={`${idPrefix}-name`}
          value={state.name}
          placeholder="e.g. Dhan · Swing 2"
          onChange={(e) => {
            onChange({ name: e.target.value });
          }}
          error={Boolean(errors.name)}
          helperText={errors.name ?? nameHint}
          slotProps={{ htmlInput: { maxLength: 80 } }}
        />
      </Grid>
      <Grid size={{ xs: 12, sm: 4, md: 2 }}>
        <TextField
          fullWidth
          type="color"
          label="Colour"
          id={`${idPrefix}-color`}
          value={state.color}
          onChange={(e) => {
            onChange({ color: e.target.value });
          }}
          error={Boolean(errors.color)}
          helperText={errors.color}
          slotProps={{ inputLabel: { shrink: true } }}
        />
      </Grid>
      <Grid size={12}>
        <TextField
          fullWidth
          multiline
          minRows={1}
          maxRows={4}
          label="Notes"
          id={`${idPrefix}-notes`}
          value={state.notes}
          placeholder="e.g. used for long-term SIP-style buys"
          onChange={(e) => {
            onChange({ notes: e.target.value });
          }}
          error={Boolean(errors.notes)}
          helperText={errors.notes}
        />
      </Grid>
    </Grid>
  );
}
