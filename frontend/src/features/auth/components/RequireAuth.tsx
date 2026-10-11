import { Box, Button } from '@mui/material';
import { Navigate, Outlet, useLocation } from 'react-router';
import { ErrorState, LoadingState } from '@/shared/ui';
import { useSession } from '../api/sessionQuery';
import { loginPath } from '../lib/nextPath';

/**
 * Route guard (layout route): renders the nested routes only with a valid session.
 * Without one it redirects — replacing the history entry — to /login?next=<where the user wanted to go>.
 * After an explicit sign-out the user goes to a plain /login.
 */
export function RequireAuth() {
  const { data, isPending, error, refetch } = useSession();
  const location = useLocation();

  if (isPending) {
    return <LoadingState label="Checking your session…" />;
  }
  if (!data) {
    return (
      <Box sx={{ p: 3 }}>
        <ErrorState error={error} />
        <Button
          variant="outlined"
          onClick={() => {
            void refetch();
          }}
        >
          Try again
        </Button>
      </Box>
    );
  }
  if (data.status === 'anonymous') {
    const next = data.reason === 'signed_out' ? undefined : `${location.pathname}${location.search}`;
    return <Navigate to={loginPath(next)} replace />;
  }
  return <Outlet />;
}
