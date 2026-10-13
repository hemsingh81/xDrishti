import { Chip, Stack } from '@mui/material';
import type { ServiceView } from '@/shared/api';
import { executionFacts } from '../lib/labels';

/** Retries, timeout and catch-up as small chips. */
export function ExecutionChips({ execution }: { execution: ServiceView['execution'] }) {
  return (
    <Stack direction="row" useFlexGap sx={{ gap: 0.5, flexWrap: 'wrap', mt: 0.75 }}>
      {executionFacts(execution).map((fact) => (
        <Chip key={fact} size="small" variant="outlined" label={fact} />
      ))}
    </Stack>
  );
}
