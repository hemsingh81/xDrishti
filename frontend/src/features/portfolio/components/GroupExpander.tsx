import ChevronRightIcon from '@mui/icons-material/ChevronRight';
import { Button } from '@mui/material';

interface GroupExpanderProps {
  name: string;
  expanded: boolean;
  /** Id of the region this button shows and hides. */
  controls: string;
  onToggle: () => void;
}

/** The group's name as a button: it opens and closes the group's holdings (Enter and Space, `aria-expanded`). */
export function GroupExpander({ name, expanded, controls, onToggle }: GroupExpanderProps) {
  return (
    <Button
      color="inherit"
      size="small"
      aria-expanded={expanded}
      aria-controls={expanded ? controls : undefined}
      onClick={onToggle}
      startIcon={
        <ChevronRightIcon sx={{ transition: 'transform 120ms', transform: expanded ? 'rotate(90deg)' : 'none' }} />
      }
      sx={{
        justifyContent: 'flex-start',
        textAlign: 'left',
        fontWeight: 600,
        // Long names wrap instead of widening the column.
        whiteSpace: 'normal',
        wordBreak: 'break-word',
        px: 0.5,
        '& .MuiButton-startIcon': { mr: 0.5, ml: 0 },
      }}
    >
      {name}
    </Button>
  );
}
