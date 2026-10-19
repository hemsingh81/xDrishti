import { Button, Divider, Stack, Typography } from '@mui/material';
import type { PriceAlert } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';
import { LoadingState } from '@/shared/ui';
import type { InfiniteRows } from '@/shared/lib/useInfiniteRows';
import type { PriceAlertPage } from '@/shared/api';
import { alertStatusLabels, type AlertStatus } from '../../lib/alerts';
import { AlertRow } from './AlertRow';
import { RetryAlert } from '../RetryAlert';

const emptyText: Record<AlertStatus, string> = {
  active: 'No active alerts. Set one with Set alert on a holding or in the Action center, or with New alert.',
  triggered: 'No alert has triggered yet.',
  cancelled: 'No alert has been cancelled.',
};

interface AlertsListProps {
  status: AlertStatus;
  result: InfiniteRows<PriceAlert, PriceAlertPage>;
  onCancel: (alert: PriceAlert) => void;
}

/** The alerts of one tab, newest first, 50 at a time ("Show more"), with loading, error and empty states. */
export function AlertsList({ status, result, onCancel }: AlertsListProps) {
  if (result.isPending) return <LoadingState label="Loading alerts" />;
  if (result.isError) {
    return <RetryAlert error={result.error} fallback="The alerts could not be loaded." onRetry={result.refetch} />;
  }
  if (result.rows.length === 0) {
    return (
      <Typography color="text.secondary" sx={{ py: 3 }}>
        {emptyText[status]}
      </Typography>
    );
  }
  return (
    <>
      <Stack
        component="ul"
        aria-label={`${alertStatusLabels[status]} alerts`}
        divider={<Divider component="li" />}
        sx={{ m: 0, p: 0, listStyle: 'none' }}
      >
        {result.rows.map((alert) => (
          <AlertRow key={alert.id} alert={alert} onCancel={onCancel} />
        ))}
      </Stack>
      <Stack spacing={0.5} sx={{ alignItems: 'flex-start', mt: 1.5 }}>
        <Typography variant="caption" color="text.secondary" role="status" className="numeric">
          {`Showing ${formatNumber(result.rows.length)} of ${formatNumber(result.total)}`}
        </Typography>
        {result.loadMoreError && (
          <RetryAlert
            error={result.loadMoreError}
            fallback="The next alerts could not be loaded."
            severity="warning"
            onRetry={result.retryMore}
          />
        )}
        {result.hasMore && !result.loadMoreError && (
          <Button size="small" loading={result.isFetchingNextPage} onClick={result.loadMore}>
            Show more
          </Button>
        )}
      </Stack>
    </>
  );
}
