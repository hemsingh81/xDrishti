import CancelIcon from '@mui/icons-material/CancelOutlined';
import CheckIcon from '@mui/icons-material/CheckCircleOutlined';
import UnknownIcon from '@mui/icons-material/HelpOutlined';
import { Box } from '@mui/material';
import { known } from './nullsFormat';

/** Status as an icon and a word, never colour alone. */
export function StatusMark({ status }: { status: string | null | undefined }) {
  const s = known(status);
  const Icon = s === 'pass' ? CheckIcon : s === 'fail' ? CancelIcon : UnknownIcon;
  return (
    <Box component="span" sx={{ display: 'inline-flex', alignItems: 'center', gap: 0.5 }}>
      <Icon fontSize="inherit" aria-hidden />
      {s}
    </Box>
  );
}
