import { Chip } from '@mui/material';
import type { ServiceRun } from '@/shared/api';
import { triggerLabel } from '../lib/labels';

/** What started a run: Scheduled · Catch-up · After another service · Manual. */
export function TriggerChip({ run }: { run: Pick<ServiceRun, 'trigger'> }) {
  return <Chip size="small" variant="outlined" label={triggerLabel(run)} />;
}
