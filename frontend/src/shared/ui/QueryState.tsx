import { Alert, Box, CircularProgress } from '@mui/material';

/** Consistent loading indicator for data-driven sections. */
export function LoadingState({ label = 'Loading…' }: { label?: string }) {
  return (
    <Box role="status" aria-live="polite" sx={{ display: 'grid', placeItems: 'center', py: 6, gap: 1 }}>
      <CircularProgress size={28} aria-label={label} />
      {/* The polite live region announces the text; the spinner alone says nothing. */}
      <Box
        component="span"
        sx={{
          position: 'absolute',
          // Pixels, not `1` (which MUI reads as 100 %): the hidden text must not widen the page.
          width: '1px',
          height: '1px',
          overflow: 'hidden',
          clipPath: 'inset(50%)',
          whiteSpace: 'nowrap',
        }}
      >
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
