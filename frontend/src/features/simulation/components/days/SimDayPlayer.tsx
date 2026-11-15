import { useQuery } from '@tanstack/react-query';
import { useEffect } from 'react';
import { ErrorState, LoadingState } from '@/shared/ui';
import { simDayQuery, type SimDays, type SimDaySnapshot } from '../../api/simDaysApi';
import { useSimPlayback } from '../../hooks/useSimPlayback';
import { SimClock } from './SimClock';
import { SimLiveBoard } from './SimLiveBoard';

interface Props {
  run: SimDays;
  startAtEnd: boolean;
  /** Bumped by Instant on a settled day: jump to its end. */
  endSignal: number;
  onEquity: (equity: number | null) => void;
}

/** The settled cursor day played on the clock, with its live board. */
export function SimDayPlayer({ run, ...rest }: Props) {
  const day = useQuery(simDayQuery(run.id, run.cursor, false));
  if (day.isPending) return <LoadingState label="Loading the day…" />;
  if (day.isError) return <ErrorState error={day.error} />;
  if (!day.data) return null;
  return <Player run={run} trades={day.data.trades} snapshot={day.data} {...rest} />;
}

function Player({
  run,
  snapshot,
  trades,
  startAtEnd,
  endSignal,
  onEquity,
}: Props & { snapshot: SimDaySnapshot; trades: SimDaySnapshot['trades'] }) {
  const clock = useSimPlayback({
    id: run.id,
    day: run.cursor,
    trades,
    secondsPerMinute: run.speedSecondsPerMinute > 0 ? run.speedSecondsPerMinute : 10,
    startAtEnd,
  });
  const { toEnd } = clock;
  useEffect(() => {
    if (endSignal > 0) toEnd();
    // toEnd is a new function each render; the signal is what counts
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [endSignal]);
  const equity = clock.view?.board.equity ?? null;
  useEffect(() => {
    onEquity(equity);
  }, [equity, onEquity]);
  return (
    <>
      {clock.error && <ErrorState error={clock.error} />}
      {clock.view ? (
        <SimLiveBoard board={clock.view.board} events={clock.view.events} day={snapshot} />
      ) : (
        !clock.error && <LoadingState label="Loading the tape…" />
      )}
      <SimClock clock={clock} day={run.cursor} />
    </>
  );
}
