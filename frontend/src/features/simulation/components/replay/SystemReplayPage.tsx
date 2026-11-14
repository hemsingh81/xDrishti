import BiotechIcon from '@mui/icons-material/BiotechOutlined';
import { Alert, Box, Button, Card, CardContent, Collapse, MenuItem, TextField, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { useSearchParams } from 'react-router';
import { formatDate } from '@/shared/lib/format';
import { ErrorState, LoadingState, PageHeader } from '@/shared/ui';
import { replayRunsQuery } from '../../api/replayApi';
import { SimulationTabs } from '../SimulationTabs';
import { ReplayResults } from './ReplayResults';
import { ReplayStartForm } from './ReplayStartForm';
import { SweepCard } from './SweepCard';
import { SweepStartForm } from './SweepStartForm';

/** The whole-system replay (SR-01 part E): read one, start one. A research tool, fenced from evidence. */
export function SystemReplayPage() {
  const runs = useQuery(replayRunsQuery);
  const [params, setParams] = useSearchParams();
  // Undecided (undefined) follows the data: open while there is no run, folded away once one is shown.
  const [formOpen, setFormOpen] = useState<boolean>();
  const chosen = params.get('run');
  const list = runs.data ?? [];
  const run = list.find((r) => r.id === chosen) ?? list[0];
  const open = formOpen ?? (runs.data !== undefined && !run);
  const [sweepFormOpen, setSweepFormOpen] = useState(false);
  // A sweep is shown when its id is in the address, or when the chosen replay is one of its starts.
  const sweepId = params.get('sweep') ?? run?.sweepId ?? undefined;
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
        {runs.isPending && <LoadingState label="Loading the replays…" />}
        {runs.isError && <ErrorState error={runs.error} />}
        {runs.data && !run && <Alert severity="info">No replay yet. Start one below.</Alert>}
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
                  {`${formatDate(r.from)} to ${formatDate(r.to)} · ${r.mode}${r.arm === 'baseline' ? ' (baseline)' : ''} · ${r.state}`}
                </MenuItem>
              ))}
            </TextField>
            <ReplayResults key={run.id} runId={run.id} />
          </>
        )}
        {sweepId && <SweepCard key={sweepId} sweepId={sweepId} />}
        <Card component="section" aria-labelledby="replay-start-title">
          <CardContent>
            <Typography id="replay-start-title" variant="h2" component="h2">
              <Button
                onClick={() => {
                  setFormOpen(!open);
                }}
                aria-expanded={open}
                aria-controls="replay-start-panel"
                color="inherit"
                sx={{ font: 'inherit', p: 0, minHeight: 44, textTransform: 'none' }}
              >
                New replay
              </Button>
            </Typography>
            <Collapse in={open} unmountOnExit id="replay-start-panel">
              <Box sx={{ pt: 1.5 }}>
                <ReplayStartForm
                  onStarted={(id) => {
                    setFormOpen(false);
                    setParams({ run: id });
                  }}
                />
              </Box>
            </Collapse>
          </CardContent>
        </Card>
        <Card component="section" aria-labelledby="replay-sweep-start-title">
          <CardContent>
            <Typography id="replay-sweep-start-title" variant="h2" component="h2">
              <Button
                onClick={() => {
                  setSweepFormOpen(!sweepFormOpen);
                }}
                aria-expanded={sweepFormOpen}
                aria-controls="replay-sweep-panel"
                color="inherit"
                sx={{ font: 'inherit', p: 0, minHeight: 44, textTransform: 'none' }}
              >
                New start-date sweep
              </Button>
            </Typography>
            <Collapse in={sweepFormOpen} unmountOnExit id="replay-sweep-panel">
              <Box sx={{ pt: 1.5 }}>
                <SweepStartForm
                  onStarted={(id) => {
                    setSweepFormOpen(false);
                    setParams({ sweep: id });
                  }}
                />
              </Box>
            </Collapse>
          </CardContent>
        </Card>
      </Box>
    </>
  );
}
