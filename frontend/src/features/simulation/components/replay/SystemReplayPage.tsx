import BiotechIcon from '@mui/icons-material/BiotechOutlined';
import { Alert, Box, Card, CardContent, MenuItem, TextField, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useSearchParams } from 'react-router';
import { formatDate } from '@/shared/lib/format';
import { ErrorState, LoadingState, PageHeader } from '@/shared/ui';
import { replayRunsQuery } from '../../api/replayApi';
import { SimulationTabs } from '../SimulationTabs';
import { ReplayResults } from './ReplayResults';
import { ReplayStartForm } from './ReplayStartForm';

/** The whole-system replay (SR-01 part E): start one, follow it, read it. A research tool, fenced from evidence. */
export function SystemReplayPage() {
  const runs = useQuery(replayRunsQuery);
  const [params, setParams] = useSearchParams();
  const chosen = params.get('run');
  const list = runs.data ?? [];
  const run = list.find((r) => r.id === chosen) ?? list[0];
  return (
    <>
      <PageHeader
        title="Simulation"
        icon={BiotechIcon}
        hue="reports"
        description="Replay the whole system from a chosen date, in memory."
      />
      <SimulationTabs current="/simulation/replay" />
      <Box sx={{ display: 'grid', gap: 2, minWidth: 0 }}>
        <Card component="section" aria-labelledby="replay-start-title">
          <CardContent>
            <Typography id="replay-start-title" variant="h2" component="h2" sx={{ mb: 1.5 }}>
              Start a replay
            </Typography>
            <ReplayStartForm
              onStarted={(id) => {
                setParams({ run: id });
              }}
            />
          </CardContent>
        </Card>
        {runs.isPending && <LoadingState label="Loading the replays…" />}
        {runs.isError && <ErrorState error={runs.error} />}
        {runs.data && !run && <Alert severity="info">No replay yet. Start one above.</Alert>}
        {run && (
          <>
            <TextField
              select
              size="small"
              label="Replay"
              value={run.id}
              onChange={(e) => {
                setParams({ run: e.target.value });
              }}
            >
              {list.map((r) => (
                <MenuItem key={r.id} value={r.id} sx={{ minHeight: 44 }}>
                  {`${formatDate(r.from)} to ${formatDate(r.to)} · ${r.mode} · ${r.state}`}
                </MenuItem>
              ))}
            </TextField>
            <ReplayResults
              key={run.id}
              runId={run.id}
              bothModes={run.pairId !== null && list.some((r) => r.pairId === run.pairId && r.id !== run.id)}
            />
          </>
        )}
      </Box>
    </>
  );
}
