import { Alert, Box, Button, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { Link } from 'react-router';
import { LoadingState } from '@/shared/ui';
import { AppDialog, AppDialogBody, AppDialogFooter, AppDialogHeader } from '@/shared/ui/dialog';
import { accountsQuery } from '../api/accountsQueries';
import { AccountRefreshRow } from './AccountRefreshRow';

interface PortfolioRefreshDialogProps {
  onClose: () => void;
  /** Called after an account's refresh setting was saved (the opener reads what depends on it again). */
  onSaved?: () => void;
  /** What the opener knows about the sync (its next and last run), under the title. */
  subtitle?: ReactNode;
}

/**
 * The automatic refresh of every account that has the Portfolio role, in one place: Off, or once a day at a time (India
 * time, ADR 0013). Saving goes through the same call as the account's Portfolio tab and sends everything else back as
 * it is stored. An account that would import its holdings for the first time on saving is shown read-only, with a link
 * to its Portfolio tab, where the first import date is chosen.
 */
export function PortfolioRefreshDialog({ onClose, onSaved, subtitle }: PortfolioRefreshDialogProps) {
  const accounts = useQuery(accountsQuery);
  const portfolio = accounts.data?.filter((account) => account.roles.portfolio.enabled);

  return (
    <AppDialog open onClose={onClose} maxWidth="sm" fullScreenOnMobile>
      <AppDialogHeader subtitle={subtitle}>Account sync settings</AppDialogHeader>
      <AppDialogBody>
        <Stack sx={{ gap: 2, pt: 0.5 }}>
          <Typography variant="body2" color="text.secondary">
            Each account with the Portfolio role refreshes its holdings by itself, once a day at the time you choose
            (India time), and catches up once if a run was missed.
          </Typography>
          {accounts.isPending && <LoadingState label="Loading accounts" />}
          {accounts.isError && (
            <Alert
              severity="error"
              action={
                <Button
                  color="inherit"
                  size="small"
                  onClick={() => {
                    void accounts.refetch();
                  }}
                >
                  Retry
                </Button>
              }
            >
              The accounts could not be loaded.
            </Alert>
          )}
          {portfolio?.length === 0 && (
            <Box role="status" sx={{ py: 3, textAlign: 'center' }}>
              <Typography sx={{ fontWeight: 600 }}>No account has the Portfolio role</Typography>
              <Typography variant="body2" color="text.secondary">
                Turn the role on for an account in{' '}
                <Link to="/accounts" onClick={onClose}>
                  Accounts
                </Link>
                , then its refresh can be set here.
              </Typography>
            </Box>
          )}
          {portfolio && portfolio.length > 0 && (
            <Box component="ul" aria-label="Accounts" sx={{ m: 0, p: 0, display: 'grid', gap: 1.5, listStyle: 'none' }}>
              {portfolio.map((account) => (
                <AccountRefreshRow key={account.id} account={account} onSaved={onSaved} />
              ))}
            </Box>
          )}
        </Stack>
      </AppDialogBody>
      <AppDialogFooter>
        <Button onClick={onClose}>Done</Button>
      </AppDialogFooter>
    </AppDialog>
  );
}
