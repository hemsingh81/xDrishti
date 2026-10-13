import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import { Box, Button } from '@mui/material';
import { useId, useState, type ReactNode } from 'react';

interface DisclosureProps {
  /** The button's text, e.g. "Show 3 rows that could not be read". */
  label: string;
  /** The button's text while the content is shown. */
  openLabel?: string;
  children: ReactNode;
}

/** A button that shows and hides the content below it (`aria-expanded`); the content is not in the page while hidden. */
export function Disclosure({ label, openLabel = label, children }: DisclosureProps) {
  const [open, setOpen] = useState(false);
  const regionId = useId();
  return (
    <Box>
      <Button
        size="small"
        aria-expanded={open}
        aria-controls={open ? regionId : undefined}
        endIcon={<ExpandMoreIcon sx={{ transform: open ? 'rotate(180deg)' : 'none' }} />}
        onClick={() => {
          setOpen((previous) => !previous);
        }}
      >
        {open ? openLabel : label}
      </Button>
      {open && (
        <Box id={regionId} sx={{ mt: 0.5 }}>
          {children}
        </Box>
      )}
    </Box>
  );
}
