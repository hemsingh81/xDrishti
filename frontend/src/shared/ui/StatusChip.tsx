import { Chip } from '@mui/material';

interface StatusChipProps {
  ok: boolean;
  okLabel?: string;
  badLabel?: string;
}

/** Green/red status pill with a text label (never colour alone — accessible). */
export function StatusChip({ ok, okLabel = 'Healthy', badLabel = 'Unhealthy' }: StatusChipProps) {
  return <Chip size="small" color={ok ? 'success' : 'error'} variant="outlined" label={ok ? okLabel : badLabel} />;
}
