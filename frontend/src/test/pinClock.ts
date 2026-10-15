/**
 * Tests never depend on the real clock. A test that shows or compares a moment (relative time such as "5 min ago", a day
 * boundary, a staleness threshold) pins `Date` to a fixed instant with `pinClock`, and builds its fixtures from that instant.
 * Only `Date` is faked: timers, requests and the network mock keep running as in every other test.
 *
 * Diagnostic: `XD_TEST_NOW=<ISO instant> npm test` (see `npm run test:clock`) shifts the clock of every test that does not
 * pin it itself; a test that fails at one of those instants depends on the real clock and must call `pinClock`.
 */

let pinnedIso: string | undefined;

/** Pins `Date` to the instant for the test now running; undo with `unpinClock` (done by `pinClock`'s own `afterEach`). */
export function pinNow(iso: string | Date) {
  vi.useFakeTimers({ toFake: ['Date'], now: typeof iso === 'string' ? new Date(iso) : iso });
}

export function unpinClock() {
  vi.useRealTimers();
}

/**
 * Registers `beforeEach`/`afterEach` that pin `Date` to the instant for each test of the file or `describe` it is called in.
 * Returns the instant as a number (ms) so the expected values can be built from it.
 */
export function pinClock(iso: string): number {
  pinnedIso = iso;
  beforeEach(() => {
    pinNow(iso);
  });
  afterEach(() => {
    unpinClock();
  });
  return Date.parse(iso);
}

/**
 * For a test that needs fake timers (to let a pop-up's time run out, to fire a poll) in a file that pins the clock: starts
 * them at the pinned instant, since `vi.useFakeTimers()` alone would start at the real one. Call after `pinClock` in the file.
 */
export function fakeTimers(config: Parameters<typeof vi.useFakeTimers>[0] = {}) {
  if (pinnedIso === undefined) throw new Error('fakeTimers() needs pinClock() in the file first.');
  vi.useFakeTimers({ ...config, now: new Date(pinnedIso) });
}
