import { Chip, Stack } from '@mui/material';
import { basisFlags, stateLabel } from '../lib/planText';

/** State of the plan as text, then the basis flags (always visible, never colour alone). */
export function PlanFlags({ plan }: { plan: { state: string; basis: string; provisional: boolean } }) {
  const f = basisFlags(plan);
  return (
    <Stack direction="row" sx={{ flexWrap: 'wrap', gap: 0.75 }}>
      <Chip size="small" color="primary" label={`State: ${stateLabel(plan.state)}`} />
      {f.provisional && <Chip size="small" variant="outlined" label="Provisional" />}
      {f.gateUnavailable && <Chip size="small" variant="outlined" color="warning" label="Market gate unavailable" />}
      {f.openBookUnavailable && <Chip size="small" variant="outlined" color="warning" label="Open book unavailable" />}
    </Stack>
  );
}
