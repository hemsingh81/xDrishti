import { ListItemText, ListSubheader, Menu, MenuItem } from '@mui/material';
import type { Basket } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';
import { BasketDot } from './BasketDot';

type Verb = 'copy' | 'move';

interface TransferMenuProps {
  /** The element that opened it; null = closed. */
  anchor: HTMLElement | null;
  /** One verb, or both (the row menu). */
  modes: readonly Verb[];
  baskets: readonly Basket[];
  /** Accessible name of the menu. */
  label: string;
  onPick: (basketId: string, mode: Verb) => void;
  onClose: () => void;
}

const heading: Readonly<Record<Verb, string>> = { copy: 'Copy to', move: 'Move to' };

/**
 * The keyboard-and-screen-reader way to copy or move members: a menu of the other baskets ("Copy to Best stocks"),
 * the same thing as dropping on a card.
 */
export function TransferMenu({ anchor, modes, baskets, label, onPick, onClose }: TransferMenuProps) {
  const items = modes.flatMap((mode) => [
    modes.length > 1 ? (
      <ListSubheader key={`${mode}-heading`} role="presentation" sx={{ lineHeight: '32px' }}>
        {heading[mode]}
      </ListSubheader>
    ) : null,
    ...baskets.map((basket) => (
      <MenuItem
        key={`${mode}-${basket.id}`}
        aria-label={`${heading[mode]} ${basket.name}`}
        onClick={() => {
          onPick(basket.id, mode);
        }}
      >
        <BasketDot colour={basket.colour} />
        <ListItemText
          sx={{ ml: 1.25 }}
          primary={basket.name}
          secondary={`${formatNumber(basket.memberCount)} ${basket.memberCount === 1 ? 'instrument' : 'instruments'}`}
        />
      </MenuItem>
    )),
  ]);
  return (
    <Menu anchorEl={anchor} open={anchor !== null} onClose={onClose} slotProps={{ list: { 'aria-label': label } }}>
      {baskets.length === 0 ? <MenuItem disabled>No other basket yet</MenuItem> : items}
    </Menu>
  );
}
