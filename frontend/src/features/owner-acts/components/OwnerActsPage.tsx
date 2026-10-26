import { Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { ErrorState, LoadingState, PageHeader } from '@/shared/ui';
import { capabilitiesQuery } from '../api/capabilitiesApi';
import { CapabilityCard } from './CapabilityCard';
import { CapabilityHistory } from './CapabilityHistory';

/** The owner-act switches. Switching one on only allows an act; every act still asks for the password. */
export function OwnerActsPage() {
  const query = useQuery(capabilitiesQuery());
  return (
    <>
      <PageHeader
        title="Owner-act switches"
        description="Switching on only allows an act. Every act still asks for your password, and nothing is applied automatically. Switching off needs no password."
      />
      {query.isPending && <LoadingState />}
      {query.isError && <ErrorState error={query.error} />}
      {query.data && (
        <Stack spacing={2}>
          {query.data.items.map((item) => (
            <CapabilityCard key={item.name} item={item} />
          ))}
          <Typography variant="h2" component="h2" sx={{ fontSize: '1.1rem' }}>
            Latest switches
          </Typography>
          <CapabilityHistory history={query.data.history} />
        </Stack>
      )}
    </>
  );
}
