import DragIndicatorIcon from '@mui/icons-material/DragIndicator';
import { Box, ButtonBase, Chip, IconButton, Stack, Typography } from '@mui/material';
import { useSortable } from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';
import type { Basket } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';
import { useBasketDrag } from '../hooks/basketDragContext';
import type { DndData } from '../lib/dropRules';
import { purposeLabel } from '../lib/labels';
import { basketCardSx } from './basketCardStyle';
import { BasketDot } from './BasketDot';

interface BasketCardProps {
  basket: Basket;
  selected: boolean;
  onSelect: (id: string) => void;
}

/**
 * One basket in the left column: colour, name, purpose and member count. Click selects it; the grip reorders the cards
 * (mouse, touch or keyboard). While members are dragged, every other card is a drop target: it lights up under the
 * pointer and says whether the drop copies or, with Alt / Option held, moves.
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
      style={{ transform: CSS.Translate.toString(transform), transition, borderLeftColor: basket.colour }}
    >
      <IconButton
        ref={setActivatorNodeRef}
        size="small"
        aria-label={`Reorder ${basket.name}`}
        {...attributes}
        {...listeners}
        sx={{ alignSelf: 'center', cursor: 'grab', touchAction: 'none' }}
      >
        <DragIndicatorIcon fontSize="small" />
      </IconButton>
      <ButtonBase
        onClick={() => {
          onSelect(basket.id);
        }}
        aria-current={selected ? 'true' : undefined}
        sx={{ flex: 1, minWidth: 0, display: 'block', textAlign: 'left', borderRadius: 1, p: 0.5 }}
      >
        <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
          <BasketDot colour={basket.colour} />
          <Typography variant="h4" component="span" noWrap>
            {basket.name}
          </Typography>
        </Stack>
        <Stack direction="row" sx={{ mt: 0.5, gap: 0.75, alignItems: 'center', flexWrap: 'wrap' }}>
          <Chip size="small" variant="outlined" label={purposeLabel(basket.purpose)} />
          <Typography variant="body2" color="text.secondary">
            {`${formatNumber(basket.memberCount)} ${basket.memberCount === 1 ? 'instrument' : 'instruments'}`}
          </Typography>
        </Stack>
        {hot && (
          <Typography variant="body2" sx={{ mt: 0.5, fontWeight: 600 }}>
            {drag.move ? 'Drop to move here' : 'Drop to copy here · hold Alt to move'}
          </Typography>
        )}
      </ButtonBase>
    </Box>
  );
}
