import BiotechIcon from '@mui/icons-material/BiotechOutlined';
import { Alert, Box, Button, Card, CardContent, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useCallback, useState } from 'react';
import { useSearchParams } from 'react-router';
import { ErrorState, LoadingState, PageHeader } from '@/shared/ui';
import { simDaysQuery } from '../../api/simDaysApi';
import { SimulationTabs } from '../SimulationTabs';
import { SimDayHistory } from './SimDayHistory';
import { SimDayPlayer } from './SimDayPlayer';
import { SimDaysControls } from './SimDaysControls';
import { SimDaysHeader } from './SimDaysHeader';
import { SimDaysStartForm } from './SimDaysStartForm';

/** The last simulation opened here: there is no list of runs, so the screen remembers its own. */
export const lastSimKey = 'xd.simDays.last';

const remembered = () => {
  try {
    return localStorage.getItem(lastSimKey);
  } catch {
    return null;
  }
};

/** The day-by-day simulation (SR-04): one trading day at a time, played on a clock, settled, advanced. */
export function SimDaysPage() {
  const [params, setParams] = useSearchParams();
  const id = params.get('sim') ?? remembered();
  const [startOpen, setStartOpen] = useState(false);
  const open = (next: string) => {
    localStorage.setItem(lastSimKey, next);
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
          <StartCard onStarted={open} />
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
      <SimDaysHeader run={sim} equity={settled && boardEquity !== null ? boardEquity : sim.equity} />
      <SimDaysControls
        run={sim}
        onSettle={(how) => {
          setShow({ day: sim.cursor, atEnd: how === 'end' });
        }}
        onInstantSettled={() => {
          setEndSignal((n) => n + 1);
        }}
      />
      {sim.phase === 'settling' && <LoadingState label={`Settling ${sim.cursor}…`} />}
      {sim.phase === 'complete' && (
        <Alert severity="success">
          The simulation is complete. Continue extends it when newer sessions are stored.
        </Alert>
      )}
      {sim.phase === 'cancelled' && <Alert severity="info">The simulation was cancelled.</Alert>}
      {settled && (
        <SimDayPlayer
          key={`${sim.cursor}-${String(atEnd)}`}
          run={sim}
          startAtEnd={atEnd}
          endSignal={endSignal}
          onEquity={onEquity}
        />
      )}
      <SimDayHistory run={sim} />
      <Box>
        <Button variant="outlined" onClick={onNew} sx={{ minHeight: 44 }}>
          New simulation
        </Button>
      </Box>
    </>
  );
}
