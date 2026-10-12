import FactCheckIcon from '@mui/icons-material/FactCheckOutlined';
import { Stack, Typography } from '@mui/material';
import { IconChip } from '@/shared/ui';

/** No check has ever run: the empty state of the table area says what to press. */
export function NeverRun() {
  return (
    <Stack spacing={1.5} sx={{ flex: '1 1 auto', alignItems: 'center', justifyContent: 'center', p: 4 }}>
      <IconChip icon={FactCheckIcon} hue="quality" size="lg" />
      <Typography sx={{ fontWeight: 600 }}>No checks yet — press Run checks</Typography>
    </Stack>
  );
}
