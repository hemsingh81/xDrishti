import { Alert, Button, Card, CardContent, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { LoadingState } from '@/shared/ui';
import { thresholdsQuery } from '../api/thresholdsApi';
import { problemText } from '../lib/problems';
import { ThresholdsForm } from './ThresholdsForm';

/**
 * Settings: when the Portfolio screen's Action center flags a holding. Ten numbers with their ranges; the Action center
 * uses a saved change on its next read. "Restore defaults" writes the documented defaults.
 */
export function ActionThresholdsCard() {
  const query = useQuery(thresholdsQuery);
  return (
    <Card component="section" aria-labelledby="action-thresholds-title">
      <CardContent>
        <Stack spacing={0.5} sx={{ mb: 2 }}>
          <Typography id="action-thresholds-title" variant="h2" component="h2">
            Action center thresholds
          </Typography>
          <Typography color="text.secondary">
            When the Action center on the Portfolio screen flags a holding. Percentages are of cost unless said
            otherwise.
          </Typography>
        </Stack>
        {query.isPending && <LoadingState label="Loading thresholds" />}
        {query.isError && (
          <Alert
            severity="error"
            action={
              <Button
                color="inherit"
                size="small"
                onClick={() => {
                  void query.refetch();
                }}
              >
                Retry
              </Button>
            }
          >
            {problemText(query.error, 'The thresholds could not be loaded.')}
          </Alert>
        )}
        {query.data && <ThresholdsForm key={query.data.updatedAt ?? 'defaults'} view={query.data} />}
      </CardContent>
    </Card>
  );
}
