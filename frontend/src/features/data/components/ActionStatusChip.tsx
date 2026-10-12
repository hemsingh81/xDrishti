import BlockIcon from '@mui/icons-material/BlockOutlined';
import CheckCircleIcon from '@mui/icons-material/CheckCircleOutlined';
import PendingIcon from '@mui/icons-material/PendingOutlined';
import { Chip } from '@mui/material';
import type { ReactElement } from 'react';
import { actionsViewLabels, isActionsView } from '../lib/actions';

const looks: Readonly<Record<string, { color: 'warning' | 'success' | 'default'; icon: ReactElement }>> = {
  suggested: { color: 'warning', icon: <PendingIcon /> },
  applied: { color: 'success', icon: <CheckCircleIcon /> },
  ignored: { color: 'default', icon: <BlockIcon /> },
};

/** An action's state as a chip with an icon and a word: Suggested (waits for you), Applied or Ignored. */
export function ActionStatusChip({ status }: { status: string }) {
  const look = looks[status];
  return (
    <Chip
      size="small"
      variant="outlined"
      color={look?.color ?? 'default'}
      icon={look?.icon}
      label={isActionsView(status) ? actionsViewLabels[status] : status}
    />
  );
}
