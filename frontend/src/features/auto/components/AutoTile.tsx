import ArrowForwardIcon from '@mui/icons-material/ArrowForward';
import { Button, Card, CardContent, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { Link as RouterLink } from 'react-router';
import { ErrorState, LoadingState } from '@/shared/ui';
import { autoQuery } from '../api/autoApi';

/** Today tile: whether Auto is on and how the last night went. */
export function AutoTile() {
  const query = useQuery(autoQuery);
  const auto = query.data;
  return (
    <Card component="section" aria-labelledby="auto-tile">
      <CardContent>
        <Stack spacing={1}>
          <Typography id="auto-tile" variant="h2">
            Auto
          </Typography>
          {query.isPending && <LoadingState label="Loading Auto…" />}
          {query.isError && <ErrorState error={query.error} />}
          {auto && (
            <Typography>
              Auto is {auto.on ? 'ON' : 'OFF'}.{' '}
              {auto.night.state === 'failed_closed'
                ? `The last night failed closed: ${auto.night.reason ?? 'no reason recorded'}.`
                : auto.night.state === 'published'
                  ? 'The last night published suggestions.'
                  : 'No night has run yet.'}{' '}
              It only suggests.
            </Typography>
          )}
          <div>
            <Button component={RouterLink} to="/auto" variant="contained" endIcon={<ArrowForwardIcon />}>
              Auto and suggestions
            </Button>
          </div>
        </Stack>
      </CardContent>
    </Card>
  );
}
