import DnsIcon from '@mui/icons-material/DnsOutlined';
import { Box, Typography } from '@mui/material';
import { IconChip } from '@/shared/ui';

/** The header row: the icon chip, the title and a one-line subtitle (it wraps under the title on a narrow screen). */
export function ServicesHeader() {
  return (
    <Box
      sx={{
        display: 'flex',
        alignItems: 'center',
        flexWrap: 'wrap',
        columnGap: 1.25,
        rowGap: 0.5,
        mb: 1.5,
        flexShrink: 0,
      }}
    >
      <IconChip icon={DnsIcon} hue="services" size="sm" />
      <Typography variant="h1">Services</Typography>
      <Typography color="text.secondary" sx={{ flex: '1 1 280px', minWidth: 0 }}>
        What runs by itself, when, and how the last run went. Change a schedule, run a job now, pause it or read its
        history.
      </Typography>
    </Box>
  );
}
