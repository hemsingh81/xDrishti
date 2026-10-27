import InfoIcon from '@mui/icons-material/InfoOutlined';
import WarningIcon from '@mui/icons-material/WarningAmberOutlined';
import { Box, Typography } from '@mui/material';
import { ungroundedWords, words } from '../lib/wording';

/** The result of the server's figure check, in words (never a green tick: "checked" is not "verified"). `no_numbers` shows nothing. */
export function GroundingBanner({
  status,
  count,
  notLocated = 0,
}: {
  status: string;
  count: number;
  notLocated?: number;
}) {
  if (status === 'no_numbers') return null;
  const warn = status === 'ungrounded';
  const located = count > 0 ? ungroundedWords(count) : null;
  const missing =
    notLocated > 0 ? `${notLocated} ${notLocated === 1 ? 'figure' : 'figures'} not located in the text` : null;
  const text = warn
    ? [located, missing].filter(Boolean).join('. ') || words.someUngrounded
    : status === 'grounded'
      ? words.checked
      : words.notChecked;
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
