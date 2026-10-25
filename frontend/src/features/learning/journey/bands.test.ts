import { buildLesson, buildModel } from '@/test/learningFixtures';
import { axisOf, lessonBand, modelPins } from './bands';

const day = (iso: string): number => Date.parse(`${iso}T00:00:00Z`);
const today = day('2026-12-31');

describe('Journey bands and pins', () => {
  const closed = buildLesson({
    id: 3,
    state: 'closed',
    result: 'improved',
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-07-01T00:00:00Z',
  });
  const open = buildLesson({
    id: 4,
    state: 'open',
    createdAt: '2026-07-01T00:00:00Z',
    updatedAt: '2026-08-01T00:00:00Z',
  });
  const model = buildModel({ id: 2, createdAt: '2026-01-01T00:00:00Z' });

  it('puts the axis from the earliest record to today, and has none with nothing to place', () => {
    expect(axisOf([], [], [], today)).toBeNull();
    expect(axisOf([closed, open], [model], [], today)).toEqual({ start: day('2026-01-01'), end: today });
  });

  it('draws a closed lesson to its last move and an open lesson to today, labelled open', () => {
    const axis = axisOf([closed, open], [model], [], today)!;
    const a = lessonBand(closed, axis, today);
    const b = lessonBand(open, axis, today);

    expect(a.from).toBe(0);
    expect(a.to).toBeCloseTo(181 / 364, 3);
    expect(a.open).toBe(false);
    expect(a.result).toBe('improved');
    expect(b.to).toBe(1);
    expect(b.open).toBe(true);
    expect(b.endedAt).toBeNull();
  });

  it('words a lesson that changed nothing as no measurable change', () => {
    const axis = axisOf([closed], [], [], today)!;

    expect(lessonBand({ ...closed, result: 'no_measurable_change' }, axis, today).result).toBe('no measurable change');
  });

  it('pins a registration and a champion designation with the id and the first 8 hex of the hash', () => {
    const axis = axisOf(
      [],
      [model],
      [{ id: 9, subject: 'model:2', toState: 'champion', at: '2026-07-01T00:00:00Z' }],
      today,
    )!;
    const pins = modelPins(
      [model],
      [
        { id: 9, subject: 'model:2', toState: 'champion', at: '2026-07-01T00:00:00Z' },
        { id: 10, subject: 'cell:x', toState: 'champion', at: '2026-07-02T00:00:00Z' },
      ],
      axis,
    );

    expect(pins.map((p) => p.label)).toEqual([
      'Model #2 registered (ffffffff)',
      'Model #2 designated champion (ffffffff)',
    ]);
    expect(pins[0]?.at).toBe(0);
    expect(pins[1]?.at).toBeCloseTo(181 / 364, 3);
  });
});
