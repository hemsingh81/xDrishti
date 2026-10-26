import { Alert, Stack } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { Link as RouterLink, useSearchParams } from 'react-router';
import { ErrorState, LoadingState, PageHeader } from '@/shared/ui';
import { capabilitiesQuery } from '../api/capabilitiesApi';
import { ExperimentForm } from './ExperimentForm';
import { JobPanel } from './JobPanel';
import { RunPicker } from './RunPicker';

const capability = 'experiments_ui';

/** Replay one change: one knob of one stored run, judged by the learning ledger. Judging only; nothing is applied. */
export function ExperimentPage() {
  const query = useQuery(capabilitiesQuery());
  const [params, setParams] = useSearchParams();
  const [runKey, setRunKey] = useState('');
  const jobId = params.get('job');
  const enabled = query.data?.items.find((item) => item.name === capability)?.enabled ?? false;

  return (
    <>
      <PageHeader
        title="Replay one change"
        description="Change one thing of a stored run, replay it and read the judgement. Every replay counts as a look. Nothing is applied."
      />
      {query.isPending && <LoadingState />}
      {query.isError && <ErrorState error={query.error} />}
      {query.data && !enabled && (
        <Alert severity="info">
          Replaying from the screen is switched off. Switch it on in{' '}
          <RouterLink to="/owner-acts">the owner-act switches</RouterLink>.
        </Alert>
      )}
      {query.data && enabled && (
        <Stack spacing={2}>
          {jobId && <JobPanel key={jobId} id={jobId} />}
          <RunPicker value={runKey} onChange={setRunKey} />
          <ExperimentForm
            key={runKey}
            runKey={runKey}
            onStarted={(job) => {
              setParams({ job: job.id });
            }}
          />
        </Stack>
      )}
    </>
  );
}
