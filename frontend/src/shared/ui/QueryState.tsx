import { Alert, Box, CircularProgress } from '@mui/material';
import { visuallyHidden } from '@mui/utils';

/** Consistent loading indicator for data-driven sections. */
export function LoadingState({ label = 'Loading…' }: { label?: string }) {
  return (
    <Box role="status" aria-live="polite" sx={{ display: 'grid', placeItems: 'center', py: 6, gap: 1 }}>
      <CircularProgress size={28} aria-hidden />
      {/* The polite live region announces the text; the spinner alone says nothing. */}
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
