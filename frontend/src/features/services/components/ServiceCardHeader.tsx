import { Box, Typography } from '@mui/material';
import { alpha } from '@mui/material/styles';
import type { ServiceView } from '@/shared/api';
import { IconChip } from '@/shared/ui';
import { lookOf } from '../lib/serviceLook';
import { ServiceStatusChip } from './ServiceStatusChip';

/** A failed service has a red-tinted header, a running one an info-tinted one, a paused one a neutral one. */
const tints = { failed: 'error', running: 'info' } as const;

/** The top row of a card: the service's icon chip, its name and its state as an icon and a word. */
export function ServiceCardHeader({ service }: { service: ServiceView }) {
  const { icon, hue } = lookOf(service.id);
  const tint = service.status === 'failed' || service.status === 'running' ? tints[service.status] : null;
  return (
    <Box
      sx={(theme) => ({
        display: 'flex',
        alignItems: 'center',
        gap: 1.25,
        py: 1.25,
        pr: 2,
        pl: 2.5,
        bgcolor: tint
          ? alpha(theme.palette[tint].main, 0.08)
          : service.status === 'paused'
            ? 'action.hover'
            : 'transparent',
      })}
    >
      <IconChip icon={icon} hue={hue} size="md" />
      <Typography component="h3" variant="h3" sx={{ flex: 1, minWidth: 0, overflowWrap: 'anywhere' }}>
        {service.name}
      </Typography>
      <Box sx={{ flexShrink: 0 }}>
        <ServiceStatusChip status={service.status} />
      </Box>
    </Box>
  );
}
