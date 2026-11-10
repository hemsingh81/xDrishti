import CheckIcon from '@mui/icons-material/CheckCircleOutlined';
import ErrorIcon from '@mui/icons-material/ErrorOutlineOutlined';
import InfoIcon from '@mui/icons-material/InfoOutlined';
import WarningIcon from '@mui/icons-material/WarningAmberOutlined';
import { Stack, Typography } from '@mui/material';
import type { Basket } from '@/shared/api';
import { sourceStatus, type SourceTone } from '../lib/system';

const toneColour: Record<SourceTone, string> = {
  ok: 'success.main',
  warning: 'warning.main',
  error: 'error.main',
  neutral: 'text.secondary',
};
const toneIcon: Record<SourceTone, typeof CheckIcon> = {
  ok: CheckIcon,
  warning: WarningIcon,
  error: ErrorIcon,
  neutral: InfoIcon,
};

interface SourceStatusProps {
  basket: Basket;
  /** Why the last fetch was rejected or failed (from the source), when known. */
  reason?: string | null;
}

/** The state of a system basket's source in words and an icon (never colour alone); announced as a status. */
export function SourceStatus({ basket, reason = null }: SourceStatusProps) {
  const status = sourceStatus(basket, reason);
  if (!status) return null;
  const Icon = toneIcon[status.tone];
  return (
    <Stack direction="row" role="status" sx={{ gap: 0.75, alignItems: 'flex-start', color: toneColour[status.tone] }}>
      <Icon fontSize="small" sx={{ flexShrink: 0 }} />
      <Typography variant="body2" sx={{ color: 'inherit', overflowWrap: 'anywhere' }}>
        {status.text}
      </Typography>
    </Stack>
  );
}
