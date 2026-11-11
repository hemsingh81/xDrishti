import { Box, Stack, Typography } from '@mui/material';
import type { Basket } from '@/shared/api';
import { SystemCard } from './SystemCard';
import { SystemTools } from './SystemTools';

interface SystemGroupProps {
  baskets: readonly Basket[];
  selectedId: string;
  onSelect: (id: string) => void;
}

/**
 * The "System" group between Portfolio and Your baskets: its heading and owner-act tools, then the read-only cards
 * (kind chip, lock, status, dates). It spans the whole row of the card grid.
 */
export function SystemGroup({ baskets, selectedId, onSelect }: SystemGroupProps) {
  return (
    <Box component="li" sx={{ listStyle: 'none', gridColumn: '1 / -1', minWidth: 0 }}>
      <Stack
        direction="row"
        sx={{ gap: 1.5, rowGap: 1, flexWrap: 'wrap', alignItems: 'flex-start', justifyContent: 'space-between', mb: 1 }}
      >
        <Box>
          <Typography variant="h3" component="h2">
            System
          </Typography>
          <Typography variant="body2" color="text.secondary">
            Reference lists kept by the system: read only, not recommendations.
          </Typography>
        </Box>
        <SystemTools />
      </Stack>
      <Box
        component="ul"
        aria-label="System baskets"
        sx={{
          m: 0,
          p: 0,
          display: 'grid',
          gap: 1.75,
          gridTemplateColumns: 'repeat(auto-fill, minmax(240px, 1fr))',
        }}
      >
        {baskets.map((basket) => (
          <SystemCard key={basket.id} basket={basket} selected={basket.id === selectedId} onSelect={onSelect} />
        ))}
      </Box>
    </Box>
  );
}
