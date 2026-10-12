import DragIndicatorIcon from '@mui/icons-material/DragIndicator';
import { Box, Chip, IconButton } from '@mui/material';
import { useSortable } from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';
import type { Basket } from '@/shared/api';
import { useBasketDrag } from '../hooks/basketDragContext';
import type { DndData } from '../lib/dropRules';
import { basketCardSx } from './basketCardStyle';
import { BasketCardBody } from './BasketCardBody';

interface BasketCardProps {
  basket: Basket;
  selected: boolean;
  onSelect: (id: string) => void;
}

/**
 * One of the owner's baskets in the row of cards. Click selects it; the grip in the corner reorders the cards (mouse,
 * touch or keyboard). While members are dragged, every other card is a drop target: it lights up under the pointer and
 * says whether the drop copies or, with Alt / Option held, moves.
 */
export function BasketCard({ basket, selected, onSelect }: BasketCardProps) {
  const drag = useBasketDrag();
  const data: DndData = { type: 'basket', basketId: basket.id };
  const { attributes, listeners, setNodeRef, setActivatorNodeRef, transform, transition, isDragging, isOver } =
    useSortable({ id: basket.id, data });
  const target = drag.session?.kind === 'member' && basket.id !== drag.session.sourceId;
  const hot = target && isOver;

  return (
    <Box
      component="li"
      ref={setNodeRef}
      data-drop-target={hot ? (drag.move ? 'move' : 'copy') : undefined}
      sx={[basketCardSx({ selected, hot }), { opacity: isDragging ? 0.6 : 1, zIndex: isDragging ? 2 : 'auto' }]}
      style={{ transform: CSS.Translate.toString(transform), transition, borderTopColor: basket.colour }}
    >
      <BasketCardBody
        basket={basket}
        selected={selected}
        onSelect={onSelect}
        kind={<Chip size="small" variant="outlined" label="manual" />}
        hint={hot ? (drag.move ? 'Drop to move here' : 'Drop to copy here · hold Alt to move') : undefined}
        gripSpace
      />
      <IconButton
        ref={setActivatorNodeRef}
        size="small"
        aria-label={`Reorder ${basket.name}`}
        {...attributes}
        {...listeners}
        sx={{ position: 'absolute', top: 4, right: 2, color: 'text.secondary', cursor: 'grab', touchAction: 'none' }}
      >
        <DragIndicatorIcon fontSize="small" />
      </IconButton>
    </Box>
  );
}
