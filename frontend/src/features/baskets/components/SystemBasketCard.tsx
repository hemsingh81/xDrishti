import LockIcon from '@mui/icons-material/LockOutlined';
import { Box, ButtonBase, Chip, Stack, Typography } from '@mui/material';
import type { Basket } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';
import { basketCardSx } from './basketCardStyle';
import { BasketDot } from './BasketDot';

interface SystemBasketCardProps {
  basket: Basket;
  selected: boolean;
  onSelect: (id: string) => void;
}

/**
 * A portfolio (system) basket in the left column: the lock and the Portfolio badge say it is protected, the account
 * name says whose it is. Click selects it. It has no grip and is not a drop target: it follows the account's holdings.
 */
export function SystemBasketCard({ basket, selected, onSelect }: SystemBasketCardProps) {
  return (
    <Box component="li" sx={[basketCardSx({ selected }), { pl: 1 }]} style={{ borderLeftColor: basket.colour }}>
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
          <Chip size="small" variant="outlined" icon={<LockIcon />} label="Portfolio" />
          <Typography variant="body2" color="text.secondary">
            {`${formatNumber(basket.memberCount)} ${basket.memberCount === 1 ? 'instrument' : 'instruments'}`}
          </Typography>
        </Stack>
        {basket.accountName && (
          <Typography variant="caption" color="text.secondary" noWrap sx={{ display: 'block', mt: 0.5 }}>
            {basket.accountName}
          </Typography>
        )}
      </ButtonBase>
    </Box>
  );
}
