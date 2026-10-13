import CheckIcon from '@mui/icons-material/CheckCircleOutlined';
import DnsIcon from '@mui/icons-material/DnsOutlined';
import ErrorIcon from '@mui/icons-material/ErrorOutlineOutlined';
import PauseIcon from '@mui/icons-material/PauseCircleOutlineOutlined';
import SyncIcon from '@mui/icons-material/Sync';
import { Box, Card } from '@mui/material';
import type { ServiceView } from '@/shared/api';
import { summarise } from '../lib/summary';
import { SummaryCount } from './SummaryCount';
import { SummaryRun } from './SummaryRun';

/**
 * The state of all services at a glance, in one wrapping row: how many there are and how many are healthy, running,
 * paused or failed; when the first of them starts next (with a countdown) and which run ended last.
 */
export function SummaryStrip({ services }: { services: readonly ServiceView[] }) {
  const summary = summarise(services);
  return (
    <Card
      component="section"
      aria-label="Services summary"
      sx={(theme) => ({
        mb: 1.5,
        px: 2,
        py: 1.25,
        flexShrink: 0,
        display: 'flex',
        flexWrap: 'wrap',
        columnGap: 4,
        rowGap: 1.25,
        // Wide: the counts, the next run and the last run on one line.
        [theme.breakpoints.up('lg')]: { display: 'grid', gridTemplateColumns: 'auto minmax(0, 1fr) minmax(0, 1fr)' },
      })}
    >
      <Box
        component="ul"
        aria-label="Service counts"
        sx={{ m: 0, p: 0, listStyle: 'none', display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 2.5 }}
      >
        <SummaryCount icon={DnsIcon} label="Services" value={summary.total} />
        <SummaryCount icon={CheckIcon} label="Healthy" value={summary.healthy} tone="success" />
        {summary.running > 0 && <SummaryCount icon={SyncIcon} label="Running" value={summary.running} tone="info" />}
        <SummaryCount icon={PauseIcon} label="Paused" value={summary.paused} tone="warning" />
        <SummaryCount icon={ErrorIcon} label="Failed" value={summary.failed} tone="error" />
      </Box>
      <SummaryRun
        label="Next run"
        at={summary.next?.at ?? null}
        serviceName={summary.next?.service.name}
        countdown
        empty="Nothing is scheduled"
      />
      <SummaryRun
        label="Last run"
        at={summary.last?.at ?? null}
        serviceName={summary.last?.service.name}
        status={summary.last?.run.status}
        empty="Nothing has run yet"
      />
    </Card>
  );
}
