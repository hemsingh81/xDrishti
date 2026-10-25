import { Chip, Stack } from '@mui/material';
import type { Schemas } from '@/shared/api';
import { flagChips, legacyFlagsLabel } from '../lib/flagText';
import { basisFlags, stateLabel } from '../lib/planText';

interface Props {
  plan: { state: string; basis: string; provisional: boolean; flags?: Schemas['PlanFlagsView'] | null };
}

/** State of the plan as text, then the flags (always visible, never colour alone). Stored flags when the plan has them; otherwise the old exact-marker reading of the basis, labelled. */
export function PlanFlags({ plan }: Props) {
  const stored = plan.flags ?? null;
  const legacy = stored ? null : basisFlags(plan);
  return (
    <Stack direction="row" sx={{ flexWrap: 'wrap', gap: 0.75 }}>
      <Chip size="small" color="primary" label={`State: ${stateLabel(plan.state)}`} />
      {plan.provisional && <Chip size="small" variant="outlined" label="Provisional" />}
      {stored &&
        flagChips(stored).map((c) => (
          <Chip key={c.key} size="small" variant="outlined" color={c.notable ? 'warning' : 'default'} label={c.label} />
        ))}
      {legacy?.gateUnavailable && (
        <Chip size="small" variant="outlined" color="warning" label="Market gate unavailable" />
      )}
      {legacy?.openBookUnavailable && (
        <Chip size="small" variant="outlined" color="warning" label="Open book unavailable" />
      )}
      {legacy && <Chip size="small" variant="outlined" label={legacyFlagsLabel} />}
    </Stack>
  );
}
