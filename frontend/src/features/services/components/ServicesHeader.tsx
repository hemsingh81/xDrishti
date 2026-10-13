import DnsIcon from '@mui/icons-material/DnsOutlined';
import { Box, Typography } from '@mui/material';
import { IconChip } from '@/shared/ui';

const subtitle =
  'What runs by itself, when, and how the last run went. Change a schedule, run a job now, pause it or read its history.';

/** The slim header row: the title and a one-line subtitle when it fits. */
export function ServicesHeader() {
  return (
    <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.25, mb: 1.5, flexShrink: 0, minWidth: 0 }}>
      <IconChip icon={DnsIcon} hue="services" size="sm" />
      <Typography variant="h1" sx={{ flexShrink: 0 }}>
        Services
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
  );
}
