import CancelIcon from '@mui/icons-material/CancelOutlined';
import CheckIcon from '@mui/icons-material/CheckCircleOutlined';
import HelpIcon from '@mui/icons-material/HelpOutlineOutlined';
import { Chip } from '@mui/material';

/** The result of a stated criterion, as the phase checks print it. */
export type Verdict = 'met' | 'not_met' | 'not_evaluable';

const verdicts = {
  met: { label: 'MET', color: 'success', Icon: CheckIcon },
  not_met: { label: 'NOT MET', color: 'error', Icon: CancelIcon },
  // Neither pass nor fail: never the `good` colour. A missing input does not pass a check.
  not_evaluable: { label: 'NOT EVALUABLE', color: 'default', Icon: HelpIcon },
} as const;

/** MET / NOT MET / NOT EVALUABLE as an icon and the words; colour is an addition, never the signal. */
export function VerdictChip({ verdict }: { verdict: Verdict }) {
  const { label, color, Icon } = verdicts[verdict];
  return <Chip size="small" variant="outlined" color={color} icon={<Icon aria-hidden />} label={label} />;
}
