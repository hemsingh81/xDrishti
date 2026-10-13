import { Box, Typography } from '@mui/material';
import type { ServiceView } from '@/shared/api';

/** The service's name and what it does (wraps, so a long description stays in its column). */
export function ServiceName({ service }: { service: ServiceView }) {
  return (
    <Box sx={{ whiteSpace: 'normal', minWidth: 0, maxWidth: 420 }}>
      <Typography component="div" sx={{ fontWeight: 650 }}>
        {service.name}
      </Typography>
      <Typography variant="caption" color="text.secondary" component="div">
        {service.description}
      </Typography>
    </Box>
  );
}
