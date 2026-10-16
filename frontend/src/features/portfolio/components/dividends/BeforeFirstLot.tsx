import { Alert, Button } from '@mui/material';
import { lazy, Suspense, useState } from 'react';
import type { DividendPreviewReport } from '@/shared/api';
import { formatDate, formatInrPrecise, formatNumber } from '@/shared/lib/format';
import { useToast } from '@/shared/ui/toastContext';
import { useBrokerHistorySync } from '../../hooks/useBrokerHistorySync';

// The dialog is loaded when first opened: most visits never get there.
const SyncFromDialog = lazy(async () => ({ default: (await import('../broker/SyncFromDialog')).SyncFromDialog }));

interface BeforeFirstLotProps {
  account: { id: string; name: string };
  before: DividendPreviewReport['beforeFirstLot'];
}

/**
 * Dividends dated before the account's first lot are imported, but the net XIRR cannot start with an inflow: the notice says so
 * and opens "Read from an earlier date" (the trades and ledger read of the account), so the lots reach back far enough.
 */
export function BeforeFirstLot({ account, before }: BeforeFirstLotProps) {
  const { notify } = useToast();
  const { start, reset, running, error } = useBrokerHistorySync(account.id);
  const [open, setOpen] = useState(false);
  if (before.count === 0) return null;
  return (
    <>
      <Alert
        severity="warning"
        action={
          <Button
            color="inherit"
            size="small"
            onClick={() => {
              reset();
              setOpen(true);
            }}
          >
            Read from an earlier date
          </Button>
        }
      >
        {`${formatNumber(before.count)} ${before.count === 1 ? 'dividend is' : 'dividends are'} dated before your first lot${before.firstLotDate ? ` (${formatDate(before.firstLotDate)})` : ''} — net XIRR needs earlier history. They add ${formatInrPrecise(before.amount)}.`}
      </Alert>
      {open && (
        <Suspense fallback={null}>
          <SyncFromDialog
            accountId={account.id}
            accountName={account.name}
            busy={running}
            error={error}
            onReset={reset}
            onStart={(options, onStarted) => {
              start(options, () => {
                onStarted();
                notify(
                  'Reading your trades and ledger from the earlier date. Preview the file again when it has finished.',
                  'info',
                );
              });
            }}
            onClose={() => {
              reset();
              setOpen(false);
            }}
          />
        </Suspense>
      )}
    </>
  );
}
