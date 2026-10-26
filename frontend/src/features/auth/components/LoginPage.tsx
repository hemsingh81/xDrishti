import { Alert, alpha, Box, Typography } from '@mui/material';
import { keyframes } from '@mui/material/styles';
import { useEffect, useState } from 'react';
import { Navigate, useSearchParams } from 'react-router';
import { LoadingState } from '@/shared/ui';
import { elevation, rem } from '@/theme';
import { useSession } from '../api/sessionQuery';
import { resolveNext } from '../lib/nextPath';
import { LoginFooter } from './LoginFooter';
import { LoginForm } from './LoginForm';
import { LoginHero } from './LoginHero';

const rise = keyframes`from { opacity: 0; transform: translateY(12px); }`;

/** Docs and the prototype are not React routes: leave the SPA with a full page load. */
function StaticRedirect({ to }: { to: string }) {
  useEffect(() => {
    window.location.assign(to);
  }, [to]);
  return <LoadingState label="Redirecting…" announce />;
}

/**
 * Public sign-in page, laid out like the prototype's: one rounded panel on a dotted background, the hero (brand, headline,
 * what the app does) on the left and the form on the right; below 860 px the form comes first and the hero shrinks to
 * its brand row. Already signed in → straight to `next` (validated) or `/`.
 */
export function LoginPage() {
  const [params] = useSearchParams();
  const { data, isPending } = useSession();
  // The notice must survive the background refetch that follows an expired session (the server only says it once).
  const [sawExpired, setSawExpired] = useState(false);
  if (data?.status === 'anonymous' && data.reason === 'expired' && !sawExpired) {
    setSawExpired(true);
  }

  if (isPending) {
    return <LoadingState label="Checking your session…" announce />;
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
        padding: '32px 20px',
        bgcolor: 'background.default',
        backgroundImage: `radial-gradient(circle at 1px 1px, ${alpha(theme.palette.text.tertiary, 0.22)} 1px, transparent 0)`,
        backgroundSize: '22px 22px',
      })}
    >
      <Box
        sx={(theme) => ({
          width: 'min(1040px, 100%)',
          display: 'grid',
          gridTemplateColumns: 'minmax(0, 1.05fr) minmax(0, 0.95fr)',
          bgcolor: 'background.paper',
          border: `1px solid ${theme.palette.divider}`,
          borderRadius: '20px',
          boxShadow: elevation[theme.palette.mode].lg,
          overflow: 'hidden',
          animation: `${rise} 0.5s cubic-bezier(0.2, 0.8, 0.2, 1) both`,
          '@media (min-width: 1800px)': { width: 'min(1180px, 100%)' },
          '@media (min-width: 2560px)': { width: 'min(1360px, 100%)' },
          [theme.breakpoints.down(861)]: { gridTemplateColumns: 'minmax(0, 1fr)', maxWidth: 460 },
        })}
      >
        <Box
          sx={(theme) => ({
            gridColumn: 1,
            gridRow: 1,
            [theme.breakpoints.down(861)]: { gridRow: 2 },
          })}
        >
          <LoginHero />
        </Box>
        <Box
          sx={(theme) => ({
            gridColumn: 2,
            gridRow: 1,
            display: 'flex',
            flexDirection: 'column',
            justifyContent: 'center',
            padding: '40px 44px 20px',
            '@media (min-width: 1800px)': { padding: '48px 52px 24px' },
            [theme.breakpoints.down(861)]: { gridColumn: 1, padding: '28px 24px 16px' },
          })}
        >
          <Box sx={{ width: '100%', maxWidth: 380, mx: 'auto' }}>
            <Typography
              component="h2"
              sx={{ fontSize: rem(24), fontWeight: 700, letterSpacing: '-0.01em', lineHeight: 1.5, mb: '4px' }}
            >
              Sign in
            </Typography>
            <Typography color="text.secondary">Private workspace — sign in to continue.</Typography>
            {sawExpired && (
              <Alert severity="info" role="status" sx={{ mt: 2 }}>
                Your session expired — please sign in again.
              </Alert>
            )}
            <Box sx={{ mt: 3 }}>
              <LoginForm />
            </Box>
          </Box>
          <LoginFooter />
        </Box>
      </Box>
    </Box>
  );
}
