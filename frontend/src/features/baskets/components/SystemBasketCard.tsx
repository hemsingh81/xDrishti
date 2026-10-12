import LockIcon from '@mui/icons-material/LockOutlined';
import { Box, Chip } from '@mui/material';
import type { Basket } from '@/shared/api';
import { basketCardSx } from './basketCardStyle';
import { BasketCardBody } from './BasketCardBody';

interface SystemBasketCardProps {
  basket: Basket;
  selected: boolean;
  onSelect: (id: string) => void;
}

/**
 * A portfolio (system) basket in the row of cards: the lock and the `portfolio` chip say it is protected. Click selects
 * it. It has no grip and is not a drop target: it follows the account's holdings.
 */
export function SystemBasketCard({ basket, selected, onSelect }: SystemBasketCardProps) {
  return (
    <Box component="li" sx={basketCardSx({ selected })} style={{ borderTopColor: basket.colour }}>
      <BasketCardBody
        basket={basket}
        selected={selected}
        onSelect={onSelect}
        kind={<Chip size="small" variant="outlined" icon={<LockIcon />} label="portfolio" />}
      />
    </Box>
  );
}
