import { Checkbox, FormControlLabel, Grid, TextField } from '@mui/material';
import type { PortfolioBucket } from '@/shared/api';
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
  onChange: (patch: Partial<PortfolioFormState>) => void;
}

/** Default bucket, "include in totals" and the auto refresh; used by the Portfolio tab and the wizard. The bucket always has a value. */
export function PortfolioFields({
  idPrefix,
  state,
  errors,
  buckets,
  ownBucketName,
  ownBucketPending = false,
  onChange,
}: PortfolioFieldsProps) {
  const custom = customBuckets(buckets);
  const known = !state.defaultBucket || custom.some((b) => b.name === state.defaultBucket);
  return (
    <Grid container spacing={2} sx={{ alignItems: 'flex-start' }}>
      <Grid size={{ xs: 12, sm: 8 }}>
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
          slotProps={{ select: { native: true } }}
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
