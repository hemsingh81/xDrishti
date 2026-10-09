import ArrowForwardIcon from '@mui/icons-material/ArrowForward';
import { Button, Card, CardContent, Stack, Typography } from '@mui/material';
import { Link as RouterLink } from 'react-router';
import { useSystemStatus } from '@/features/system-status';
import { PageHeader, StatusChip } from '@/shared/ui';

/** Home page. Phase 0: shows that the foundation is running; trading content arrives in later phases. */
export function TodayPage() {
  const { data } = useSystemStatus();

  return (
    <>
      <PageHeader
        title="Today"
        description="Your trading day at a glance — plan, live trades and portfolio arrive from Phase 1 onwards."
      />
      <Card>
        <CardContent>
          <Stack
            direction={{ xs: 'column', sm: 'row' }}
            spacing={2}
            sx={{ alignItems: { sm: 'center' }, justifyContent: 'space-between' }}
          >
            <div>
              <Typography variant="h2">Foundation</Typography>
              <Typography color="text.secondary">
                Frontend, API, database and worker are connected.{' '}
                {data && <StatusChip ok={data.isHealthy} okLabel="All systems healthy" badLabel="Needs attention" />}
              </Typography>
            </div>
            <Button component={RouterLink} to="/system" variant="contained" endIcon={<ArrowForwardIcon />}>
              System status
            </Button>
          </Stack>
        </CardContent>
      </Card>
    </>
  );
}
