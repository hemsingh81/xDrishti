import CheckIcon from '@mui/icons-material/CheckCircleOutlineOutlined';
import HelpIcon from '@mui/icons-material/HelpOutlineOutlined';
import HourglassIcon from '@mui/icons-material/HourglassEmptyOutlined';
import { Chip } from '@mui/material';
import { stateWords } from '../lib/executionLabels';

/** The state of a row as an icon and a word, never colour alone. */
export function ExecutionStateChip({ state }: { state: string }) {
  const Icon = state === 'value' ? CheckIcon : state === 'insufficient' ? HourglassIcon : HelpIcon;
  return (
    <Chip
      size="small"
      variant="outlined"
      color={state === 'value' ? 'success' : 'default'}
      icon={<Icon />}
      label={stateWords[state] ?? state}
    />
  );
}
