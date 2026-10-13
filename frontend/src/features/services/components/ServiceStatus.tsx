import { Box } from '@mui/material';
import type { ServiceView } from '@/shared/api';
import { RunProgress } from './RunProgress';
import { ServiceStatusChip } from './ServiceStatusChip';

/** The status chip and, while the service runs a data job, that job's progress. */
export function ServiceStatus({ service }: { service: ServiceView }) {
  const jobId = service.status === 'running' ? service.lastRun?.dataJobId : null;
  return (
    <Box>
      <ServiceStatusChip status={service.status} />
      {jobId ? <RunProgress jobId={jobId} /> : null}
    </Box>
  );
}
