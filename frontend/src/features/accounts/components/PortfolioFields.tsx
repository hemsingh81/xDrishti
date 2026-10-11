import { Checkbox, FormControlLabel, Grid, TextField, Typography } from '@mui/material';
import type { PortfolioBucket } from '@/shared/api';
import { minAcquiredOn, todayIso } from '@/shared/lib/dates';
import type { FieldErrors } from '../lib/fieldErrors';
import { customBuckets, type PortfolioFormState } from '../lib/portfolioForm';
import { AutoRefreshSettings } from './AutoRefreshSettings';

interface PortfolioFieldsProps {
  idPrefix: string;
  state: PortfolioFormState;
  errors: FieldErrors;
  buckets: PortfolioBucket[];
  /** Name of the account's own default bucket. */
  ownBucketName: string;
  /** The account does not exist yet (wizard): its own bucket is created automatically. */
  ownBucketPending?: boolean;
  /** The account's import state: with holdings the date is read-only; not connected, the import waits for a sync. */
  firstImport: { hasHoldings: boolean; connected: boolean };
  onChange: (patch: Partial<PortfolioFormState>) => void;
}

/** Default bucket, first import date, "include in totals" and the auto refresh; used by the Portfolio tab and the wizard. The bucket always has a value. */
export function PortfolioFields({
  idPrefix,
  state,
  errors,
  buckets,
  ownBucketName,
  ownBucketPending = false,
  firstImport,
  onChange,
}: PortfolioFieldsProps) {
  const custom = customBuckets(buckets);
  const known = !state.defaultBucket || custom.some((b) => b.name === state.defaultBucket);
  return (
    <Grid container spacing={2} sx={{ alignItems: 'flex-start' }}>
      <Grid size={{ xs: 12, md: 5 }}>
        <TextField
          select
          fullWidth
          label="Default bucket"
          id={`${idPrefix}-bucket`}
          value={state.defaultBucket}
          onChange={(e) => {
            onChange({ defaultBucket: e.target.value });
          }}
          error={Boolean(errors.defaultBucket)}
          helperText={errors.defaultBucket ?? 'Where imported holdings go. Add more buckets below.'}
          // A native select with the account's own bucket has the value '': keep the label up so it never covers the text.
          slotProps={{ select: { native: true }, inputLabel: { shrink: true } }}
        >
          <option value="">
            {ownBucketPending
              ? `${ownBucketName} — account’s own bucket (created automatically)`
              : `${ownBucketName} (default)`}
          </option>
          {!known && <option value={state.defaultBucket}>{state.defaultBucket}</option>}
          {custom.map((bucket) => (
            <option key={bucket.id} value={bucket.name}>
              {bucket.name}
            </option>
          ))}
        </TextField>
      </Grid>
      <Grid size={{ xs: 12, sm: 6, md: 3 }}>
        {firstImport.hasHoldings ? (
          <Typography variant="body2" sx={{ pt: 1 }}>
            First import date: <strong>{state.firstImportDate}</strong>
          </Typography>
        ) : (
          <TextField
            type="date"
            fullWidth
            id={`${idPrefix}-first-import-date`}
            label="First import date"
            value={state.firstImportDate}
            error={errors.firstImportDate !== undefined}
            helperText={errors.firstImportDate}
            onChange={(e) => {
              onChange({ firstImportDate: e.target.value });
            }}
            slotProps={{ inputLabel: { shrink: true }, htmlInput: { min: minAcquiredOn, max: todayIso() } }}
          />
        )}
      </Grid>
      <Grid size={{ xs: 12, sm: 6, md: 4 }}>
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
      <Grid size={12}>
        <Typography variant="caption" color="text.secondary">
          {firstImport.hasHoldings
            ? 'Holdings refresh automatically; new holdings are dated the day they appear.'
            : 'Holdings are imported when you save (dated with this date, editable per holding on the Portfolio screen). After that they refresh automatically; new holdings are dated the day they appear.'}
          {!firstImport.hasHoldings &&
            !firstImport.connected &&
            ' Not connected yet: they will import on the first sync after connecting.'}
        </Typography>
      </Grid>
      <Grid size={12}>
        <AutoRefreshSettings
          idPrefix={idPrefix}
          value={state.refresh}
          errors={errors}
          onChange={(patch) => {
            onChange({ refresh: { ...state.refresh, ...patch } });
          }}
        />
      </Grid>
    </Grid>
  );
}
