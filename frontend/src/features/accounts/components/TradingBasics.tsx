import {
  Checkbox,
  FormControl,
  FormControlLabel,
  FormGroup,
  FormHelperText,
  FormLabel,
  Grid,
  TextField,
} from '@mui/material';
import type { RiskProfile } from '@/shared/api';
import type { FieldErrors } from '../lib/fieldErrors';
import type { TradingFormState } from '../lib/tradingForm';

interface TradingBasicsProps {
  idPrefix: string;
  state: TradingFormState;
  errors: FieldErrors;
  profiles: RiskProfile[];
  onChange: (patch: Partial<TradingFormState>) => void;
}

/** Mode, risk profile, capital and trading styles. */
export function TradingBasics({ idPrefix, state, errors, profiles, onChange }: TradingBasicsProps) {
  return (
    <>
      <Grid container spacing={2}>
        <Grid size={{ xs: 12, sm: 6, md: 3 }}>
          <TextField
            select
            fullWidth
            label="Mode"
            id={`${idPrefix}-mode`}
            value={state.mode}
            onChange={(e) => {
              onChange({ mode: e.target.value });
            }}
            error={Boolean(errors.mode)}
            helperText={errors.mode ?? 'Automated modes arrive in a later phase.'}
            slotProps={{ select: { native: true } }}
          >
            <option value="paper">Paper</option>
            <option value="manual_live">Manual live</option>
            <option disabled>Semi-auto / auto (later phase)</option>
          </TextField>
        </Grid>
        <Grid size={{ xs: 12, sm: 6, md: 3 }}>
          <TextField
            select
            fullWidth
            required
            label="Risk profile"
            id={`${idPrefix}-profile`}
            value={state.riskProfileId}
            onChange={(e) => {
              onChange({ riskProfileId: e.target.value });
            }}
            error={Boolean(errors.riskProfileId)}
            helperText={errors.riskProfileId}
            slotProps={{ select: { native: true } }}
          >
            <option value="">Choose…</option>
            {profiles.map((profile) => (
              <option key={profile.id} value={profile.id}>
                {profile.name}
              </option>
            ))}
          </TextField>
        </Grid>
        <Grid size={{ xs: 12, sm: 6, md: 3 }}>
          <TextField
            select
            fullWidth
            label="Capital source"
            id={`${idPrefix}-source`}
            value={state.capitalSource}
            onChange={(e) => {
              onChange({ capitalSource: e.target.value });
            }}
            error={Boolean(errors.capitalSource)}
            helperText={errors.capitalSource}
            slotProps={{ select: { native: true } }}
          >
            <option value="fixed">Fixed amount</option>
            <option value="broker_funds">Broker funds (capped)</option>
          </TextField>
        </Grid>
        <Grid size={{ xs: 12, sm: 6, md: 3 }}>
          <TextField
            fullWidth
            label={state.capitalSource === 'fixed' ? 'Capital (₹)' : 'Capital cap (₹)'}
            id={`${idPrefix}-capital`}
            type="number"
            value={state.capital}
            onChange={(e) => {
              onChange({ capital: e.target.value });
            }}
            error={Boolean(errors.capitalAmount)}
            helperText={errors.capitalAmount}
            slotProps={{ htmlInput: { min: 0, step: 'any', inputMode: 'decimal' } }}
          />
        </Grid>
      </Grid>
      <FormControl component="fieldset" error={Boolean(errors.styles)}>
        <FormLabel component="legend">Styles</FormLabel>
        <FormGroup row>
          <FormControlLabel
            label="Intraday"
            control={
              <Checkbox
                checked={state.intraday}
                onChange={(e) => {
                  onChange({ intraday: e.target.checked });
                }}
              />
            }
          />
          <FormControlLabel
            label="Swing"
            control={
              <Checkbox
                checked={state.swing}
                onChange={(e) => {
                  onChange({ swing: e.target.checked });
                }}
              />
            }
          />
        </FormGroup>
        {errors.styles && <FormHelperText>{errors.styles}</FormHelperText>}
      </FormControl>
    </>
  );
}
