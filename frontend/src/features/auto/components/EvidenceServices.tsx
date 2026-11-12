import { Box, Link, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { Link as RouterLink } from 'react-router';
import { servicesListQuery } from '@/shared/api';
import { ErrorState, LoadingState } from '@/shared/ui';

/** The forward-evidence services. They are outside the Auto chain (ADR 0252) and stay separate switches (OPS-01, ADR 0278). */
const evidence = [
  { id: 'shadow-write', name: 'Shadow write' },
  { id: 'shadow-grade', name: 'Shadow grade' },
  { id: 'paper-capture', name: 'Paper capture' },
  { id: 'paper-review', name: 'Paper review' },
  { id: 'cells-refresh', name: 'Cells refresh' },
] as const;

/** Read only: no control here turns anything on or off. A service the list does not hold reads missing; a failed read reads unavailable, never running. */
export function EvidenceServices() {
  const services = useQuery(servicesListQuery);
  return (
    <Box component="section" aria-labelledby="evidence-services-heading">
      <Typography variant="h2" id="evidence-services-heading" sx={{ fontSize: '1.1rem', mb: 0.5 }}>
        Evidence services
      </Typography>
      <Typography color="text.secondary" sx={{ mb: 1 }}>
        Auto does not resume these. Each is its own switch, set where the link goes. The shadow record has its own owner
        switch on the{' '}
        <Link component={RouterLink} to="/paper?tab=shadow">
          Paper Shadow tab
        </Link>
        .
      </Typography>
      {services.isPending && <LoadingState label="Reading the evidence services…" />}
      {services.isError && <ErrorState error={services.error} />}
      {services.data && (
        <Stack component="ul" aria-label="Evidence services" spacing={0.5} sx={{ m: 0, pl: 2.5 }}>
          {evidence.map(({ id, name }) => {
            const found = services.data.items.find((item) => item.id === id);
            const state = found ? (found.enabled ? 'running' : 'paused') : 'missing';
            return (
              <li key={id}>
                {name}: {state}.{' '}
                <Link component={RouterLink} to="/services">
                  Open Services
                </Link>
              </li>
            );
          })}
        </Stack>
      )}
    </Box>
  );
}
