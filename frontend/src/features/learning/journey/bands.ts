import type { Lesson, Model } from '../api/learningQueries';

/** A time axis from `start` to `end` (epoch milliseconds); a position is a fraction 0 to 1 of it. */
export interface Axis {
  start: number;
  end: number;
}

export interface LessonBand {
  id: number;
  label: string;
  /** Where the band starts and ends, 0 to 1 on the axis. */
  from: number;
  to: number;
  /** Open or linked lessons run to today and are labelled "open". */
  open: boolean;
  /** `improved` / `no measurable change`, or null while no result exists. */
  result: string | null;
  startedAt: string;
  endedAt: string | null;
}

export interface ModelPin {
  id: string;
  label: string;
  at: number;
  when: string;
}

/** A designation row of the changelog as far as the pins need it. */
export interface Designation {
  id: number;
  subject: string;
  toState: string;
  at: string;
}

const resultWords: Record<string, string> = { improved: 'improved', no_measurable_change: 'no measurable change' };

const ms = (iso: string): number => Date.parse(iso);

/** The axis that holds every lesson, model and designation up to `today`; null when there is nothing to place. */
export function axisOf(
  lessons: readonly Lesson[],
  models: readonly Model[],
  designations: readonly Designation[],
  today: number,
): Axis | null {
  const times = [
    ...lessons.map((l) => ms(l.createdAt)),
    ...models.map((m) => ms(m.createdAt)),
    ...designations.map((d) => ms(d.at)),
  ].filter(Number.isFinite);
  if (times.length === 0) return null;
  const start = Math.min(...times);
  return { start, end: Math.max(today, start + 1) };
}

const place = (axis: Axis, time: number): number =>
  Math.min(1, Math.max(0, (time - axis.start) / (axis.end - axis.start)));

/** A lesson as a band: from when it was opened to its last move (closed or dismissed) or to today (open or linked). */
export function lessonBand(lesson: Lesson, axis: Axis, today: number): LessonBand {
  const open = lesson.state === 'open' || lesson.state === 'linked';
  const end = open ? today : ms(lesson.updatedAt);
  return {
    id: lesson.id,
    label: `Lesson #${lesson.id}: ${lesson.subject}`,
    from: place(axis, ms(lesson.createdAt)),
    to: place(axis, end),
    open,
    result: lesson.result === null ? null : (resultWords[lesson.result] ?? lesson.result),
    startedAt: lesson.createdAt,
    endedAt: open ? null : lesson.updatedAt,
  };
}

/** A model registration, and a champion designation, as pins with the id and the first 8 hex of the hash. */
export function modelPins(models: readonly Model[], designations: readonly Designation[], axis: Axis): ModelPin[] {
  const short = new Map(models.map((m) => [m.id, m.sha256.slice(0, 8)]));
  const registered = models.map((m) => ({
    id: `model-${m.id}`,
    label: `Model #${m.id} registered (${m.sha256.slice(0, 8)})`,
    at: place(axis, ms(m.createdAt)),
    when: m.createdAt,
    time: ms(m.createdAt),
  }));
  const designated = designations
    .filter((d) => d.toState === 'champion' && d.subject.startsWith('model:'))
    .map((d) => {
      const id = Number(d.subject.slice('model:'.length));
      return {
        id: `designation-${d.id}`,
        label: `Model #${id} designated champion (${short.get(id) ?? 'hash not listed'})`,
        at: place(axis, ms(d.at)),
        when: d.at,
        time: ms(d.at),
      };
    });
  return [...registered, ...designated]
    .sort((a, b) => a.time - b.time)
    .map((pin) => ({ id: pin.id, label: pin.label, at: pin.at, when: pin.when }));
}
