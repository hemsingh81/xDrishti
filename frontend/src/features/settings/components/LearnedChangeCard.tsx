import CheckCircleIcon from '@mui/icons-material/CheckCircleOutlined';
import HourglassIcon from '@mui/icons-material/HourglassEmptyOutlined';
import RemoveCircleIcon from '@mui/icons-material/RemoveCircleOutlineOutlined';
import UndoIcon from '@mui/icons-material/UndoOutlined';
import { Button, Stack, Typography } from '@mui/material';
import type { ElementType } from 'react';
import type { LearnedChangeView } from '@/shared/api/types';
import { formatIstDateTime } from '@/shared/lib/format';
import type { LearnedAct } from '../api/learnedApi';
import { decidedByText, stateLabel } from '../lib/learnedWords';

const stateIcon: Record<string, ElementType> = {
  proposed: HourglassIcon,
  applied: CheckCircleIcon,
  dismissed: RemoveCircleIcon,
  undone: UndoIcon,
};

/** One change: what moved, why it counts, who decided, and the acts the server allows on it. */
export function LearnedChangeCard({ change, onAct }: { change: LearnedChangeView; onAct: (act: LearnedAct) => void }) {
  const Icon = stateIcon[change.state] ?? HourglassIcon;
  const heading = `${change.label}: ${change.oldValue} to ${change.newValue}`;
  return (
    <Stack
      component="article"
      aria-label={heading}
      spacing={0.5}
      sx={{ py: 1.5, borderBottom: 1, borderColor: 'divider' }}
    >
      <Typography component="h3" variant="subtitle2">
        {heading}
      </Typography>
      <Stack direction="row" spacing={0.5} sx={{ alignItems: 'center' }}>
        <Icon fontSize="small" color="action" aria-hidden />
        <Typography variant="body2">
          {stateLabel(change.state)} · {change.direction === 'tighten' ? 'More cautious' : 'Less cautious'} ·{' '}
          {change.source}
        </Typography>
      </Stack>
      {change.decidedBy !== null && change.decidedAt !== null && (
        <Typography variant="body2" color="text.secondary">
          {decidedByText(change.decidedBy)} · {formatIstDateTime(change.decidedAt)}
        </Typography>
      )}
      {change.undoneBy !== null && change.undoneAt !== null && (
        <Typography variant="body2" color="text.secondary">
          Undone by {change.undoneBy.replace(/^owner:/, '')} · {formatIstDateTime(change.undoneAt)}
        </Typography>
      )}
      <Typography variant="body2" color="text.secondary">
        Evidence {change.evidenceRef} · settings snapshot {change.snapshotHash} · proposed{' '}
        {formatIstDateTime(change.createdAt)}
      </Typography>
      <Stack direction="row" spacing={1}>
        {change.state === 'proposed' && (
          <>
            <Button
              size="small"
              variant="contained"
              onClick={() => {
                onAct('approve');
              }}
            >
              Approve
            </Button>
            <Button
              size="small"
              variant="outlined"
              onClick={() => {
                onAct('dismiss');
              }}
            >
              Dismiss
            </Button>
          </>
        )}
        {change.canUndo && (
          <Button
            size="small"
            variant="outlined"
            onClick={() => {
              onAct('undo');
            }}
          >
            Undo
          </Button>
        )}
      </Stack>
    </Stack>
  );
}
