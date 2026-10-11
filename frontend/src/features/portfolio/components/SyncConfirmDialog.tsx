import { Alert, Typography } from '@mui/material';
import { ConfirmDialog } from '@/shared/ui/ConfirmDialog';
import type { Confirmation } from '../lib/errors';

interface SyncConfirmDialogProps {
  confirmation: Confirmation | null;
  busy: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}

const holdings = (count: number) => `${String(count)} ${count === 1 ? 'holding' : 'holdings'}`;

/** What Dhan's answer would do, in words: "Dhan listed 0 holdings while 5 are open" / "Dhan listed 2 holdings but 4 of your 6 open holdings are missing". */
function describe({ listedHoldings, openHoldings, wouldClose }: Confirmation): string {
  if (listedHoldings === 0) {
    return `Dhan listed 0 holdings while ${String(openHoldings)} ${openHoldings === 1 ? 'is' : 'are'} open — ${wouldClose >= openHoldings ? 'all of them' : String(wouldClose)} would be closed.`;
  }
  return `Dhan listed ${holdings(listedHoldings)} but ${String(wouldClose)} of your ${String(openHoldings)} open holdings are missing — they would be closed.`;
}

/** The safeguard: Dhan's answer would close all or most holdings, which is more often a broken answer than a sale of everything. */
export function SyncConfirmDialog({ confirmation, busy, onConfirm, onCancel }: SyncConfirmDialogProps) {
  return (
    <ConfirmDialog
      open={confirmation !== null}
      title="Close these holdings?"
      confirmLabel="Confirm and sync"
      destructive
      busy={busy}
      onConfirm={onConfirm}
      onCancel={onCancel}
    >
      {confirmation?.stale && (
        <Alert severity="warning" sx={{ mb: 1.5 }}>
          Dhan’s answer changed since you looked — review again.
        </Alert>
      )}
      <Typography component="span">{confirmation && describe(confirmation)} Confirm only if that is right.</Typography>
      <Typography component="span" sx={{ display: 'block', mt: 1 }}>
        If it is not, cancel and try again later: closing and reopening holdings would lose the dates you set.
      </Typography>
    </ConfirmDialog>
  );
}
