import LockIcon from '@mui/icons-material/LockOutlined';
import { Box, Chip, Stack, Typography } from '@mui/material';
import type { Basket } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';
import { asOfLine } from '../lib/system';
import { basketCardSx } from './basketCardStyle';
import { BasketCardBody } from './BasketCardBody';
import { SourceStatus } from './SourceStatus';

interface SystemCardProps {
  basket: Basket;
  selected: boolean;
  onSelect: (id: string) => void;
}

/**
 * A stored system basket (index, derived, list) in the row of cards: the kind chip with a lock, name, count, then its
 * source status and the two dates. It has no grip and is not a drop target. The derived as-of line needs a version and
 * is shown with the selected basket, not here (the list carries no versions).
 */
export function SystemCard({ basket, selected, onSelect }: SystemCardProps) {
  const line = asOfLine(basket, null);
  const unmatched = basket.source?.unmatched ?? 0;
  return (
    <Box component="li" sx={basketCardSx({ selected })} style={{ borderTopColor: basket.colour }}>
      <Stack sx={{ flex: 1, minWidth: 0 }}>
        <BasketCardBody
          basket={basket}
          selected={selected}
          onSelect={onSelect}
          kind={<Chip size="small" variant="outlined" icon={<LockIcon />} label={basket.kind} />}
        />
        <Stack spacing={0.5} sx={{ px: 1.5, pb: 1.5 }}>
          <SourceStatus basket={basket} />
          {line && (
            <Typography variant="caption" color="text.secondary">
              {line}
            </Typography>
          )}
          {unmatched > 0 && (
            <Typography variant="caption" color="text.secondary">
              {`${formatNumber(unmatched)} members not in your instrument list`}
            </Typography>
          )}
        </Stack>
      </Stack>
    </Box>
  );
}
