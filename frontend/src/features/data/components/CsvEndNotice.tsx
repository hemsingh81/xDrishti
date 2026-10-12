import { Box, Typography } from '@mui/material';
import { InfoPopover } from './InfoPopover';

/**
 * The fact the owner must keep in mind, in one line: the CSV history is adjusted already and the screen checks it for
 * every action. The info button gives the whole explanation and what the ratios mean.
 */
export function CsvEndNotice() {
  return (
    <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.5, px: 1.5, pb: 0.5 }}>
      <Typography variant="caption" color="text.secondary" role="note" noWrap sx={{ minWidth: 0 }}>
        History from your CSV files is already adjusted at the source; an action that would adjust it twice asks first.
      </Typography>
      <InfoPopover label="About adjusted history and ratios">
        <Typography variant="body2" role="note" sx={{ mb: 1 }}>
          History loaded from your CSV files is already adjusted at the source. The check is automatic: an action whose
          ex-date is on or before the day an instrument’s CSV files end asks you to confirm before it adjusts those
          prices a second time.
        </Typography>
        <Typography variant="body2" color="text.secondary">
          Splits and bonus issues change the price scale. Bars stay as loaded; the applied actions adjust prices when
          you ask for adjusted prices (the switch in the bars panel). Split 1:5 is one share into five, so prices before
          the ex-date × 0.2; bonus 1:1 is one extra share for each held, so × 0.5.
        </Typography>
      </InfoPopover>
    </Box>
  );
}
