import { Box, Button } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { accountsQuery } from '@/features/accounts';
import { ErrorState, LoadingState } from '@/shared/ui';
import { ToastProvider } from '@/shared/ui/ToastProvider';
import { PortfolioHeader } from './PortfolioHeader';
import { PortfolioView } from './PortfolioView';

function PortfolioContent() {
  const accounts = useQuery(accountsQuery);
  if (accounts.isPending) {
    return (
      <>
        <PortfolioHeader />
        <LoadingState label="Loading accounts" />
      </>
    );
  }
  if (accounts.isError) {
    return (
      <>
        <PortfolioHeader />
        <ErrorState error={accounts.error} />
        <Box>
          <Button
            variant="outlined"
            onClick={() => {
              void accounts.refetch();
            }}
          >
            Retry
          </Button>
        </Box>
      </>
    );
  }
  return <PortfolioView accounts={accounts.data} />;
}

/**
 * The Portfolio screen. From `md` up it fills the viewport under the top bar: the header, filters, totals and sync bar
 * stay put, the holdings scroll inside their own card (further pages load as the end comes into view) and the
 * "Showing N of total" footer stays at the bottom. Account, bucket, status, search, sort and the open holding live in
 * the URL.
 */
export function PortfolioPage() {
  return (
    <ToastProvider>
      <PortfolioContent />
    </ToastProvider>
  );
}
