import { Paper, Typography } from '@mui/material';
import { formatNumber } from '@/shared/lib/format';
import type { DragSession, DropTarget } from '../lib/dropRules';

interface DragPreviewProps {
  session: DragSession;
  over: DropTarget | null;
  move: boolean;
  labelOf: (kind: 'member' | 'basket', id: string) => string;
}

/** What follows the pointer (or the keyboard drag): what is carried and, over another basket, what dropping does. */
export function DragPreview({ session, over, move, labelOf }: DragPreviewProps) {
  const title =
    session.kind === 'basket'
      ? labelOf('basket', session.activeId)
      : session.ids.length === 1
        ? labelOf('member', session.activeId)
        : `${formatNumber(session.ids.length)} instruments`;
  const toBasket = session.kind === 'member' && over?.data.type === 'basket' && over.id !== session.sourceId;
  return (
    <Paper
      elevation={4}
      aria-hidden
      sx={{ px: 1.5, py: 0.75, border: 1, borderColor: 'primary.main', cursor: 'grabbing', whiteSpace: 'nowrap' }}
    >
      <Typography sx={{ fontWeight: 600 }}>{title}</Typography>
      {toBasket && (
        <Typography variant="body2" color="text.secondary">
          {`${move ? 'Move' : 'Copy'} to ${labelOf('basket', over.id)}`}
        </Typography>
      )}
    </Paper>
  );
}
