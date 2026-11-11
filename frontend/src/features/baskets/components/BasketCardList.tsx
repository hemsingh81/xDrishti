import { rectSortingStrategy, SortableContext } from '@dnd-kit/sortable';
import { Box, Typography } from '@mui/material';
import type { BasketGroups } from '../lib/groups';
import type { KindFilter } from '../lib/system';
import { BasketCard } from './BasketCard';
import { SystemBasketCard } from './SystemBasketCard';
import { SystemGroup } from './SystemGroup';

interface BasketCardListProps {
  /** The groups the kind filter keeps. */
  groups: BasketGroups;
  kind: KindFilter;
  /** How many stored system baskets there are before the filter: none means the group's empty note. */
  systemTotal: number;
  selectedId: string;
  onSelect: (id: string) => void;
}

/**
 * The row of cards across the top (prototype `.basket-cards`): a grid that fills the width and wraps. The portfolio
 * baskets come first and are plain (no grip, no drop target); the owner's baskets follow in their order and can be
 * reordered (the card grid is a sorting context of its own, with the strategy for a wrapping grid).
 */
export function BasketCardList({ groups, kind, systemTotal, selectedId, onSelect }: BasketCardListProps) {
  const { portfolio, system, manual } = groups;
  return (
    <Box>
      {(portfolio.length > 0 || system.length > 0 || manual.length > 0) && (
        <Box
          component="ul"
          aria-label="Baskets"
          sx={{
            m: 0,
            p: 0,
            display: 'grid',
            gap: 1.75,
            gridTemplateColumns: 'repeat(auto-fill, minmax(240px, 1fr))',
          }}
        >
          {portfolio.map((basket) => (
            <SystemBasketCard key={basket.id} basket={basket} selected={basket.id === selectedId} onSelect={onSelect} />
          ))}
          {system.length > 0 && <SystemGroup baskets={system} selectedId={selectedId} onSelect={onSelect} />}
          <SortableContext items={manual.map((b) => b.id)} strategy={rectSortingStrategy}>
            {manual.map((basket) => (
              <BasketCard key={basket.id} basket={basket} selected={basket.id === selectedId} onSelect={onSelect} />
            ))}
          </SortableContext>
        </Box>
      )}
      {systemTotal === 0 && kind !== 'yours' && (
        <Typography color="text.secondary" sx={{ py: 1 }}>
          No system baskets yet: turn on fetching in Settings (off by default)
        </Typography>
      )}
      {systemTotal > 0 && system.length === 0 && kind !== 'all' && kind !== 'yours' && (
        <Typography color="text.secondary" sx={{ py: 1 }}>{`No ${kind} baskets.`}</Typography>
      )}
      {manual.length === 0 && (kind === 'all' || kind === 'yours') && (
        <Typography color="text.secondary" sx={{ py: 1 }}>
          No baskets of your own yet — create one with New basket.
        </Typography>
      )}
    </Box>
  );
}
