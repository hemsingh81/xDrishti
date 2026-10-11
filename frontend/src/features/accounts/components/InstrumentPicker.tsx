import { Checkbox, FormControl, FormControlLabel, FormGroup, FormHelperText, FormLabel, Grid } from '@mui/material';
import type { FieldErrors } from '../lib/fieldErrors';
import { instrumentOptions, type InstrumentGroup, type TradingFormState } from '../lib/tradingForm';

const groups: { key: InstrumentGroup; label: string; style: 'intraday' | 'swing' }[] = [
  { key: 'intraday', label: 'Intraday instruments', style: 'intraday' },
  { key: 'swingBuy', label: 'Swing BUY instruments', style: 'swing' },
  { key: 'swingSell', label: 'Swing SELL instruments (optional)', style: 'swing' },
];

interface InstrumentPickerProps {
  state: TradingFormState;
  errors: FieldErrors;
  onChange: (patch: Partial<TradingFormState>) => void;
}

/** Instrument types allowed per style; a group is inactive while its style is off. */
export function InstrumentPicker({ state, errors, onChange }: InstrumentPickerProps) {
  function toggle(group: InstrumentGroup, option: string, checked: boolean) {
    const current = state.instruments[group];
    const next = checked ? [...current, option] : current.filter((o) => o !== option);
    onChange({ instruments: { ...state.instruments, [group]: next } });
  }

  return (
    <Grid container spacing={2}>
      {groups.map(({ key, label, style }) => {
        const error = errors[`instruments.${key}`];
        const inactive = !state[style];
        return (
          <Grid key={key} size={{ xs: 12, sm: 4 }}>
            <FormControl component="fieldset" error={Boolean(error)} disabled={inactive}>
              <FormLabel component="legend">{label}</FormLabel>
              <FormGroup row>
                {instrumentOptions[key].map((option) => (
                  <FormControlLabel
                    key={option}
                    label={option}
                    control={
                      <Checkbox
                        checked={state.instruments[key].includes(option)}
                        onChange={(e) => {
                          toggle(key, option, e.target.checked);
                        }}
                      />
                    }
                  />
                ))}
              </FormGroup>
              {inactive && <FormHelperText>Switch on the {style} style to use these.</FormHelperText>}
              {error && <FormHelperText>{error}</FormHelperText>}
            </FormControl>
          </Grid>
        );
      })}
    </Grid>
  );
}
