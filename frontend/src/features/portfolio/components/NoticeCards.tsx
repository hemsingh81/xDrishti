import { Alert, Button, Card, CardContent, Stack, Typography } from '@mui/material';
import { Link as RouterLink } from 'react-router';
import { accountTabPath } from '../lib/errors';

/** The account is not signed in at Dhan: say so before anything fails, with the way to fix it. */
export function NotConnectedNotice({ accountId, accountName }: { accountId: string; accountName: string }) {
  return (
    <Alert
      severity="warning"
      sx={{ mb: 2, flexShrink: 0 }}
      action={
        <Button component={RouterLink} to={accountTabPath(accountId, 'connection')} color="inherit" size="small">
          Open the Connection tab
        </Button>
      }
    >
      Connect the account first. {accountName} is not signed in at Dhan, so its holdings cannot be synced until it is.
      What is already here stays.
    </Alert>
  );
}

/** No holding in the system for this view: the way in is the account's Portfolio settings (the first import). */
export function EmptyHoldings({ accountId }: { accountId: string | null }) {
  return (
    <Stack spacing={1.5} sx={{ alignItems: 'center', textAlign: 'center', py: 6, px: 2 }}>
      <Typography variant="h2" component="h2">
        No holdings yet
      </Typography>
      <Typography color="text.secondary" sx={{ maxWidth: 520 }}>
        Holdings are imported when you save the account&rsquo;s Portfolio settings (Account &rarr; Portfolio), where you
        also choose each purchase date so profit and XIRR can be worked out. After that they refresh by themselves or
        with Sync now.
      </Typography>
      <Button
        component={RouterLink}
        to={accountId ? accountTabPath(accountId, 'portfolio') : '/accounts?tab=portfolio'}
        variant="contained"
      >
        Open Portfolio settings
      </Button>
    </Stack>
  );
}

/** No account has the Portfolio role: nothing can be shown. */
export function NoPortfolioAccounts() {
  return (
    <Card>
      <CardContent>
        <Stack spacing={1.5} sx={{ alignItems: 'center', textAlign: 'center', py: 4 }}>
          <Typography variant="h2" component="h2">
            No account has the Portfolio role yet
          </Typography>
          <Typography color="text.secondary" sx={{ maxWidth: 520 }}>
            Switch the Portfolio role on for an account, then its holdings appear here.
          </Typography>
          <Button component={RouterLink} to="/accounts?tab=roles" variant="contained">
            Open Accounts
          </Button>
        </Stack>
      </CardContent>
    </Card>
  );
}
