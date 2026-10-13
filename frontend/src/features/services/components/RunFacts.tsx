import { Box, Stack } from '@mui/material';
import type { ServiceView } from '@/shared/api';
import { CardFact } from './CardFact';
import { Countdown } from './Countdown';
import { LastRun } from './LastRun';
import { NextRun } from './NextRun';

/** The two facts the owner looks for first, side by side while there is room: when it runs next and how the last run went. */
export function RunFacts({ service, names }: { service: ServiceView; names: ReadonlyMap<string, string> }) {
  const scheduled = service.enabled && service.nextRun !== null;
  return (
    <Box sx={{ display: 'grid', gap: 1.5, gridTemplateColumns: 'repeat(auto-fit, minmax(150px, 1fr))' }}>
      <CardFact label="Next run (IST)">
        <Stack direction="row" sx={{ gap: 0.75, alignItems: 'baseline', flexWrap: 'wrap' }}>
          <NextRun service={service} names={names} />
        </Stack>
        {scheduled && service.nextRun && <Countdown to={service.nextRun} />}
      </CardFact>
      <CardFact label="Last run (IST)">
        <LastRun run={service.lastRun} />
      </CardFact>
    </Box>
  );
}
