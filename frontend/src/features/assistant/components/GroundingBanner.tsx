import InfoIcon from '@mui/icons-material/InfoOutlined';
import WarningIcon from '@mui/icons-material/WarningAmberOutlined';
import { Box, Typography } from '@mui/material';
import { ungroundedWords, words } from '../lib/wording';

/** The result of the server's figure check, in words (never a green tick: "checked" is not "verified"). `no_numbers` shows nothing. */
export function GroundingBanner({ status, count }: { status: string; count: number }) {
  if (status === 'no_numbers') return null;
  const warn = status === 'ungrounded';
  const text = warn ? ungroundedWords(count) : status === 'grounded' ? words.checked : words.notChecked;
  const Icon = warn ? WarningIcon : InfoIcon;
  return (
    <Box
      sx={{ display: 'flex', gap: 0.75, alignItems: 'center', mt: 1, color: warn ? 'warning.main' : 'text.secondary' }}
    >
      <Icon aria-hidden fontSize="small" />
      <Typography variant="body2" sx={{ color: 'inherit' }}>
        {text}
      </Typography>
    </Box>
  );
}
