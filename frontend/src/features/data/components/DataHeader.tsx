import StorageIcon from '@mui/icons-material/StorageOutlined';
import { Box, Typography } from '@mui/material';
import type { ReactNode } from 'react';
import { IconChip } from '@/shared/ui';

const subtitle = 'Load the CSV folder, fetch what is missing from Dhan, build every timeframe.';

/** The slim header row: the title, a one-line subtitle when it fits, and the page's action (Update data) on the right. */
export function DataHeader({ actions }: { actions: ReactNode }) {
  return (
    <Box
      sx={{
        display: 'flex',
        flexWrap: 'wrap',
        alignItems: 'center',
        justifyContent: 'space-between',
        gap: 1.5,
        mb: 1.5,
        flexShrink: 0,
      }}
    >
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.25, minWidth: 0, flex: '1 1 auto' }}>
        <IconChip icon={StorageIcon} hue="data" size="sm" />
        <Typography variant="h1" sx={{ flexShrink: 0 }}>
          Data &amp; import
        </Typography>
        <Typography
          color="text.secondary"
          noWrap
          title={subtitle}
          sx={{ display: { xs: 'none', lg: 'block' }, minWidth: 0 }}
        >
          {subtitle}
        </Typography>
      </Box>
      <Box sx={{ flexShrink: 0 }}>{actions}</Box>
    </Box>
  );
}
