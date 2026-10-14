import DownloadIcon from '@mui/icons-material/CloudDownloadOutlined';
import { Button, Divider, Stack, Typography } from '@mui/material';
import { formatIstTimeAndDay } from '@/shared/lib/format';
import { numeric } from '@/theme';
import { useBrokerHistorySync } from '../../hooks/useBrokerHistorySync';
import { needsReviewSymbols, rebuildText } from '../../lib/brokerText';
import { ProblemNotice } from '../ProblemNotice';
import { BrokerKindLine } from './BrokerKindLine';

interface BrokerHistoryBarProps {
  account: { id: string; name: string };
  /** The account is connected to Dhan (a read needs it). */
  connected: boolean;
  /** The holdings sync of the account runs: the API refuses a second sync meanwhile. */
  holdingsRunning: boolean;
}

/**
 * Trades and ledger read from Dhan (P2-13), under the holdings sync: how far each is read, what the automatic rebuild did,
 * the next scheduled read, and the button that reads now. Mount it per account (`key`): its error belongs to one account.
 */
export function BrokerHistoryBar({ account, connected, holdingsRunning }: BrokerHistoryBarProps) {
  const { status, start, running, error } = useBrokerHistorySync(account.id);
  const data = status.data;
  const review = data ? needsReviewSymbols(data) : [];
  const rebuilt = data ? rebuildText(data) : null;
  return (
    <Stack component="section" aria-label="Trades and ledger from Dhan" spacing={1.5}>
      <Divider />
      <Stack
        direction={{ xs: 'column', md: 'row' }}
        sx={{ gap: 1.5, alignItems: { md: 'center' }, justifyContent: 'space-between' }}
      >
        <Stack spacing={0.5} sx={{ minWidth: 0 }}>
          {data ? (
            <>
              <BrokerKindLine kind="trades" status={data.trades} />
              <BrokerKindLine kind="ledger" status={data.ledger} />
              {rebuilt && (
                <Typography variant="body2" color="text.secondary" sx={numeric}>
                  {rebuilt}
                  {review.length > 0 && ` (${review.join(', ')}) — open the holding and use Rebuild lots`}
                </Typography>
              )}
              <Typography variant="body2" color="text.secondary" sx={numeric}>
                {data.nextRunAt
                  ? `Next scheduled read ${formatIstTimeAndDay(data.nextRunAt)} IST`
                  : 'No scheduled read is planned.'}
              </Typography>
            </>
          ) : status.isPending ? (
            <Typography color="text.secondary" role="status">
              Loading the trades and ledger status…
            </Typography>
          ) : null}
        </Stack>
        <Stack direction="row" sx={{ gap: 1, flexWrap: 'wrap', flexShrink: 0 }}>
          <Button
            variant="contained"
            startIcon={<DownloadIcon />}
            loading={running}
            disabled={!connected || running || holdingsRunning}
            onClick={start}
          >
            {running ? 'Reading…' : 'Sync trades & ledger'}
          </Button>
        </Stack>
      </Stack>
      {status.isError && (
        <ProblemNotice
          error={status.error}
          accountId={account.id}
          fallback="The trades and ledger status could not be loaded."
          onRetry={() => {
            void status.refetch();
          }}
        />
      )}
      {error ? <ProblemNotice error={error} accountId={account.id} fallback="The read did not start." /> : null}
    </Stack>
  );
}
