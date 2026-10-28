import CheckIcon from '@mui/icons-material/CheckCircleOutlineOutlined';
import HelpIcon from '@mui/icons-material/HelpOutlineOutlined';
import CloseIcon from '@mui/icons-material/HighlightOffOutlined';
import { Stack, Typography } from '@mui/material';

/** A verdict as icon and word (MET, NOT MET, NOT EVALUABLE), never colour alone. */
export function PhaseVerdict({ verdict }: { verdict: string }) {
  const Icon = verdict === 'MET' ? CheckIcon : verdict === 'NOT MET' ? CloseIcon : HelpIcon;
  const color = verdict === 'MET' ? 'success.main' : verdict === 'NOT MET' ? 'error.main' : 'warning.main';
  return (
    <Stack direction="row" sx={{ gap: 0.5, alignItems: 'center', color }}>
      <Icon fontSize="small" aria-hidden />
      <Typography component="span" sx={{ fontWeight: 600 }}>
        {verdict}
      </Typography>
    </Stack>
  );
}
