import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import { Button, Menu, MenuItem, Stack } from '@mui/material';
import { useId, useState } from 'react';
import { rowActionLabels, type RowActionKey } from '../../lib/actions';

interface ActionRowActionsProps {
  actions: readonly RowActionKey[];
  /** What the item is about, said after the label for assistive technology ("Done RELIANCE"); the visible word stays the first part of the name. */
  subject: string;
  onAction: (action: RowActionKey) => void;
  /** A phone: fold the buttons into a menu. */
  phone: boolean;
}

/**
 * The buttons of one item. From `sm` up they sit in a row; on a phone they fold into one "Actions" menu (a menu button
 * that opens a list of the same actions: Escape closes it and focus returns to the button).
 */
export function ActionRowActions({ actions, subject, onAction, phone }: ActionRowActionsProps) {
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const menuId = useId();
  if (actions.length === 0) return null;

  if (phone) {
    return (
      <>
        <Button
          size="small"
          variant="outlined"
          endIcon={<ExpandMoreIcon />}
          aria-label={`Actions for ${subject}`}
          data-row-action="menu"
          aria-haspopup="menu"
          aria-expanded={anchor !== null}
          aria-controls={anchor ? menuId : undefined}
          onClick={(event) => {
            setAnchor(event.currentTarget);
          }}
          sx={{ alignSelf: 'flex-start', flexShrink: 0 }}
        >
          Actions
        </Button>
        <Menu
          id={menuId}
          anchorEl={anchor}
          open={anchor !== null}
          onClose={() => {
            setAnchor(null);
          }}
        >
          {actions.map((action) => (
            <MenuItem
              key={action}
              onClick={() => {
                setAnchor(null);
                onAction(action);
              }}
            >
              {rowActionLabels[action]}
            </MenuItem>
          ))}
        </Menu>
      </>
    );
  }

  return (
    <Stack direction="row" sx={{ flexWrap: 'wrap', gap: 0.75, alignSelf: 'flex-start', flexShrink: 0 }}>
      {actions.map((action) => (
        <Button
          key={action}
          size="small"
          variant="outlined"
          aria-label={`${rowActionLabels[action]} ${subject}`}
          data-row-action={action}
          onClick={() => {
            onAction(action);
          }}
        >
          {rowActionLabels[action]}
        </Button>
      ))}
    </Stack>
  );
}
