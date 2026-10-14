import type { LiveFeedStatus } from '@/shared/realtime';
import { istClockParts } from '@/shared/lib/istClock';
import type { MarketPhase } from './market';

/** The five states of the prices-line chip (P2-10a criterion 7). */
export type ChipKind = 'live' | 'preopen' | 'delayed' | 'quotes' | 'closed';

/** A tick this young is "live"; one older than the second limit is "delayed"; in between it is still live, with its age. */
export const liveTickSeconds = 15;
export const delayedTickSeconds = 60;

/** The feed states in which ticks are expected: it is streaming, or on its way (and the last tick tells how well). */
const streamingStates: ReadonlySet<string> = new Set(['connected', 'connecting', 'reconnecting']);

export interface ChipInput {
  phase: MarketPhase;
  /** The hub is connected and follows the scope (a subscription that was answered and did not fail). */
  following: boolean;
  /** The feed's state as the hub (or, without the hub, the REST status) last gave it. */
  feed: LiveFeedStatus | null;
  /** The moment the chip is for (ms, on the server's clock). */
  now: number;
  /** The price time (ms) of the newest live tick among the shown holdings; null when none yet. */
  newestTickAt: number | null;
  /** Since when the screen follows the scope (ms); used only while no tick has arrived. */
  followingSince: number | null;
}

export interface LiveChip {
  kind: ChipKind;
  /** The chip's text: "Live · 10:35:42 IST", "Delayed · 2 min", "Pre-open", "Quotes", "Closed". */
  label: string;
  /** What a screen reader says for the chip. */
  description: string;
  /** The line beside the chip when it replaces the quotes text (live, delayed); null: the usual prices text stays. */
  detail: string | null;
  /** The hub is wanted (the market is on) but the screen is not following: it reads once a minute instead. */
  fallback: boolean;
}

const seconds = (ms: number): number => Math.max(0, Math.floor(ms / 1000));

/** "12 s", "3 min", "1 h 5 min". */
export function formatAge(ms: number): string {
  const s = seconds(ms);
  if (s < 60) return `${String(s)} s`;
  const minutes = Math.floor(s / 60);
  if (minutes < 60) return `${String(minutes)} min`;
  const rest = minutes % 60;
  return rest === 0
    ? `${String(Math.floor(minutes / 60))} h`
    : `${String(Math.floor(minutes / 60))} h ${String(rest)} min`;
}

export function istSecondText(ms: number): string {
  const { hours, minutes, seconds: s } = istClockParts(ms);
  return `${hours}:${minutes}:${s} IST`;
}

/**
 * The chip from the market's phase, the hub and the feed's state, and the age of the last tick of the shown holdings
 * (the browser's clock alone would say "Live" for a feed that stopped). *Quotes* is what the line said before the feed
 * existed (ADR 0019): the feed is off, paused, refused or not followed.
 */
export function describeChip({ phase, following, feed, now, newestTickAt, followingSince }: ChipInput): LiveChip {
  if (phase === 'closed') {
    return { kind: 'closed', label: 'Closed', description: 'The market is closed', detail: null, fallback: false };
  }
  if (phase === 'preopen') {
    return {
      kind: 'preopen',
      label: 'Pre-open',
      description: 'Pre-open session: prices are indicative until the market opens at 09:15',
      detail: null,
      fallback: false,
    };
  }
  const quotes = (fallback: boolean): LiveChip => ({
    kind: 'quotes',
    label: 'Quotes',
    description: fallback
      ? 'Prices come from the five-minute quotes; live updates are not available and the page reads every minute'
      : 'Prices come from the five-minute quotes; the live feed is not streaming',
    detail: null,
    fallback,
  });
  if (!following) return quotes(true);
  if (feed === null || !streamingStates.has(feed.state)) return quotes(false);

  if (newestTickAt === null) {
    // Streaming but nothing for the shown holdings yet: give it a minute before calling it late.
    const waited = followingSince === null ? 0 : now - followingSince;
    if (waited <= delayedTickSeconds * 1000) return quotes(false);
    return {
      kind: 'delayed',
      label: 'Delayed · no tick yet',
      description: 'The live feed is connected but has sent no price for the shown holdings yet',
      detail: 'No tick for the shown holdings yet · prices are from the last quote or sync',
      fallback: false,
    };
  }
  const age = now - newestTickAt;
  if (age > delayedTickSeconds * 1000) {
    return {
      kind: 'delayed',
      label: `Delayed · ${formatAge(age)}`,
      description: `The live feed is connected but the last tick for the shown holdings is ${formatAge(age)} old`,
      detail: `Last tick ${istSecondText(newestTickAt)} · other prices are from the last quote or sync`,
      fallback: false,
    };
  }
  const recent = age <= liveTickSeconds * 1000;
  return {
    kind: 'live',
    label: recent ? `Live · ${istSecondText(newestTickAt)}` : `Live · last tick ${formatAge(age)} ago`,
    description: `Prices are live: the last tick for the shown holdings was at ${istSecondText(newestTickAt)}`,
    detail: 'Prices move with the exchange feed · totals are read again every 5 min',
    fallback: false,
  };
}
