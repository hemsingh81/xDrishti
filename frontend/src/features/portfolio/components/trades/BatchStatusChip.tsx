import CheckIcon from '@mui/icons-material/CheckCircleOutlined';
import UndoIcon from '@mui/icons-material/UndoOutlined';
import { Chip } from '@mui/material';

/** Imported or Undone, as an icon and a word. */
export function BatchStatusChip({ undone }: { undone: boolean }) {
  return undone ? (
    <Chip size="small" variant="outlined" icon={<UndoIcon />} label="Undone" />
  ) : (
    <Chip size="small" variant="outlined" color="success" icon={<CheckIcon />} label="Imported" />
  );
}
