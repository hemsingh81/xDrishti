import { Chip } from '@mui/material';
import { statusColor, statusLabel } from '../lib/labels';
import { StatusIcon } from './StatusIcon';

/** A status as a chip with an icon and a word: Pass, Warning, Fail, Not checked (never colour alone). */
export function QualityStatusChip({ status }: { status: string }) {
  return (
    <Chip
      size="small"
      variant="outlined"
      color={statusColor(status)}
      icon={<StatusIcon status={status} fontSize="inherit" />}
      label={statusLabel(status)}
    />
  );
}
