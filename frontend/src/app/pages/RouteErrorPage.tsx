import { Alert, Box, Button } from '@mui/material';
import { isRouteErrorResponse, useRouteError } from 'react-router';

/** Shown when a route fails to load or render — keeps the app usable instead of a blank screen. */
export function RouteErrorPage() {
  const error = useRouteError();
  const message = isRouteErrorResponse(error)
    ? `${error.status} ${error.statusText}`
    : error instanceof Error
      ? error.message
      : 'Unexpected error';
  return (
    <Box sx={{ p: 3 }}>
      <Alert severity="error" sx={{ mb: 2 }}>
        {message}
      </Alert>
      <Button
        variant="outlined"
        onClick={() => {
          window.location.reload();
        }}
      >
        Reload
      </Button>
    </Box>
  );
}
