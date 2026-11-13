import { Box, Link, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { Link as RouterLink } from 'react-router';
import { servicesListQuery, type Schemas } from '@/shared/api';
import { ErrorState, LoadingState } from '@/shared/ui';
import { autoEvidenceSetQuery, autoShadowSwitchQuery } from '../api/autoApi';

/** The forward-evidence services: outside the Auto chain (ADR 0252), each its own switch (OPS-01, ADR 0278). */
const evidence = [
  { id: 'shadow-write', name: 'Shadow write' },
  { id: 'shadow-grade', name: 'Shadow grade' },
  { id: 'shadow-digest', name: 'Shadow digest' },
  { id: 'paper-capture', name: 'Paper capture' },
  { id: 'paper-review', name: 'Paper review' },
  { id: 'history-eod', name: 'History end of day' },
  { id: 'history-backfill', name: 'History backfill' },
] as const;

/** Enabled and failed reads as failed; never as running. */
function stateOf(service: Schemas['ServiceView'] | undefined): string {
  if (!service) return 'missing';
  if (!service.enabled || service.status === 'paused') return 'paused';
  return service.status === 'failed' ? 'failed' : 'running';
}

/** ON from the seed row does not mean writing: with no evidence set stored and none nameable the writer writes nothing (AM-01b), and the screen says so. */
function shadowState(
  shadow: Schemas['ShadowSummaryView'] | undefined,
  failed: boolean,
  evidenceSet: Schemas['EvidenceSetView'] | undefined,
  evidenceSetFailed: boolean,
): string {
  if (failed) return 'unavailable';
  if (!shadow) return 'loading';
  if (!shadow.enabled) return 'OFF';
  if (evidenceSetFailed) return 'ON, evidence set unreadable';
  return evidenceSet?.state === 'pending' && evidenceSet.versions.length === 0
    ? 'ON, writing nothing: no evidence set'
    : 'ON';
}

function Row({ name, state, to, note }: { name: string; state: string; to: string; note?: string }) {
  return (
    <li>
      {name}: {state}.{note ? ` ${note}` : ''}{' '}
      <Link component={RouterLink} to={to}>
        Open {to.startsWith('/paper') ? 'Shadow tab' : 'Services'}
      </Link>
    </li>
  );
}

/** Read only: no control here turns anything on or off. A service the list does not hold reads missing; a failed read shows the error state, never running. */
export function EvidenceServices() {
  const services = useQuery(servicesListQuery);
  const shadow = useQuery(autoShadowSwitchQuery);
  const evidenceSet = useQuery(autoEvidenceSetQuery);
  const find = (id: string) => services.data?.items.find((item) => item.id === id);
  return (
    <Box component="section" aria-labelledby="evidence-services-heading">
      <Typography variant="h2" id="evidence-services-heading" sx={{ fontSize: '1.1rem', mb: 0.5 }}>
        Evidence services
      </Typography>
      <Typography color="text.secondary" sx={{ mb: 1 }}>
        Auto does not resume these. Each is its own switch, set where the link goes.
      </Typography>
      {services.isPending && <LoadingState label="Reading the evidence services…" />}
      {services.isError && <ErrorState error={services.error} />}
      {services.data && (
        <Stack component="ul" aria-label="Evidence services" spacing={0.5} sx={{ m: 0, pl: 2.5 }}>
          <Row
            name="Shadow record switch"
            state={shadowState(shadow.data, shadow.isError, evidenceSet.data, evidenceSet.isError)}
            to="/paper?tab=shadow"
          />
          {evidence.map(({ id, name }) => (
            <Row key={id} name={name} state={stateOf(find(id))} to="/services" />
          ))}
        </Stack>
      )}
      {services.data && (
        <>
          <Typography variant="h3" sx={{ fontSize: '1rem', mt: 2, mb: 0.5 }}>
            Resumed by Auto
          </Typography>
          <Stack component="ul" aria-label="Resumed by Auto" sx={{ m: 0, pl: 2.5 }}>
            <Row
              name="Cells refresh"
              state={stateOf(find('cells-refresh'))}
              to="/services"
              note="Auto resumes this one (with the owner's password); each refresh is noted in the paper epoch."
            />
          </Stack>
        </>
      )}
    </Box>
  );
}
