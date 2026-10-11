import RefreshIcon from '@mui/icons-material/Refresh';
import ArrowDropDownIcon from '@mui/icons-material/ArrowDropDown';
import { Button, ButtonGroup, CircularProgress, ListItemText, Menu, MenuItem } from '@mui/material';
import { useId, useState, type Ref } from 'react';
import { stateAlpha } from '@/theme';

interface RefreshActionsProps {
  /** A refresh is running or was just started. */
  busy: boolean;
  /** The status is not known yet, so starting would be a guess. */
  disabled?: boolean;
  onStart: (force: boolean) => void;
  /** The main button, so the page can move focus back to it. */
  buttonRef?: Ref<HTMLButtonElement>;
}

/**
 * "Refresh master list" plus a menu with "Force full refresh". While a refresh runs both are marked `aria-disabled`
 * (and do nothing) instead of `disabled`, so a button that has focus keeps it. They are really disabled only while the
 * status is unknown.
 */
export function RefreshActions({ busy, disabled = false, onStart, buttonRef }: RefreshActionsProps) {
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const menuId = useId();

  return (
    <>
      <ButtonGroup
        variant="contained"
        disabled={disabled}
        aria-label="Refresh the master list"
        sx={{ '& .MuiButton-root[aria-disabled="true"]': { opacity: stateAlpha.busy, cursor: 'default' } }}
      >
        <Button
          ref={buttonRef}
          aria-disabled={busy || undefined}
          startIcon={busy ? <CircularProgress size={16} color="inherit" aria-hidden /> : <RefreshIcon />}
          onClick={() => {
            if (!busy) onStart(false);
          }}
        >
          Refresh master list
        </Button>
        <Button
          size="small"
          aria-label="More refresh options"
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
        <MenuItem
          onClick={() => {
            setAnchor(null);
            onStart(true);
          }}
        >
          <ListItemText
            primary="Force full refresh"
            secondary="Re-import everything, even if Dhan's file has not changed"
          />
        </MenuItem>
      </Menu>
    </>
  );
}
