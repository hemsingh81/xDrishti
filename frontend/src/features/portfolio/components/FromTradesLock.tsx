import LockIcon from '@mui/icons-material/LockOutlined';
import { IconButton, Tooltip } from '@mui/material';
import { useState } from 'react';

const reason =
  'This lot comes from your trades, so its date is the trade date. Change the trades and rebuild the lots to change it.';

/**
 * Stands where the edit button would be for a lot that was built from trades: it says why the date cannot be edited. The
 * button is focusable (`aria-disabled`, not `disabled`) and the tooltip opens from the keyboard as well as the pointer.
 */
export function FromTradesLock({ lotName }: { lotName: string }) {
  const [open, setOpen] = useState(false);
  return (
    <Tooltip
      title={reason}
      open={open}
      onOpen={() => {
        setOpen(true);
      }}
      onClose={() => {
        setOpen(false);
      }}
    >
      <IconButton
        size="small"
        aria-disabled
        aria-label={`The date of the lot bought on ${lotName} comes from your trades and cannot be edited`}
        onFocus={() => {
          setOpen(true);
        }}
        onBlur={() => {
          setOpen(false);
        }}
      >
        <LockIcon fontSize="small" />
      </IconButton>
    </Tooltip>
  );
}
