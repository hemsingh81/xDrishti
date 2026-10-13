import { Box, Card, Typography } from '@mui/material';
import type { ServiceView } from '@/shared/api';
import { RunFacts } from './RunFacts';
import { RunProgress } from './RunProgress';
import { ScheduleBlock } from './ScheduleBlock';
import { ServiceActions, type ServiceActionHandlers } from './ServiceActions';
import { ServiceCardHeader } from './ServiceCardHeader';

interface ServiceCardProps extends ServiceActionHandlers {
  service: ServiceView;
  names: ReadonlyMap<string, string>;
  busy: boolean;
}

/** The colour of the card's left bar by state; the status chip says the same in words, so colour is never alone. */
const accents: Readonly<Record<string, string>> = {
  healthy: 'success.main',
  running: 'info.main',
  failed: 'error.main',
  paused: 'text.disabled',
};

/**
 * One service: its state, what it does, when it runs next and how the last run went, the schedule in words with its
 * retries, timeout and catch-up, then Run now and the overflow menu. A failed, running or paused service looks it (the
 * left bar and the header); a paused card has a dashed border. While the end-of-day run works, its progress is shown.
 */
export function ServiceCard({ service, names, busy, ...handlers }: ServiceCardProps) {
  const jobId = service.status === 'running' ? service.lastRun?.dataJobId : null;
  return (
    <Card
      component="li"
      aria-label={service.name}
      sx={{
        position: 'relative',
        display: 'flex',
        flexDirection: 'column',
        overflow: 'hidden',
        listStyle: 'none',
        borderStyle: service.status === 'paused' ? 'dashed' : 'solid',
        '&::before': {
          content: '""',
          position: 'absolute',
          insetBlock: 0,
          left: 0,
          width: 4,
          bgcolor: accents[service.status] ?? 'divider',
        },
      }}
    >
      <ServiceCardHeader service={service} />
      <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1.5, flex: 1, py: 1.25, pr: 2, pl: 2.5 }}>
        <Typography
          variant="body2"
          color="text.secondary"
          title={service.description}
          sx={{
            display: '-webkit-box',
            WebkitLineClamp: 2,
            WebkitBoxOrient: 'vertical',
            overflow: 'hidden',
            minHeight: '2.9em',
          }}
        >
          {service.description}
        </Typography>
        <RunFacts service={service} names={names} />
        {jobId ? <RunProgress jobId={jobId} /> : null}
        <ScheduleBlock service={service} />
      </Box>
      <Box
        sx={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          flexWrap: 'wrap',
          gap: 1,
          py: 1,
          pr: 1.5,
          pl: 2.5,
          borderTop: 1,
          borderColor: 'divider',
        }}
      >
        <Typography variant="caption" color="text.secondary" sx={{ minWidth: 0 }}>
          Runs as <span className="mono">{service.runsAs}</span>
        </Typography>
        <ServiceActions service={service} busy={busy} {...handlers} />
      </Box>
    </Card>
  );
}
