import { FormHelperText, MenuItem, TextField } from '@mui/material';
import { useBasketNames } from '../hooks/useBasketNames';
import { MAX_BASKETS } from '../lib/pivotLimits';

interface BasketPickerProps {
  value: readonly string[];
  onChange: (ids: string[]) => void;
}

/** Two to six baskets. A seventh is refused: the options stay disabled at the limit and the limit is stated. */
export function BasketPicker({ value, onChange }: BasketPickerProps) {
  const baskets = useBasketNames();
  const full = value.length >= MAX_BASKETS;
  return (
    <>
      <TextField
        select
        size="small"
        label="Baskets to compare"
        value={[...value]}
        onChange={(event) => {
          const picked = event.target.value as unknown as string[];
          onChange(picked.slice(0, MAX_BASKETS));
        }}
        slotProps={{
          select: {
            multiple: true,
            displayEmpty: true,
            renderValue: (selected) => {
              const ids = selected as string[];
              return ids.length === 0 ? 'None chosen' : ids.map(baskets.name).join(', ');
            },
          },
          inputLabel: { shrink: true },
        }}
        sx={{ minWidth: 260, maxWidth: '100%' }}
      >
        {baskets.options.map((basket) => (
          <MenuItem
            key={basket.id}
            value={basket.id}
            disabled={full && !value.includes(basket.id)}
            sx={{ minHeight: 44 }}
          >
            {basket.name}
          </MenuItem>
        ))}
      </TextField>
      <FormHelperText>{`Choose 2 to ${String(MAX_BASKETS)} baskets (limit ${String(MAX_BASKETS)}: the chart has six line roles).`}</FormHelperText>
    </>
  );
}
