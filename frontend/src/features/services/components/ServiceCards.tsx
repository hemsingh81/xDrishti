import { Box } from '@mui/material';
import type { ServiceView } from '@/shared/api';
import type { ServiceActionHandlers } from './ServiceActions';
import { ServiceCard } from './ServiceCard';

interface ServiceCardsProps {
  services: readonly ServiceView[];
  names: ReadonlyMap<string, string>;
  busyId: string | null;
  handlers: ServiceActionHandlers;
}

/** The services on a phone: one card each, in the order of the table. */
export function ServiceCards({ services, names, busyId, handlers }: ServiceCardsProps) {
  return (
    <Box component="ul" aria-label="Services" sx={{ m: 0, p: 0, display: 'grid', gap: 1.25 }}>
      {services.map((service) => (
        <ServiceCard key={service.id} service={service} names={names} busy={busyId === service.id} {...handlers} />
      ))}
    </Box>
  );
}
