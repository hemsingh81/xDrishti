import { Box, Stack, Typography } from '@mui/material';
import type { ReactNode } from 'react';
import type { ServiceView } from '@/shared/api';
import { LastRun } from './LastRun';
import { NextRun } from './NextRun';
import { ScheduleSummary } from './ScheduleSummary';
import { ServiceActions, type ServiceActionHandlers } from './ServiceActions';
import { ServiceName } from './ServiceName';
import { ServiceStatus } from './ServiceStatus';

interface ServiceCardProps extends ServiceActionHandlers {
  service: ServiceView;
  names: ReadonlyMap<string, string>;
  busy: boolean;
}

function Fact({ label, children }: { label: string; children: ReactNode }) {
  return (
    <Box>
      <Typography variant="overline" color="text.secondary" component="div">
        {label}
      </Typography>
      {children}
    </Box>
  );
}

/** One service on a phone: the table's columns stacked, with the actions at the bottom. */
export function ServiceCard({ service, names, busy, ...handlers }: ServiceCardProps) {
  return (
    <Box
      component="li"
      aria-label={service.name}
      sx={{
        listStyle: 'none',
        p: 1.5,
        border: 1,
        borderColor: 'divider',
        borderRadius: 1,
        bgcolor: 'background.paper',
      }}
    >
      <Stack direction="row" sx={{ gap: 1, justifyContent: 'space-between', alignItems: 'flex-start' }}>
        <ServiceName service={service} />
        <ServiceStatus service={service} />
      </Stack>
      <Stack sx={{ gap: 1, mt: 1.25 }}>
        <Fact label="Schedule">
          <ScheduleSummary service={service} />
        </Fact>
        <Fact label="Runs as">
          <Typography variant="body2" className="mono">
            {service.runsAs}
          </Typography>
        </Fact>
        <Fact label="Last run">
          <LastRun run={service.lastRun} />
        </Fact>
        <Fact label="Next run (IST)">
          <NextRun service={service} names={names} />
        </Fact>
      </Stack>
      <Box sx={{ mt: 1.5 }}>
        <ServiceActions service={service} busy={busy} align="flex-start" {...handlers} />
      </Box>
    </Box>
  );
}
