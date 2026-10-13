import { Box, Card, Typography, useMediaQuery, useTheme } from '@mui/material';
import { useMemo } from 'react';
import type { ServiceView } from '@/shared/api';
import { Grid } from '@/shared/ui/grid';
import type { ServiceActionHandlers } from './ServiceActions';
import { ServiceCards } from './ServiceCards';
import { serviceColumns } from './serviceColumns';

const getRowId = (service: ServiceView) => service.id;

interface ServicesCardProps {
  services: readonly ServiceView[];
  busyId: string | null;
  handlers: ServiceActionHandlers;
}

function NoServices() {
  return (
    <Box sx={{ p: 4, textAlign: 'center' }}>
      <Typography sx={{ fontWeight: 600 }}>No services</Typography>
      <Typography variant="body2" color="text.secondary">
        No automatic job is registered yet.
      </Typography>
    </Box>
  );
}

/**
 * The services: a table in a card that fills the space under the timeline (from `md`; its rows scroll inside), and a
 * card per service on a phone.
 */
export function ServicesCard({ services, busyId, handlers }: ServicesCardProps) {
  const theme = useTheme();
  const phone = useMediaQuery(theme.breakpoints.down('md'));
  const names = useMemo(() => new Map(services.map((s) => [s.id, s.name])), [services]);
  const columns = useMemo(() => serviceColumns(handlers, names, busyId), [handlers, names, busyId]);

  if (services.length === 0) {
    return (
      <Card component="section" aria-label="Services">
        <NoServices />
      </Card>
    );
  }
  if (phone) return <ServiceCards services={services} names={names} busyId={busyId} handlers={handlers} />;
  return (
    <Card
      component="section"
      aria-label="Services"
      sx={{ display: 'flex', flexDirection: 'column', flex: '1 1 auto', minHeight: 240 }}
    >
      <Grid label="Services" columns={columns} rows={services} getRowId={getRowId} density="comfortable" />
    </Card>
  );
}
