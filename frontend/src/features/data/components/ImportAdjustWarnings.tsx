import { Alert, Box, Checkbox, FormControlLabel, Typography } from '@mui/material';
import { useId } from 'react';
import type { ActionsImportResult } from '@/shared/api';
import { formatDate, formatNumber } from '@/shared/lib/format';

interface ImportAdjustWarningsProps {
  result: ActionsImportResult;
  /** The owner ticked that they understand; Import stays disabled until then. */
  consent: boolean;
  onConsentChange: (consent: boolean) => void;
}

/**
 * Lines of a preview whose ex-date is on or before the day their instrument's CSV files end: those prices are adjusted
 * at the source already. Lists them (the API sends the first 50) and asks for explicit consent before Import is enabled.
 */
export function ImportAdjustWarnings({ result, consent, onConsentChange }: ImportAdjustWarningsProps) {
  const listId = useId();
  const count = result.adjustsCsvTwice ?? 0;
  const warnings = result.warnings ?? [];
  if (count <= 0) return null;
  return (
    <Alert severity="warning" role="note" sx={{ '& .MuiAlert-message': { width: '100%' } }}>
      <Typography sx={{ fontWeight: 600 }} id={listId}>
        {count === 1
          ? '1 line adjusts history that is already adjusted'
          : `${formatNumber(count)} lines adjust history that is already adjusted`}
      </Typography>
      <Typography variant="body2" sx={{ mb: 1 }}>
        The CSV files of these instruments are adjusted at the source up to the day shown. Importing the lines would
        adjust those prices a second time.
      </Typography>
      <Box component="ul" aria-labelledby={listId} sx={{ m: 0, pl: 3, maxHeight: 160, overflowY: 'auto' }}>
        {warnings.map((warning) => (
          <li key={warning.line}>
            Line {warning.line}: {warning.symbol}, ex-date {formatDate(warning.exDate)} — CSV files adjusted through{' '}
            {formatDate(warning.csvAdjustedThrough)}
          </li>
        ))}
      </Box>
      {count > warnings.length && (
        <Typography variant="body2">The first {formatNumber(warnings.length)} lines are listed.</Typography>
      )}
      <FormControlLabel
        sx={{ mt: 1 }}
        control={
          <Checkbox
            checked={consent}
            onChange={(event) => {
              onConsentChange(event.target.checked);
            }}
          />
        }
        label="I understand these actions adjust history that is already adjusted"
      />
    </Alert>
  );
}
