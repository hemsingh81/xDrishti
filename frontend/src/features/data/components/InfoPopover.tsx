import InfoIcon from '@mui/icons-material/InfoOutlined';
import { IconButton, Popover } from '@mui/material';
import { useId, useState, type ReactNode } from 'react';

interface InfoPopoverProps {
  /** Accessible name of the button, e.g. "About gaps". */
  label: string;
  children: ReactNode;
}

/** A small info button that opens the longer explanation, so that the screen itself stays short. */
export function InfoPopover({ label, children }: InfoPopoverProps) {
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const id = useId();
  return (
    <>
      <IconButton
        size="small"
        aria-label={label}
        aria-haspopup="dialog"
        aria-expanded={anchor ? 'true' : undefined}
        aria-controls={anchor ? id : undefined}
        onClick={(event) => {
          setAnchor(event.currentTarget);
        }}
      >
        <InfoIcon fontSize="small" />
      </IconButton>
      <Popover
        open={anchor !== null}
        anchorEl={anchor}
        onClose={() => {
          setAnchor(null);
        }}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'left' }}
        slotProps={{ paper: { id, role: 'dialog', 'aria-label': label, sx: { p: 2, maxWidth: 440 } } }}
      >
        {children}
      </Popover>
    </>
  );
}
