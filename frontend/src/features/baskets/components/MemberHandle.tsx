import DragIndicatorIcon from '@mui/icons-material/DragIndicator';
import { IconButton } from '@mui/material';
import { useSortable } from '@dnd-kit/sortable';
import { useCallback } from 'react';
import { useMemberPanel } from '../hooks/memberPanelContext';
import type { DndData } from '../lib/dropRules';

/**
 * The grip of a member row (a `handle` column of the Grid). The whole row is the sortable item: the grip hands its row
 * to dnd-kit as the node to measure and is the activator for mouse, touch and keyboard (space picks up, arrows move,
 * space drops, escape cancels). Which rows move is drawn by the grid from the drag state, not by transforms.
 * In a portfolio basket the grip only starts a copy: the rows have no order of their own to change.
 */
export function MemberHandle({ id, symbol }: { id: string; symbol: string }) {
  const { basket } = useMemberPanel();
  const readOnly = basket.system;
  const data: DndData = { type: 'member', basketId: basket.id };
  const { attributes, listeners, setNodeRef, setActivatorNodeRef } = useSortable({ id, data });
  const attach = useCallback(
    (grip: HTMLButtonElement | null) => {
      setActivatorNodeRef(grip);
      setNodeRef(grip?.closest('tr') ?? null);
    },
    [setActivatorNodeRef, setNodeRef],
  );
  return (
    <IconButton
      ref={attach}
      size="small"
      aria-label={readOnly ? `Drag ${symbol} to copy` : `Drag ${symbol}`}
      {...attributes}
      {...listeners}
      sx={{ cursor: 'grab', touchAction: 'none' }}
    >
      <DragIndicatorIcon fontSize="small" />
    </IconButton>
  );
}
