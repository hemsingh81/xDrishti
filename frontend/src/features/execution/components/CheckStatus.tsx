import CancelIcon from '@mui/icons-material/CancelOutlined';
import CheckCircleIcon from '@mui/icons-material/CheckCircleOutlined';
import HelpIcon from '@mui/icons-material/HelpOutlineOutlined';
import { Stack, Typography } from '@mui/material';
import type { ReactElement } from 'react';

import type { Outcome } from '../outcome';

const looks: Record<Outcome, { word: string; icon: ReactElement }> = {
  pass: { word: 'Pass', icon: <CheckCircleIcon color="success" fontSize="small" aria-hidden /> },
  fail: { word: 'Fail', icon: <CancelIcon color="error" fontSize="small" aria-hidden /> },
  unavailable: { word: 'Unavailable', icon: <HelpIcon color="warning" fontSize="small" aria-hidden /> },
};

/** Icon plus a word: status is never colour alone, and only `pass` is ever green. */
export function CheckStatus({ outcome }: { outcome: Outcome }) {
  const look = looks[outcome];
  return (
    <Stack direction="row" sx={{ alignItems: 'center', gap: 0.5 }}>
      {look.icon}
      <Typography component="span" variant="body2" sx={{ fontWeight: 600 }}>
        {look.word}
      </Typography>
    </Stack>
  );
}
