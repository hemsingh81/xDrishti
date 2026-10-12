import { SortableContext, verticalListSortingStrategy } from '@dnd-kit/sortable';
import { Box, Typography } from '@mui/material';
import type { BasketGroups } from '../lib/groups';
import { BasketCard } from './BasketCard';
import { SystemBasketCard } from './SystemBasketCard';

interface BasketCardListProps {
  groups: BasketGroups;
  selectedId: string;
  onSelect: (id: string) => void;
}

const heading = { display: 'block', mb: 0.75, px: 0.5 } as const;

const list = { m: 0, p: 0, display: 'flex', flexDirection: 'column', gap: 1.5 } as const;

/**
 * The left column. The Portfolio group (one protected basket per account bucket) comes first and is plain: no grip, no
 * drop target. Below it the owner's baskets in their order; those can be reordered and are drop targets for members.
 */
export function BasketCardList({ groups, selectedId, onSelect }: BasketCardListProps) {
  const { portfolio, manual } = groups;
  const grouped = portfolio.length > 0;
  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2.5, minHeight: 0, overflowY: 'auto', p: '3px' }}>
      {grouped && (
        <Box component="section" aria-labelledby="baskets-portfolio-group">
          <Typography
            id="baskets-portfolio-group"
            variant="overline"
            component="h2"
            color="text.secondary"
            sx={heading}
          >
            Portfolio
          </Typography>
          <Box component="ul" aria-label="Portfolio baskets" sx={list}>
            {portfolio.map((basket) => (
              <SystemBasketCard
                key={basket.id}
                basket={basket}
                selected={basket.id === selectedId}
                onSelect={onSelect}
              />
            ))}
          </Box>
        </Box>
      )}
      <Box component="section" aria-labelledby={grouped ? 'baskets-manual-group' : undefined}>
        {grouped && (
          <Typography id="baskets-manual-group" variant="overline" component="h2" color="text.secondary" sx={heading}>
            Your baskets
          </Typography>
        )}
        {manual.length === 0 ? (
          <Typography color="text.secondary" sx={{ py: 1 }}>
            No baskets of your own yet — create one with New basket.
          </Typography>
        ) : (
          <SortableContext items={manual.map((b) => b.id)} strategy={verticalListSortingStrategy}>
            <Box component="ul" aria-label="Baskets" sx={list}>
              {manual.map((basket) => (
                <BasketCard key={basket.id} basket={basket} selected={basket.id === selectedId} onSelect={onSelect} />
              ))}
            </Box>
          </SortableContext>
        )}
      </Box>
    </Box>
  );
}
