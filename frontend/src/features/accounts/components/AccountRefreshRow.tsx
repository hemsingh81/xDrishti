import { Alert, Box, Button, Skeleton, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import type { AccountSummary } from '@/shared/api';
import { accountQuery } from '../api/accountsQueries';
import { AccountRefreshForm } from './AccountRefreshForm';

interface AccountRefreshRowProps {
  account: AccountSummary;
  onSaved?: () => void;
}

/** One account of the sync settings: its stored settings are read first (loading and error states), then the form. */
export function AccountRefreshRow({ account, onSaved }: AccountRefreshRowProps) {
  const detail = useQuery(accountQuery(account.id));
  return (
    <Box
      component="li"
      aria-label={account.name}
      sx={{ p: 1.5, border: 1, borderColor: 'divider', borderRadius: 1, bgcolor: 'background.paper' }}
    >
      <Typography component="h3" variant="h3" sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 1 }}>
        <Box
          component="span"
          aria-hidden
          sx={{ width: 10, height: 10, borderRadius: '50%', flex: 'none', bgcolor: account.color }}
        />
        {account.name}
      </Typography>
      {detail.isPending && <Skeleton variant="rounded" height={96} aria-label={`Loading ${account.name}`} />}
      {detail.isError && (
        <Alert
          severity="error"
          action={
            <Button
              color="inherit"
              size="small"
              onClick={() => {
                void detail.refetch();
              }}
            >
              Retry
            </Button>
          }
        >
          The settings of {account.name} could not be loaded.
        </Alert>
      )}
      {detail.data && <AccountRefreshForm account={detail.data} fetching={detail.isFetching} onSaved={onSaved} />}
    </Box>
  );
}
