import { Box, Card, Typography } from '@mui/material';
import { useMemo } from 'react';
import type { ServiceView } from '@/shared/api';
import type { ServiceActionHandlers } from './ServiceActions';
import { ServiceCard } from './ServiceCard';

interface ServiceGridProps {
  services: readonly ServiceView[];
  busyId: string | null;
  handlers: ServiceActionHandlers;
}

function NoServices() {
  return (
    <Card component="section" aria-label="Services" sx={{ p: 4, textAlign: 'center' }}>
      <Typography sx={{ fontWeight: 600 }}>No services</Typography>
      <Typography variant="body2" color="text.secondary">
        No automatic job is registered yet.
      </Typography>
    </Card>
  );
}

/**
 * The services as cards in a grid that fills the width: as many columns as fit (each at least 340 px, so one on a
 * phone). From `md` it takes the rest of the height under the summary and the timeline and scrolls inside when the
 * cards need more; below `md` the page scrolls.
 */
export function ServiceGrid({ services, busyId, handlers }: ServiceGridProps) {
  const names = useMemo(() => new Map(services.map((s) => [s.id, s.name])), [services]);
  if (services.length === 0) return <NoServices />;
  return (
    // The padding leaves room for the focus rings and the card edges, which a scroll container would clip.
    <Box sx={{ flex: '1 1 auto', minHeight: { md: 400 }, overflowY: { md: 'auto' }, p: 0.5, m: -0.5 }}>
      <Box
        component="ul"
        aria-label="Services"
        sx={{
          m: 0,
          p: 0,
          display: 'grid',
          gap: 2,
          gridTemplateColumns: 'repeat(auto-fill, minmax(min(100%, 340px), 1fr))',
        }}
      >
        {services.map((service) => (
          <ServiceCard key={service.id} service={service} names={names} busy={busyId === service.id} {...handlers} />
        ))}
      </Box>
    </Box>
  );
}
