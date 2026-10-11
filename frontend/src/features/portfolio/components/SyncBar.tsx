import SyncIcon from '@mui/icons-material/Sync';
import { Button, Card, CardContent, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import type { SyncRun } from '@/shared/api';
import { syncStatusQuery } from '../api/portfolioQueries';
import { useRefreshOnNewRun } from '../hooks/useRefreshOnNewRun';
import { useSyncFlow } from '../hooks/useSyncFlow';
import { ProblemNotice } from './ProblemNotice';
import { SyncConfirmDialog } from './SyncConfirmDialog';
import { SyncStatusLine } from './SyncStatusLine';

interface SyncBarProps {
  /** The one account that Sync now acts on; null when several accounts are listed. */
  account: { id: string; name: string } | null;
  /** The account is connected to Dhan (a sync needs it). */
  connected: boolean;
  /** The latest run of any account, shown when no single account is in play. */
  fallbackRun: SyncRun | null;
}

/** Last sync, the daily-refresh setting and next run, and the Sync now button. */
export function SyncBar({ account, connected, fallbackRun }: SyncBarProps) {
  const accountId = account?.id ?? null;
  const status = useQuery({ ...syncStatusQuery(accountId ?? ''), enabled: accountId !== null });
  const flow = useSyncFlow(accountId);
  useRefreshOnNewRun(status.data?.last?.id);

  const running = flow.pending || (status.data?.running ?? false);
  const canSync = account !== null && connected && !running;

  return (
    <Card sx={{ mb: 2, flexShrink: 0 }}>
      <CardContent sx={{ '&:last-child': { pb: 2 } }}>
        <Stack spacing={1.5}>
          <Stack
            direction={{ xs: 'column', md: 'row' }}
            sx={{ gap: 1.5, alignItems: { md: 'center' }, justifyContent: 'space-between' }}
          >
            <SyncStatusLine status={status.data ?? null} fallbackRun={fallbackRun} />
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
