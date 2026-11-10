import ArrowForwardIcon from '@mui/icons-material/ArrowForward';
import WbSunnyIcon from '@mui/icons-material/WbSunnyOutlined';
import { Box, Button, Card, CardContent, Stack, Typography } from '@mui/material';
import { lazy, Suspense } from 'react';
import { Link as RouterLink } from 'react-router';
import { AutoTile } from '@/features/auto';
import { LiveTile } from '@/features/live';
import { PlanTile } from '@/features/plans';
import { useSystemStatus } from '@/features/system-status';
import { LoadingState, PageHeader, StatusChip } from '@/shared/ui';

// The results tile loads in its own chunk: the first paint does not carry it.
const ResultsTile = lazy(() => import('./ResultsTile'));

/** Home page. Phase 0: shows that the foundation is running; trading content arrives in later phases. */
export function TodayPage() {
  const { data } = useSystemStatus();

  return (
    <>
      <PageHeader
        title="Today"
        icon={WbSunnyIcon}
        hue="today"
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
      <Box sx={{ mt: 2 }}>
        <PlanTile />
      </Box>
      <Box sx={{ mt: 2 }}>
        <AutoTile />
      </Box>
      <Box sx={{ mt: 2 }}>
        <LiveTile />
      </Box>
      <Box sx={{ mt: 2 }}>
        <Suspense fallback={<LoadingState label="Loading results calendar…" />}>
          <ResultsTile />
        </Suspense>
      </Box>
    </>
  );
}
