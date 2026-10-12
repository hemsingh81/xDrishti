import { TextField } from '@mui/material';
import type { Basket } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';

interface BasketSelectProps {
  baskets: readonly Basket[];
  selectedId: string;
  onSelect: (id: string) => void;
}

/** The phone version of the basket cards: one select. (Reordering baskets and dropping on them need the wider layout.) */
export function BasketSelect({ baskets, selectedId, onSelect }: BasketSelectProps) {
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
      {baskets.map((basket) => (
        <option key={basket.id} value={basket.id}>
          {`${basket.name} (${formatNumber(basket.memberCount)})`}
        </option>
      ))}
    </TextField>
  );
}
