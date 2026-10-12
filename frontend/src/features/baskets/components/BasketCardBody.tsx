import { ButtonBase, Chip, Stack, Typography } from '@mui/material';
import type { ReactNode } from 'react';
import type { Basket } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';
import { purposeTag } from '../lib/labels';

interface BasketCardBodyProps {
  basket: Basket;
  selected: boolean;
  onSelect: (id: string) => void;
  /** The chip that says what kind of basket it is (`manual`, or `portfolio` with the lock). */
  kind: ReactNode;
  /** Replaces the chip row while the card is a drop target under the pointer. */
  hint?: string;
  /** Room on the right for the grip of a sortable card. */
  gripSpace?: boolean;
}

const purposeColour = (purpose: string) =>
  purpose === 'trading' ? 'primary' : purpose === 'long_term' ? 'success' : 'default';

/** What a basket card says: its name, then kind, purpose and member count. The whole body selects the basket. */
export function BasketCardBody({ basket, selected, onSelect, kind, hint, gripSpace = false }: BasketCardBodyProps) {
  const count = `${formatNumber(basket.memberCount)} ${basket.memberCount === 1 ? 'instrument' : 'instruments'}`;
  return (
    <ButtonBase
      onClick={() => {
        onSelect(basket.id);
      }}
      aria-current={selected ? 'true' : undefined}
      sx={{
        flex: 1,
        minWidth: 0,
        display: 'block',
        textAlign: 'left',
        borderRadius: 'inherit',
        p: 1.5,
        pr: gripSpace ? 4.5 : 1.5,
      }}
    >
      <Typography variant="h4" component="span" noWrap sx={{ display: 'block', mb: 0.5 }}>
        {basket.name}
      </Typography>
      {hint ? (
        <Typography variant="body2" sx={{ fontWeight: 600, minHeight: 24, lineHeight: '24px' }}>
          {hint}
        </Typography>
      ) : (
        <Stack direction="row" sx={{ gap: 0.75, alignItems: 'center', flexWrap: 'wrap', minHeight: 24 }}>
          {kind}
          <Chip
            size="small"
            variant="outlined"
            color={purposeColour(basket.purpose)}
            label={purposeTag(basket.purpose)}
          />
          <Typography variant="caption" color="text.secondary">
            {count}
          </Typography>
        </Stack>
      )}
    </ButtonBase>
  );
}
