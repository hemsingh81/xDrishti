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
 * The phone version of the basket cards: one select, in groups (Portfolio, System, Your baskets) when there is more than one. (Reordering
 * baskets and dropping on them need the wider layout.)
 */
export function BasketSelect({ groups, selectedId, onSelect }: BasketSelectProps) {
  const { portfolio, system, manual } = groups;
  const sections = [
    { label: 'Portfolio', baskets: portfolio },
    { label: 'System', baskets: system },
    { label: 'Your baskets', baskets: manual },
  ].filter((section) => section.baskets.length > 0);
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
      {sections.length > 1
        ? sections.map((section) => (
            <optgroup key={section.label} label={section.label}>
              {section.baskets.map(optionOf)}
            </optgroup>
          ))
        : sections.flatMap((section) => section.baskets.map(optionOf))}
    </TextField>
  );
}
