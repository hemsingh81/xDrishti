import ArrowDropDownIcon from '@mui/icons-material/ArrowDropDown';
import { Button, ButtonGroup, CircularProgress, ListItemText, Menu, MenuItem } from '@mui/material';
import { useId, useState, type ReactNode, type Ref } from 'react';
import { stateAlpha } from '@/theme';

export interface SplitOption {
  id: string;
  label: string;
  /** A line under the label that says what the option does differently. */
  description?: string;
  onSelect: () => void;
}

interface SplitButtonProps {
  /** The main action. */
  label: string;
  /** Accessible name of the pair of buttons. */
  groupLabel: string;
  /** Accessible name of the arrow that opens the menu. */
  moreLabel: string;
  startIcon: ReactNode;
  onClick: () => void;
  options: readonly SplitOption[];
  /** The action is running: both buttons are marked `aria-disabled` and do nothing, a spinner replaces the icon. */
  busy?: boolean;
  /** Really disabled (the state it needs is not known yet). */
  disabled?: boolean;
  /** The main button, so the page can move focus back to it. */
  buttonRef?: Ref<HTMLButtonElement>;
}

/**
 * A main action with a menu of variants. While busy the buttons are `aria-disabled` (not `disabled`), so a button that
 * has focus keeps it. Import it by path (`@/shared/ui/SplitButton`): it pulls the menu into the screens that use it.
 */
export function SplitButton({
  label,
  groupLabel,
  moreLabel,
  startIcon,
  onClick,
  options,
  busy = false,
  disabled = false,
  buttonRef,
}: SplitButtonProps) {
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const menuId = useId();

  return (
    <>
      <ButtonGroup
        variant="contained"
        disabled={disabled}
        aria-label={groupLabel}
        sx={{ '& .MuiButton-root[aria-disabled="true"]': { opacity: stateAlpha.busy, cursor: 'default' } }}
      >
        <Button
          ref={buttonRef}
          aria-disabled={busy || undefined}
          startIcon={busy ? <CircularProgress size={16} color="inherit" aria-hidden /> : startIcon}
          onClick={() => {
            if (!busy) onClick();
          }}
        >
          {label}
        </Button>
        <Button
          size="small"
          aria-label={moreLabel}
          aria-disabled={busy || undefined}
          aria-haspopup="menu"
          aria-controls={anchor ? menuId : undefined}
          aria-expanded={anchor ? 'true' : undefined}
          onClick={(event) => {
            if (!busy) setAnchor(event.currentTarget);
          }}
        >
          <ArrowDropDownIcon />
        </Button>
      </ButtonGroup>
      <Menu
        id={menuId}
        anchorEl={anchor}
        open={anchor !== null}
        onClose={() => {
          setAnchor(null);
        }}
      >
        {options.map((option) => (
          <MenuItem
            key={option.id}
            onClick={() => {
              setAnchor(null);
              option.onSelect();
            }}
          >
            <ListItemText primary={option.label} secondary={option.description} />
          </MenuItem>
        ))}
      </Menu>
    </>
  );
}
