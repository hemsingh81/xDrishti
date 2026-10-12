import { Checkbox, FormControlLabel, Grid, TextField } from '@mui/material';
import { minAcquiredOn, todayIso } from '@/shared/lib/dates';
import type { FieldErrors } from '../lib/fieldErrors';
import type { PortfolioFormState } from '../lib/portfolioForm';
import { AutoRefreshSettings } from './AutoRefreshSettings';

interface PortfolioFieldsProps {
  idPrefix: string;
  state: PortfolioFormState;
  errors: FieldErrors;
  /** The account's import state: not connected (or no account yet), the first import waits for a sync. */
  firstImport: { hasHoldings: boolean; connected: boolean };
  onChange: (patch: Partial<PortfolioFormState>) => void;
}

/** What saving the date does, for the helper line under the field. */
function firstImportHelp({ hasHoldings, connected }: PortfolioFieldsProps['firstImport']): string {
  if (hasHoldings) {
    return 'The date holdings from the first import get. Changing it re-dates the holdings still on the default date.';
  }
  const first =
    'The date holdings from the first import get. Holdings are imported when you save; after that they refresh automatically and new holdings are dated the day they appear.';
  return connected ? first : `${first} Not connected yet: the first sync after connecting imports them with this date.`;
}

/** First import date (always editable), "include in totals" and the auto refresh; used by the Portfolio tab and the wizard. */
export function PortfolioFields({ idPrefix, state, errors, firstImport, onChange }: PortfolioFieldsProps) {
  return (
    <Grid container spacing={2} sx={{ alignItems: 'flex-start' }}>
      <Grid size={{ xs: 12, sm: 7, md: 6 }}>
        <TextField
          type="date"
          fullWidth
          id={`${idPrefix}-first-import-date`}
          label="First import date"
          value={state.firstImportDate}
          error={errors.firstImportDate !== undefined}
          helperText={errors.firstImportDate ?? firstImportHelp(firstImport)}
          onChange={(e) => {
            onChange({ firstImportDate: e.target.value });
          }}
          slotProps={{ inputLabel: { shrink: true }, htmlInput: { min: minAcquiredOn, max: todayIso() } }}
        />
      </Grid>
      <Grid size={{ xs: 12, sm: 5, md: 6 }}>
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
