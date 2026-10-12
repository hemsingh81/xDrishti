import StorageIcon from '@mui/icons-material/StorageOutlined';
import { Card, Stack, Typography } from '@mui/material';
import { IconChip } from '@/shared/ui';

/** Nothing stored and nothing running: say where the files go and what to press. */
export function EmptyData() {
  return (
    <Card sx={{ p: 2 }}>
      <Stack direction="row" spacing={1.5} sx={{ alignItems: 'center' }}>
        <IconChip icon={StorageIcon} hue="data" size="lg" />
        <Typography sx={{ fontWeight: 600 }}>
          No data yet — put your CSV files in data/Stocks_CSV and press Update data
        </Typography>
      </Stack>
    </Card>
  );
}
