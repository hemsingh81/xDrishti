import ExpandLessIcon from '@mui/icons-material/ExpandLess';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import { Box, Button, Collapse } from '@mui/material';
import { useId, useState } from 'react';
import { monoFontFamily, rem } from '@/theme';

interface CollapsibleProps {
  /** The button's text, e.g. "3 unknown symbols". */
  label: string;
  items: readonly string[];
}

/** A short list kept out of the way until asked for. Renders nothing for an empty list. */
export function Collapsible({ label, items }: CollapsibleProps) {
  const [open, setOpen] = useState(false);
  const listId = useId();
  if (items.length === 0) return null;
  return (
    <Box>
      <Button
        size="small"
        color="inherit"
        aria-expanded={open}
        aria-controls={listId}
        endIcon={open ? <ExpandLessIcon /> : <ExpandMoreIcon />}
        onClick={() => {
          setOpen((value) => !value);
        }}
        sx={{ px: 0.5 }}
      >
        {label}
      </Button>
      <Collapse in={open} unmountOnExit>
        <Box
          component="ul"
          id={listId}
          sx={{
            m: 0,
            pl: 2.5,
            maxHeight: 160,
            overflowY: 'auto',
            overflowWrap: 'anywhere',
            fontFamily: monoFontFamily,
            fontSize: rem(13),
          }}
        >
          {items.map((item) => (
            <li key={item}>{item}</li>
          ))}
        </Box>
      </Collapse>
    </Box>
  );
}
