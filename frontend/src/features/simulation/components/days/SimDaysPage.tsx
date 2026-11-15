import BiotechIcon from '@mui/icons-material/BiotechOutlined';
import { Alert, Box, Button, Card, CardContent, MenuItem, TextField, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useCallback, useState } from 'react';
import { useSearchParams } from 'react-router';
import { formatDate } from '@/shared/lib/format';
import { ErrorState, LoadingState, PageHeader } from '@/shared/ui';
import { simDaysListQuery, simDaysQuery, type SimDaysItem } from '../../api/simDaysApi';
import { SimulationTabs } from '../SimulationTabs';
import { SimDayHistory } from './SimDayHistory';
import { SimDayPlayer } from './SimDayPlayer';
import { SimDaysControls } from './SimDaysControls';
import { SimDaysHeader } from './SimDaysHeader';
import { SimDaysStartForm } from './SimDaysStartForm';

const runLabel = (r: SimDaysItem) =>
  `${formatDate(r.start)} to ${formatDate(r.end)} · ${r.mode} · ${r.phase}${r.active ? ' (active)' : ''}`;

/** The day-by-day simulation (SR-04): one trading day at a time, played on a clock, settled, advanced. */
export function SimDaysPage() {
  const [params, setParams] = useSearchParams();
  const list = useQuery(simDaysListQuery);
  const [startOpen, setStartOpen] = useState(false);
  const runs = list.data?.items ?? [];
  // a deep link wins; else the active run; else the start form (past runs stay in the picker)
  const id = params.get('sim') ?? runs.find((r) => r.active)?.id ?? null;
  const open = (next: string) => {
    setStartOpen(false);
    setParams({ sim: next });
  };
  return (
    <>
      <PageHeader
        title="Simulation"
        icon={BiotechIcon}
        hue="reports"
        description="Day by day: each day learns only from the days before it, plays on a clock, settles and moves on."
      />
      <SimulationTabs current="/simulation/days" />
      <Box sx={{ display: 'grid', gap: 2, minWidth: 0 }}>
        {list.isPending && !params.get('sim') && <LoadingState label="Loading the simulations…" />}
        {list.isError && <ErrorState error={list.error} />}
        {runs.length > 0 && (
          <TextField
            select
            size="small"
            label="Simulation"
            value={id ?? ''}
            onChange={(e) => {
              open(e.target.value);
            }}
          >
            {id && !runs.some((r) => r.id === id) && <MenuItem value={id}>This simulation</MenuItem>}
            {runs.map((r) => (
              <MenuItem key={r.id} value={r.id} sx={{ minHeight: 44 }}>
                {runLabel(r)}
              </MenuItem>
            ))}
          </TextField>
        )}
        {id && !startOpen ? (
          <SimRun
            key={id}
            id={id}
            onStarted={open}
            onNew={() => {
              setStartOpen(true);
            }}
          />
        ) : (
          (list.data ?? params.get('sim')) && <StartCard onStarted={open} />
        )}
      </Box>
    </>
  );
}

function StartCard({ onStarted }: { onStarted: (id: string) => void }) {
  return (
    <Card component="section" aria-labelledby="sim-days-start-title">
      <CardContent sx={{ display: 'grid', gap: 1.5 }}>
        <Typography id="sim-days-start-title" variant="h2" component="h2">
          New day-by-day simulation
        </Typography>
        <SimDaysStartForm onStarted={onStarted} />
      </CardContent>
    </Card>
  );
}

function SimRun({ id, onNew, onStarted }: { id: string; onNew: () => void; onStarted: (id: string) => void }) {
  const run = useQuery(simDaysQuery(id));
  const [show, setShow] = useState<{ day: string; atEnd: boolean } | null>(null);
  const [past, setPast] = useState<string | null>(null);
  const [endSignal, setEndSignal] = useState(0);
  const [boardEquity, setBoardEquity] = useState<number | null>(null);
  const onEquity = useCallback((equity: number | null) => {
    setBoardEquity(equity);
  }, []);
  if (run.isPending) return <LoadingState label="Loading the simulation…" />;
  if (run.isError) return <ErrorState error={run.error} />;
  if (!run.data)
    return (
      <>
        <Alert severity="info">That simulation is not there any more. Start one below.</Alert>
        <StartCard onStarted={onStarted} />
      </>
    );
  const sim = run.data;
  const settled = sim.phase === 'settled';
  const atEnd = show?.day === sim.cursor ? show.atEnd : false;
  return (
    <>
      <SimDaysHeader run={sim} equity={settled && past === null && boardEquity !== null ? boardEquity : sim.equity} />
      <SimDaysControls
        run={sim}
        onSettle={(how) => {
          setPast(null);
          setShow({ day: sim.cursor, atEnd: how === 'end' });
        }}
        onInstantSettled={() => {
          setPast(null);
          setEndSignal((n) => n + 1);
        }}
      />
      {sim.phase === 'settling' && <LoadingState label={`Settling ${sim.cursor}…`} />}
      {sim.phase === 'complete' && (
        <Alert severity="success">
          {sim.hasNewSessions
            ? 'The simulation is complete. Newer sessions are stored: Continue extends it.'
            : 'The simulation is complete. Continue appears when newer sessions are stored.'}
        </Alert>
      )}
      {sim.phase === 'cancelled' && <Alert severity="info">The simulation was cancelled.</Alert>}
      {past !== null ? (
        <>
          <Alert
            severity="info"
            action={
              <Button
                color="inherit"
                onClick={() => {
                  setPast(null);
                }}
                sx={{ minHeight: 44 }}
              >
                Stop
              </Button>
            }
          >
            {`Playing the stored day ${formatDate(past)}.`}
          </Alert>
          <SimDayPlayer
            key={`past-${past}`}
            runId={sim.id}
            day={past}
            stored
            startAtEnd={false}
            endSignal={0}
            onEquity={onEquity}
          />
        </>
      ) : (
        settled && (
          <SimDayPlayer
            key={`${sim.cursor}-${String(atEnd)}`}
            runId={sim.id}
            day={sim.cursor}
            stored={false}
            startAtEnd={atEnd}
            endSignal={endSignal}
            onEquity={onEquity}
          />
        )
      )}
      <SimDayHistory run={sim} onPlay={setPast} />
      <Box>
        <Button variant="outlined" onClick={onNew} sx={{ minHeight: 44 }}>
          New simulation
        </Button>
      </Box>
    </>
  );
}
