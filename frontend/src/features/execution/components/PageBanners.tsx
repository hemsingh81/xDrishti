import { Alert, AlertTitle, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { positionsQuery } from '../api/viewsApi';

/** Canary pending (`exec.canary_pending`): orders are capped at one share; shown only while the switch is ON. */
export function CanaryBanner({ pending }: { pending: boolean | null | undefined }) {
  if (pending !== true) return null;
  return (
    <Alert severity="warning" role="status" sx={{ mb: 2 }}>
      Canary pending: orders are capped at 1 share until C1 and C2 are recorded
    </Alert>
  );
}

/** A position without confirmed protection is shown at the top of every tab, as an error (criterion 4). */
export function ProtectionBanner() {
  const { data } = useQuery(positionsQuery());
  const missing = data?.filter((p) => p.protection === 'missing') ?? [];
  if (missing.length === 0) return null;
  return (
    <Alert severity="error" role="alert" sx={{ mb: 2 }}>
      <AlertTitle>Protection is missing</AlertTitle>
      {missing.map((p) => (
        <Typography key={`${p.accountId}:${p.symbol}`} variant="body2">
          {p.symbol} ({p.account}): {p.netQuantity} shares have no stop at the broker.
        </Typography>
      ))}
    </Alert>
  );
}

const withSeconds = new Intl.DateTimeFormat('en-GB', {
  timeZone: 'Asia/Kolkata',
  hour: '2-digit',
  minute: '2-digit',
  second: '2-digit',
  hour12: false,
});

/** The last successful read is older than 30 s while the market is open: the numbers may be old (criterion 12). */
export function StaleView({ updatedAt, marketOpen, now }: { updatedAt: number; marketOpen: boolean; now: number }) {
  if (!marketOpen || updatedAt === 0 || now - updatedAt <= 30_000) return null;
  return (
    <Alert severity="warning" role="status" sx={{ mb: 2 }}>
      Last updated {withSeconds.format(updatedAt)}, the numbers may be old
    </Alert>
  );
}
