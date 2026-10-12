import FactCheckIcon from '@mui/icons-material/FactCheckOutlined';
import { Button, CircularProgress } from '@mui/material';
import { stateAlpha } from '@/theme';

interface RunChecksButtonProps {
  /** A job is running or was just started. */
  busy: boolean;
  /** The state of the page is not known yet, so starting would be a guess. */
  disabled: boolean;
  onRun: () => void;
}

/**
 * "Run checks" over every tracked instrument. While a job runs the button is `aria-disabled` (not `disabled`), so it
 * keeps focus, and shows a spinner.
 */
export function RunChecksButton({ busy, disabled, onRun }: RunChecksButtonProps) {
  return (
    <Button
      variant="contained"
      disabled={disabled}
      aria-disabled={busy || undefined}
      startIcon={busy ? <CircularProgress size={16} color="inherit" aria-hidden /> : <FactCheckIcon />}
      onClick={() => {
        if (!busy) onRun();
      }}
      sx={{ '&[aria-disabled="true"]': { opacity: stateAlpha.busy, cursor: 'default' } }}
    >
      Run checks
    </Button>
  );
}
