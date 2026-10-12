import BlockIcon from '@mui/icons-material/BlockOutlined';
import CheckCircleIcon from '@mui/icons-material/CheckCircleOutlined';
import WarningIcon from '@mui/icons-material/WarningAmberOutlined';
import { Chip } from '@mui/material';
import type { ReactElement } from 'react';
import { gapsViewLabels, isGapStatus } from '../lib/gaps';

const looks: Readonly<Record<string, { color: 'warning' | 'success' | 'default'; icon: ReactElement }>> = {
  open: { color: 'warning', icon: <WarningIcon /> },
  unfillable: { color: 'default', icon: <BlockIcon /> },
  filled: { color: 'success', icon: <CheckCircleIcon /> },
};

/** A gap's state as a chip with an icon and a word: Open, Unfillable (a refetch added nothing) or Filled. */
export function GapStatusChip({ status }: { status: string }) {
  const look = looks[status];
  return (
    <Chip
      size="small"
      variant="outlined"
      color={look?.color ?? 'default'}
      icon={look?.icon}
      label={isGapStatus(status) ? gapsViewLabels[status] : status}
    />
  );
}
