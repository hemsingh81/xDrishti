import { Button, Typography } from '@mui/material';
import { Link as RouterLink } from 'react-router';
import { PageHeader } from '@/shared/ui';

export function NotFoundPage() {
  return (
    <>
      <PageHeader title="Page not found" />
      <Typography sx={{ mb: 2 }}>The page you opened does not exist.</Typography>
      <Button component={RouterLink} to="/" variant="contained">
        Go to Today
      </Button>
    </>
  );
}
