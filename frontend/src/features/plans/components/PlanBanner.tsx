import { Alert, Box } from '@mui/material';
import type { Schemas } from '@/shared/api';
import { noTradeSentence } from '../lib/flagText';

/**
 * The smart-rule lines of a plan as it was published: the full stored text of every flag (not clamped), and the gate's own
 * sentence on a no-trade day while the plan is open. Text only; React escapes it, so markup in a line is shown as written.
 */
export function PlanBanner({ flags, planOpen }: { flags: Schemas['PlanFlagsView']; planOpen: boolean }) {
  const lines = [...(planOpen && flags.gate === 'no_trade' ? [noTradeSentence] : []), ...flags.banner];
  if (lines.length === 0) return null;
  return (
    <Alert severity="warning" icon={false} aria-label="Smart rules">
      <Box component="ul" sx={{ m: 0, pl: 2 }}>
        {lines.map((line, i) => (
          <li key={`${String(i)}-${line.slice(0, 24)}`}>{line}</li>
        ))}
      </Box>
    </Alert>
  );
}
