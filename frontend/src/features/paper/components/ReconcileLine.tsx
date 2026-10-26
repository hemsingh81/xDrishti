import { Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { ErrorState, LoadingState } from '@/shared/ui';
import { paperReconcileQuery } from '../api/paperQueries';

/** Whether the derived ledger agrees with the stored rows, in words. */
export function ReconcileLine({ accountId }: { accountId: string }) {
  const reconcile = useQuery(paperReconcileQuery(accountId));
  if (reconcile.isPending) return <LoadingState label="Checking the ledger…" />;
  if (reconcile.isError) return <ErrorState error={reconcile.error} />;
  const { status, differences } = reconcile.data;
  return (
    <Typography component="div" role="status">
      {status === 'ok' ? (
        'Reconciliation: ok, the ledger agrees with the stored rows.'
      ) : (
        <>
          Reconciliation: differences found.
          <ul>
            {differences.map((d) => (
              <li key={d}>{d}</li>
            ))}
          </ul>
        </>
      )}
    </Typography>
  );
}
