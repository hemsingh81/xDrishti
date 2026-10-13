import type { ServicesTimeline, TimelineEntry } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';

export const minutesPerDay = 1440;

/** "15:50" as minutes after midnight ("24:00" is the end of the day). */
export function minutesOf(time: string): number {
  const [hours, minutes] = time.split(':').map(Number);
  return (hours ?? 0) * 60 + (minutes ?? 0);
}

/** A position on the track, in percent of the day. */
export const percentOf = (time: string): number => (minutesOf(time) / minutesPerDay) * 100;

export interface TimelineMarkItem {
  key: string;
  serviceId: string;
  /** What is printed next to the dot. */
  label: string;
  time: string;
  /** Starts when another service ends. */
  after: boolean;
  lane: number;
  /** The label would run off the right edge: it is written to the left of the dot. */
  flip: boolean;
  /** The sentence for assistive technology and the tooltip. */
  description: string;
}

export interface TimelineBandItem {
  key: string;
  serviceId: string;
  label: string;
  from: string;
  to: string;
  lane: number;
  /** An interval (every N minutes) rather than a window that is up all the way. */
  dashed: boolean;
  description: string;
}

export interface TimelineLayout {
  marks: TimelineMarkItem[];
  bands: TimelineBandItem[];
  markLanes: number;
}

/** How many minutes of the day a label of this text covers on a track of about 900 px (7 px a character). */
const labelMinutes = (text: string): number => Math.ceil(((text.length + 7) * 7 * minutesPerDay) / 900);
const maxMarkLanes = 4;
const flipFrom = 0.72 * minutesPerDay;

const bandDescription = (entry: TimelineEntry, from: string, to: string): string => {
  const span = from === '00:00' && to === '24:00' ? 'all day' : `from ${from} to ${to}`;
  return entry.everyMinutes
    ? `${entry.name} runs every ${formatNumber(entry.everyMinutes)} minutes ${span}`
    : `${entry.name} is up ${span}`;
};

/** Where each mark and band goes: marks in the lowest free row so that their labels do not overlap; bands one row each. */
export function layoutTimeline(timeline: ServicesTimeline): TimelineLayout {
  const names = new Map(timeline.entries.map((e) => [e.serviceId, e.name]));
  const raw = timeline.entries
    .filter((e) => e.kind === 'mark')
    .flatMap((entry) =>
      entry.marks.map((mark) => {
        const after = entry.followsServiceId !== null;
        const followed = names.get(entry.followsServiceId ?? '') ?? 'another service';
        return {
          entry,
          time: mark.time,
          after,
          label: `${after ? '↳ ' : ''}${mark.time} ${entry.name}`,
          description: after
            ? `${entry.name} starts after ${followed} finishes, about ${mark.time}`
            : `${entry.name} starts at ${mark.time}`,
        };
      }),
    )
    .sort((a, b) => minutesOf(a.time) - minutesOf(b.time));

  const laneEnds: number[] = [];
  const marks = raw.map((item, index): TimelineMarkItem => {
    const start = minutesOf(item.time);
    const width = labelMinutes(item.label);
    const flip = start > flipFrom;
    const from = flip ? start - width : start;
    const to = flip ? start : start + width;
    let lane = laneEnds.findIndex((end) => end <= from);
    if (lane === -1) lane = Math.min(laneEnds.length, maxMarkLanes - 1);
    laneEnds[lane] = Math.max(laneEnds[lane] ?? 0, to);
    return {
      key: `${item.entry.serviceId}-${item.time}-${String(index)}`,
      serviceId: item.entry.serviceId,
      label: item.label,
      time: item.time,
      after: item.after,
      lane,
      flip,
      description: item.description,
    };
  });

  const bands = timeline.entries
    .filter(
      (e): e is TimelineEntry & { band: NonNullable<TimelineEntry['band']> } => e.kind === 'band' && e.band !== null,
    )
    .map((entry, lane): TimelineBandItem => ({
      key: `${entry.serviceId}-band`,
      serviceId: entry.serviceId,
      label: entry.name,
      from: entry.band.from,
      to: entry.band.to,
      lane,
      dashed: entry.everyMinutes !== null,
      description: bandDescription(entry, entry.band.from, entry.band.to),
    }));

  return { marks, bands, markLanes: Math.max(1, laneEnds.length) };
}

/** The line next to the card title: "market 09:15–15:30 · review cutoff 16:30", or why there is no session. */
export function timelineSummary(timeline: ServicesTimeline): string {
  const market =
    timeline.marketOpen && timeline.marketClose
      ? `market ${timeline.marketOpen}–${timeline.marketClose}`
      : 'no market session';
  return `${market} · review cutoff ${timeline.reviewCutoff}`;
}
