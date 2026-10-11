import { Checkbox, FormControlLabel, Grid, TextField } from '@mui/material';
import type { PortfolioBucket } from '@/shared/api';
import type { FieldErrors } from '../lib/fieldErrors';
import { earliestHistoryDate, todayIso, type PortfolioFormState } from '../lib/portfolioForm';

interface PortfolioFieldsProps {
  idPrefix: string;
  state: PortfolioFormState;
  errors: FieldErrors;
  buckets: PortfolioBucket[];
  onChange: (patch: Partial<PortfolioFormState>) => void;
}

/** Default bucket, history start and "include in totals"; used by the Portfolio tab and the wizard. */
export function PortfolioFields({ idPrefix, state, errors, buckets, onChange }: PortfolioFieldsProps) {
  return (
    <Grid container spacing={2} sx={{ alignItems: 'flex-start' }}>
      <Grid size={{ xs: 12, sm: 4 }}>
        <TextField
          select
          fullWidth
          required
          label="Default bucket"
          id={`${idPrefix}-bucket`}
          value={state.defaultBucket}
          onChange={(e) => {
            onChange({ defaultBucket: e.target.value });
          }}
          error={Boolean(errors.defaultBucket)}
          helperText={errors.defaultBucket}
          slotProps={{ select: { native: true } }}
        >
          <option value="">Choose…</option>
          {buckets.map((bucket) => (
            <option key={bucket.id} value={bucket.name}>
              {bucket.name}
            </option>
          ))}
        </TextField>
      </Grid>
      <Grid size={{ xs: 12, sm: 4 }}>
        <TextField
          fullWidth
          type="date"
          label="Import trade history from"
          id={`${idPrefix}-history`}
          value={state.historyFrom}
          onChange={(e) => {
            onChange({ historyFrom: e.target.value });
          }}
          error={Boolean(errors.historyFrom)}
          helperText={errors.historyFrom}
          slotProps={{
            inputLabel: { shrink: true },
            htmlInput: { min: earliestHistoryDate, max: todayIso() },
          }}
        />
      </Grid>
      <Grid size={{ xs: 12, sm: 4 }}>
        <FormControlLabel
          label="Include in portfolio totals"
          control={
            <Checkbox
              checked={state.includeInTotals}
              onChange={(e) => {
                onChange({ includeInTotals: e.target.checked });
              }}
            />
          }
        />
      </Grid>
    </Grid>
  );
}
