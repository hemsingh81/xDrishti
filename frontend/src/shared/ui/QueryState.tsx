import { Alert, Box, CircularProgress } from '@mui/material';
import { visuallyHidden } from '@mui/utils';

/**
 * Consistent loading indicator for data-driven sections. It is a status region that does not announce itself
 * (`aria-live="off"`): inside the app shell one page-level region says "Loading…" / "Loaded" once for the whole screen, so
 * twelve panels do not make twelve announcements. Outside the shell (sign-in check) pass `announce`.
 */
export function LoadingState({ label = 'Loading…', announce = false }: { label?: string; announce?: boolean }) {
  return (
    <Box
      role="status"
      aria-live={announce ? 'polite' : 'off'}
      sx={{ display: 'grid', placeItems: 'center', py: 6, gap: 1 }}
    >
      <CircularProgress size={28} aria-hidden />
      {/* The words stay in the page for a reader who moves through it; the spinner alone says nothing. */}
      <Box component="span" sx={visuallyHidden}>
        {label}
      </Box>
    </Box>
  );
}

/** Consistent error message for failed requests. */
export function ErrorState({ error }: { error: unknown }) {
  const message = error instanceof Error ? error.message : 'Something went wrong.';
  return (
    <Alert severity="error" sx={{ my: 2 }}>
      {message}
    </Alert>
  );
}
