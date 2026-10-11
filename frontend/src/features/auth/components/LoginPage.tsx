import DarkModeIcon from '@mui/icons-material/DarkModeOutlined';
import LightModeIcon from '@mui/icons-material/LightModeOutlined';
import { Alert, alpha, Box, Button, Paper, Stack, Typography } from '@mui/material';
import { useEffect, useState } from 'react';
import { Navigate, useSearchParams } from 'react-router';
import { BrandMark, LoadingState } from '@/shared/ui';
import { useColorMode } from '@/theme';
import { useSession } from '../api/sessionQuery';
import { resolveNext } from '../lib/nextPath';
import { LoginForm } from './LoginForm';

/** Docs and the prototype are not React routes: leave the SPA with a full page load. */
function StaticRedirect({ to }: { to: string }) {
  useEffect(() => {
    window.location.assign(to);
  }, [to]);
  return <LoadingState label="Redirecting…" />;
}

/** Public sign-in page: centred card at every width. Already signed in → straight to `next` (validated) or `/`. */
export function LoginPage() {
  const [params] = useSearchParams();
  const { data, isPending } = useSession();
  const { mode, toggle } = useColorMode();
  // The notice must survive the background refetch that follows an expired session (the server only says it once).
  const [sawExpired, setSawExpired] = useState(false);
  if (data?.status === 'anonymous' && data.reason === 'expired' && !sawExpired) {
    setSawExpired(true);
  }

  if (isPending) {
    return <LoadingState label="Checking your session…" />;
  }
  if (data?.status === 'authenticated') {
    const target = resolveNext(params.get('next'));
    return target.kind === 'static' ? <StaticRedirect to={target.path} /> : <Navigate to={target.path} replace />;
  }

  return (
    <Box
      component="main"
      sx={(theme) => ({
        minHeight: '100dvh',
        display: 'grid',
        placeItems: 'center',
        p: { xs: 2, sm: 3 },
        bgcolor: 'background.default',
        backgroundImage: `radial-gradient(circle at 1px 1px, ${alpha(theme.palette.text.secondary, 0.22)} 1px, transparent 0)`,
        backgroundSize: '22px 22px',
      })}
    >
      <Stack spacing={2} sx={{ width: 'min(100%, 420px)' }}>
        <Paper variant="outlined" sx={{ p: { xs: 3, sm: 4 }, boxShadow: 3 }}>
          <Stack direction="row" spacing={1.5} sx={{ alignItems: 'center', mb: 3 }}>
            <BrandMark size={40} />
            <Box>
              <Typography sx={{ fontWeight: 700, lineHeight: 1.2 }}>xDrishti</Typography>
              <Typography variant="caption" color="text.secondary" lang="hi">
                दृष्टि · vision, insight
              </Typography>
            </Box>
          </Stack>
          <Typography component="h1" variant="h1" sx={{ mb: 0.5 }}>
            Sign in
          </Typography>
          <Typography color="text.secondary" sx={{ mb: 2.5 }}>
            Private workspace — sign in to continue.
          </Typography>
          {sawExpired && (
            <Alert severity="info" role="status" sx={{ mb: 2 }}>
              Your session expired — please sign in again.
            </Alert>
          )}
          <LoginForm />
        </Paper>
        <Stack direction="row" sx={{ alignItems: 'center', justifyContent: 'space-between' }}>
          <Typography variant="caption" color="text.secondary">
            Personal use · single owner
          </Typography>
          <Button
            size="small"
            color="inherit"
            startIcon={mode === 'dark' ? <LightModeIcon /> : <DarkModeIcon />}
            onClick={toggle}
          >
            {mode === 'dark' ? 'Light mode' : 'Dark mode'}
          </Button>
        </Stack>
      </Stack>
    </Box>
  );
}
