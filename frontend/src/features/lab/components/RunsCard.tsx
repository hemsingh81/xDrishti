import { Card, CardContent, CardHeader, Grid, Typography } from '@mui/material';
import { useLabRunEvents } from '../hooks/useLabRunEvents';
import { RunDetail } from './RunDetail';
import { RunStarter } from './RunStarter';
import { RunsList } from './RunsList';

interface RunsCardProps {
  familyId: string | null;
  target: { id: string; version: number } | null;
  runId: string | null;
  onSelectRun: (id: string | null) => void;
}

/** Start a test, follow the runs (live over the hub, polling when it is away) and read one run's verdict and report. */
export function RunsCard({ familyId, target, runId, onSelectRun }: RunsCardProps) {
  const { live } = useLabRunEvents(true);
  const poll = !live;
  return (
    <Card>
      <CardHeader title={<Typography variant="h2">Test runs</Typography>} />
      <CardContent>
        <Grid container spacing={2}>
          <Grid size={{ xs: 12, lg: 4 }}>
            <RunStarter
              target={target}
              onStarted={(run) => {
                onSelectRun(run.id);
              }}
            />
            <Typography variant="h3" component="h3" sx={{ mt: 2 }}>
              {familyId ? `Runs of ${familyId}` : 'All runs'}
            </Typography>
            <RunsList strategyId={familyId} poll={poll} selectedId={runId} onSelect={onSelectRun} />
          </Grid>
          <Grid size={{ xs: 12, lg: 8 }}>
            {runId ? (
              <RunDetail key={runId} runId={runId} poll={poll} />
            ) : (
              <Typography color="text.secondary">
                Run a test or choose a run to see its progress, verdict, gates and report.
              </Typography>
            )}
          </Grid>
        </Grid>
      </CardContent>
    </Card>
  );
}
