import CancelIcon from '@mui/icons-material/CancelOutlined';
import CheckIcon from '@mui/icons-material/CheckCircleOutlined';
import HelpIcon from '@mui/icons-material/HelpOutlineOutlined';
import { Chip } from '@mui/material';
import { termStateLabel } from '../lib/mlText';

const looks = {
  // Holds is a plain result, not praise: it keeps the neutral colour.
  holds: { color: 'default', Icon: CheckIcon },
  fails: { color: 'error', Icon: CancelIcon },
  // Unavailable is NOT a pass: a help icon, the words, no success colour.
  unavailable: { color: 'default', Icon: HelpIcon },
} as const;

/** A predicate term's state as an icon and words; colour is an addition. Anything but `holds` says "not a pass". */
export function TermStateChip({ state }: { state: string }) {
  const look = state in looks ? looks[state as keyof typeof looks] : looks.unavailable;
  return (
    <Chip
      size="small"
      variant="outlined"
      color={look.color}
      icon={<look.Icon aria-hidden />}
      label={state in looks ? termStateLabel(state) : `${termStateLabel(state)} (not a pass)`}
    />
  );
}
