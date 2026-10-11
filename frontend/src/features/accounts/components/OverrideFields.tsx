import { Grid, TextField, Typography } from '@mui/material';
import type { RiskProfile } from '@/shared/api';
import { formatInr } from '@/shared/lib/format';
import type { FieldErrors } from '../lib/fieldErrors';
import { effectiveLimits, type OverrideKey, type TradingFormState } from '../lib/tradingForm';

const overrideFields: { key: OverrideKey; label: string; step: string }[] = [
  { key: 'riskPerTradePct', label: 'Risk per trade (%)', step: 'any' },
  { key: 'maxNewEntriesPerDay', label: 'Max new entries per day', step: '1' },
  { key: 'maxOpenPositions', label: 'Max open positions', step: '1' },
  { key: 'dailyLossPct', label: 'Daily loss limit (%)', step: 'any' },
];

interface OverrideFieldsProps {
  idPrefix: string;
  state: TradingFormState;
  errors: FieldErrors;
  profile: RiskProfile | undefined;
  onChange: (patch: Partial<TradingFormState>) => void;
}

/** Per-account overrides (blank = use the profile) and what actually applies. */
export function OverrideFields({ idPrefix, state, errors, profile, onChange }: OverrideFieldsProps) {
  const effective = effectiveLimits(state, profile);
  return (
    <>
      <Typography variant="h3">Overrides for this account (blank = use the profile)</Typography>
      <Grid container spacing={2}>
        {overrideFields.map(({ key, label, step }) => (
          <Grid key={key} size={{ xs: 12, sm: 6, md: 3 }}>
            <TextField
              fullWidth
              type="number"
              label={label}
              id={`${idPrefix}-ov-${key}`}
              value={state.overrides[key]}
              placeholder={profile ? String(profile[key]) : undefined}
              onChange={(e) => {
                onChange({ overrides: { ...state.overrides, [key]: e.target.value } });
              }}
              error={Boolean(errors[`overrides.${key}`])}
              helperText={errors[`overrides.${key}`]}
              slotProps={{ htmlInput: { min: 0, step, inputMode: 'decimal' } }}
            />
          </Grid>
        ))}
      </Grid>
      {effective && (
        <Typography variant="body2" color="text.secondary" role="status">
          Effective: {effective.riskPerTradePct}% risk per trade
          {effective.rupeesPerR !== null && ` = ${formatInr(effective.rupeesPerR)} per 1R`} · max{' '}
          {effective.maxNewEntriesPerDay} new entries/day · max {effective.maxOpenPositions} open · daily loss{' '}
          {effective.dailyLossPct}%
        </Typography>
      )}
    </>
  );
}
