import BlockIcon from '@mui/icons-material/BlockOutlined';
import CheckIcon from '@mui/icons-material/CheckCircleOutlined';
import ErrorIcon from '@mui/icons-material/ErrorOutlineOutlined';
import HelpIcon from '@mui/icons-material/HelpOutlineOutlined';
import RemoveIcon from '@mui/icons-material/RemoveCircleOutlineOutlined';
import { Chip } from '@mui/material';
import type { ReactElement } from 'react';
import type { TradeRebuild } from '@/shared/api';
import { rebuildStatusLabels, statusOf, type RebuildStatus } from '../../lib/tradeText';

const looks: Record<RebuildStatus, { icon: ReactElement; color: 'success' | 'error' | 'warning' | 'default' }> = {
  ok: { icon: <CheckIcon />, color: 'success' },
  unchanged: { icon: <RemoveIcon />, color: 'default' },
  quantity_mismatch: { icon: <ErrorIcon />, color: 'error' },
  no_holding: { icon: <BlockIcon />, color: 'default' },
  ambiguous: { icon: <HelpIcon />, color: 'warning' },
};

/** The result of a rebuild as an icon and a word (the colour only helps): OK, Unchanged, Quantity mismatch, Not held, Ambiguous. */
export function RebuildStatusChip({ rebuild }: { rebuild: TradeRebuild }) {
  const status = statusOf(rebuild);
  const { icon, color } = looks[status];
  return <Chip size="small" variant="outlined" icon={icon} color={color} label={rebuildStatusLabels[status]} />;
}
