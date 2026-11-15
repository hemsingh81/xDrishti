import { useQueries } from '@tanstack/react-query';
import { useEffect, useMemo, useState } from 'react';
import type { Schemas } from '@/shared/api';
import { simTapeQuery } from '../api/simDaysApi';
import { boardAt, boardTrades, istIso, marketOpenMs, sessionMinutes } from '../lib/simBoard';

export type Speed = 1 | 2 | 5 | 10 | 'max';
export const speeds: readonly Speed[] = [1, 2, 5, 10, 'max'];
/** Max plays a market minute every 50 ms; pages of 30 minutes keep it under one request a second at 10X and beyond. */
const maxDelayMs = 50;
export const pageMinutes = 30;

/** How long one market minute lasts on screen. */
export const minuteDelayMs = (secondsPerMinute: number, speed: Speed) =>
  speed === 'max' ? maxDelayMs : (secondsPerMinute * 1000) / speed;

interface Range {
  from: string;
  to: string;
  first: number;
  last: number;
}

const range = (day: string, first: number, last: number): Range => {
  const open = marketOpenMs(day);
  return { from: istIso(open + first * 60_000), to: istIso(open + last * 60_000), first, last };
};

/**
 * Plays a settled day on a clock: the minute moves at the speed, the tape is read a page (30 minutes) ahead, and only bars
 * and events at or before the clock minute are given out. The clock waits for a page it has not read yet.
 */
export function useSimPlayback({
  id,
  day,
  trades,
  secondsPerMinute,
  startAtEnd,
}: {
  id: string;
  day: string;
  trades: readonly Schemas['SimDayTrade'][];
  secondsPerMinute: number;
  startAtEnd: boolean;
}) {
  const lastIdx = sessionMinutes - 1;
  const [idx, setIdx] = useState(startAtEnd ? lastIdx : 0);
  const [playing, setPlaying] = useState(!startAtEnd);
  const [speed, setSpeed] = useState<Speed>(1);
  // at the end (Instant), the whole day is one page: one request
  const [whole, setWhole] = useState(startAtEnd);

  const ranges = useMemo(() => {
    if (whole) return [range(day, 0, lastIdx)];
    const upTo = Math.min(Math.floor(idx / pageMinutes) + 1, Math.floor(lastIdx / pageMinutes));
    return Array.from({ length: upTo + 1 }, (_, k) =>
      range(day, k * pageMinutes, Math.min(k * pageMinutes + pageMinutes - 1, lastIdx)),
    );
  }, [whole, idx, day, lastIdx]);
  const pages = useQueries({ queries: ranges.map((r) => simTapeQuery(id, day, r.from, r.to)) });
  const loadedUpTo = ranges.reduce((upTo, r, k) => (pages[k]?.data && upTo === r.first - 1 ? r.last : upTo), -1);
  const error = pages.find((p) => p.isError)?.error ?? null;

  const nextReady = idx + 1 <= loadedUpTo;
  const delay = minuteDelayMs(secondsPerMinute, speed);
  useEffect(() => {
    if (!playing || idx >= lastIdx || !nextReady) return;
    const timer = setTimeout(() => {
      setIdx((now) => Math.min(now + 1, lastIdx));
    }, delay);
    return () => {
      clearTimeout(timer);
    };
  }, [playing, idx, lastIdx, nextReady, delay]);

  const minute = marketOpenMs(day) + idx * 60_000;
  const ready = idx <= loadedUpTo;
  const first = pages[0]?.data;
  let view = null;
  if (ready && first) {
    const loaded = pages.flatMap((p) => (p.data ? [p.data] : []));
    const bars = loaded.flatMap((p) => p.bars).filter((b) => Date.parse(b.at) <= minute);
    const events = loaded.flatMap((p) => p.events).filter((e) => Date.parse(e.at) <= minute);
    const board = boardAt(minute, first.board.equityOpen, boardTrades(trades, first.board.open), bars);
    view = { bars, events, board, costsLabel: first.costsLabel };
  }

  return {
    minute,
    atEnd: idx >= lastIdx,
    playing: playing && idx < lastIdx,
    buffering: playing && idx < lastIdx && !nextReady,
    speed,
    view,
    error,
    pause: () => {
      setPlaying(false);
    },
    resume: () => {
      setPlaying(true);
    },
    setSpeed,
    restart: () => {
      setWhole(false);
      setIdx(0);
      setPlaying(true);
    },
    toEnd: () => {
      setWhole(true);
      setIdx(lastIdx);
      setPlaying(false);
    },
  };
}
