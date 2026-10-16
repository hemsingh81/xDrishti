import UploadIcon from '@mui/icons-material/UploadFileOutlined';
import SyncIcon from '@mui/icons-material/Sync';
import { Button, Card, CardContent, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import type { SyncRun } from '@/shared/api';
import { brokerHistoryStatusQuery } from '../api/brokerHistoryQueries';
import { syncStatusQuery } from '../api/portfolioQueries';
import { useRefreshOnNewRun } from '../hooks/useRefreshOnNewRun';
import { useSyncFlow } from '../hooks/useSyncFlow';
import { BrokerHistoryBar } from './broker/BrokerHistoryBar';
import { ProblemNotice } from './ProblemNotice';
import { RefreshPricesButton } from './RefreshPricesButton';
import { SyncConfirmDialog } from './SyncConfirmDialog';
import { SyncStatusLine } from './SyncStatusLine';

interface SyncBarProps {
  /** The one account that Sync now acts on; null when several accounts are listed. */
  account: { id: string; name: string } | null;
  /** The account is connected to Dhan (a sync needs it). */
  connected: boolean;
  /** The latest run of any account, shown when no single account is in play. */
  fallbackRun: SyncRun | null;
  /** Refresh prices: runs the quote service now. */
  refresh: { available: boolean; busy: boolean; onRefresh: () => void };
  /** Opens the Import files dialog (Dhan's downloaded reports). */
  onImportFiles: () => void;
}

/**
 * Last sync, the daily-refresh setting and next run, and the Sync now and Refresh prices buttons; for one account also the
 * read of its trades and ledger from Dhan, under them.
 */
export function SyncBar({ account, connected, fallbackRun, refresh, onImportFiles }: SyncBarProps) {
  const accountId = account?.id ?? null;
  const status = useQuery({ ...syncStatusQuery(accountId ?? ''), enabled: accountId !== null });
  const flow = useSyncFlow(accountId);
  useRefreshOnNewRun(status.data?.last?.id);

  // The trades and ledger read of the account (the same query as its own bar): the API refuses one sync during the other.
  const broker = useQuery({ ...brokerHistoryStatusQuery(accountId ?? ''), enabled: accountId !== null });
  const running = flow.pending || (status.data?.running ?? false);
  const canSync = account !== null && connected && !running && !broker.data?.running;

  return (
    <Card sx={{ mb: 2, flexShrink: 0 }}>
      <CardContent sx={{ '&:last-child': { pb: 2 } }}>
        <Stack spacing={1.5}>
          <Stack
            direction={{ xs: 'column', md: 'row' }}
            sx={{ gap: 1.5, alignItems: { md: 'center' }, justifyContent: 'space-between' }}
          >
            <SyncStatusLine status={status.data ?? null} fallbackRun={fallbackRun} />
            <Stack direction="row" sx={{ gap: 1, flexWrap: 'wrap', flexShrink: 0 }}>
              <Button variant="outlined" startIcon={<UploadIcon />} onClick={onImportFiles}>
                Import files
              </Button>
              <RefreshPricesButton available={refresh.available} busy={refresh.busy} onRefresh={refresh.onRefresh} />
              <Button
                variant="contained"
                startIcon={<SyncIcon />}
                loading={running}
                disabled={!canSync}
                onClick={flow.run}
              >
                {running ? 'Syncing…' : 'Sync now'}
              </Button>
            </Stack>
          </Stack>
          {!account && (
            <Typography variant="body2" color="text.secondary">
              Pick an account above to sync it.
            </Typography>
          )}
          {status.isError && account && (
            <ProblemNotice
              error={status.error}
              accountId={accountId}
              fallback="The sync status could not be loaded."
              onRetry={() => {
                void status.refetch();
              }}
            />
          )}
          {flow.error ? (
            <ProblemNotice error={flow.error} accountId={accountId} fallback="The sync did not complete." />
          ) : null}
          {account && (
            <BrokerHistoryBar key={account.id} account={account} connected={connected} holdingsRunning={running} />
          )}
        </Stack>
      </CardContent>
      <SyncConfirmDialog
        confirmation={flow.confirmation}
        busy={flow.pending}
        onConfirm={flow.confirm}
        onCancel={flow.cancelConfirmation}
      />
    </Card>
  );
}
