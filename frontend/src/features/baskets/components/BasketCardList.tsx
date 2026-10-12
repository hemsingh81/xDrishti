import { SortableContext, verticalListSortingStrategy } from '@dnd-kit/sortable';
import { Box } from '@mui/material';
import type { Basket } from '@/shared/api';
import { BasketCard } from './BasketCard';

interface BasketCardListProps {
  baskets: readonly Basket[];
  selectedId: string;
  onSelect: (id: string) => void;
}

/** The left column of baskets in the owner's order; the cards can be reordered and are drop targets for members. */
export function BasketCardList({ baskets, selectedId, onSelect }: BasketCardListProps) {
  return (
    <SortableContext items={baskets.map((b) => b.id)} strategy={verticalListSortingStrategy}>
      <Box
        component="ul"
        aria-label="Baskets"
        sx={{ m: 0, p: 0, display: 'flex', flexDirection: 'column', gap: 1.5, minHeight: 0, overflowY: 'auto' }}
      >
        {baskets.map((basket) => (
          <BasketCard key={basket.id} basket={basket} selected={basket.id === selectedId} onSelect={onSelect} />
        ))}
      </Box>
    </SortableContext>
  );
}
