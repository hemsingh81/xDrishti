import { TextField } from '@mui/material';
import type { Basket } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';
import type { BasketGroups } from '../lib/groups';

interface BasketSelectProps {
  groups: BasketGroups;
  selectedId: string;
  onSelect: (id: string) => void;
}

const optionOf = (basket: Basket) => (
  <option key={basket.id} value={basket.id}>
    {`${basket.name} (${formatNumber(basket.memberCount)})`}
  </option>
);

/**
 * The phone version of the basket cards: one select, in two groups when there are portfolio baskets. (Reordering
 * baskets and dropping on them need the wider layout.)
 */
export function BasketSelect({ groups, selectedId, onSelect }: BasketSelectProps) {
  const { portfolio, manual } = groups;
  return (
    <TextField
      select
      size="small"
      label="Basket"
      value={selectedId}
      onChange={(event) => {
        onSelect(event.target.value);
      }}
      slotProps={{ select: { native: true } }}
    >
      {portfolio.length > 0
        ? [
            <optgroup key="portfolio" label="Portfolio">
              {portfolio.map(optionOf)}
            </optgroup>,
            <optgroup key="manual" label="Your baskets">
              {manual.map(optionOf)}
            </optgroup>,
          ]
        : manual.map(optionOf)}
    </TextField>
  );
}
