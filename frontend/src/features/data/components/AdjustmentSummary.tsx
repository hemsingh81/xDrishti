import { Box, Typography } from '@mui/material';
import type { BarsAdjustment } from '@/shared/api';
import { formatDate } from '@/shared/lib/format';
import { adjustedText, factorText, kindLabel } from '../lib/actions';

/**
 * What an adjusted read did: "Adjusted by ×0.2 for 1 action" with the actions it used, oldest first. The factor is the
 * strongest one, of the oldest bar shown; bars closer to the ex-date have a factor nearer 1.
 */
export function AdjustmentSummary({ adjustment }: { adjustment: BarsAdjustment }) {
  if (adjustment.applied === 0) {
    return (
      <Typography variant="body2" color="text.secondary">
        No applied corporate action changes these bars, so the prices are as loaded.
      </Typography>
    );
  }
  return (
    <Box component="section" aria-label="Adjustment">
      <Typography sx={{ fontWeight: 600 }}>{adjustedText(adjustment.factor, adjustment.applied)}</Typography>
      <Typography variant="body2" color="text.secondary">
        Open, high, low and close are multiplied by the factor of the actions after each bar, and the volume is divided
        by it. The oldest bar shown has the factor above.
      </Typography>
      <Box component="ul" aria-label="Actions applied" sx={{ m: 0, mt: 0.5, pl: 3 }}>
        {adjustment.actions.map((action) => (
          <li key={`${action.exDate}-${action.kind}`}>
            {formatDate(action.exDate)} — {kindLabel(action.kind)} {action.ratio} ({factorText(action.factor)})
          </li>
        ))}
      </Box>
    </Box>
  );
}
