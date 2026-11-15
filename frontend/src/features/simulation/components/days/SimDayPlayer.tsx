import { useQuery } from '@tanstack/react-query';
import { useEffect } from 'react';
import { ErrorState, LoadingState } from '@/shared/ui';
import { simDayQuery, simDaysSettingsQuery, type SimDaySnapshot } from '../../api/simDaysApi';
import { useSimPlayback } from '../../hooks/useSimPlayback';
import { SimClock } from './SimClock';
import { SimLiveBoard } from './SimLiveBoard';

interface Props {
  runId: string;
  /** The day played: the settled cursor day, or a stored day chosen in the history. */
  day: string;
  stored: boolean;
  startAtEnd: boolean;
  /** Bumped by Instant on a settled day: jump to its end. */
  endSignal: number;
  onEquity: (equity: number | null) => void;
}

/** A settled or stored day played on the clock, with its live board. */
export function SimDayPlayer(props: Props) {
  const day = useQuery(simDayQuery(props.runId, props.day, props.stored));
  const settings = useQuery(simDaysSettingsQuery);
  if (day.isPending || settings.isPending) return <LoadingState label="Loading the day…" />;
  if (day.isError) return <ErrorState error={day.error} />;
  if (!day.data) return null;
  const speed = settings.data?.speedSecondsPerMinute ?? 10;
  return <Player {...props} snapshot={day.data} secondsPerMinute={speed > 0 ? speed : 10} />;
}

function Player({
  runId,
  day,
  snapshot,
  secondsPerMinute,
  startAtEnd,
  endSignal,
  onEquity,
}: Props & { snapshot: SimDaySnapshot; secondsPerMinute: number }) {
  const clock = useSimPlayback({ id: runId, day, trades: snapshot.trades, secondsPerMinute, startAtEnd });
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
      <SimClock clock={clock} day={day} />
    </>
  );
}
