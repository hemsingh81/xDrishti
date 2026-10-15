// A test that asserts relative time ("5 min ago", "just now", "in 3 minutes", "yesterday") reads the clock, so it must pin the
// clock (`pinClock`, `setNow`, `vi.setSystemTime` or fake timers); otherwise it turns red with the time of day or the date
// (ISS-07). Low-noise on purpose: it looks for the words in the file, not for how they are used.
//
// Limit: this guard covers RELATIVE-TIME WORDING only. A test that depends on the clock without such words (a date field that
// refuses the future, a key expiry, a day boundary) cannot be found by reading the source: a check for "a 2026-/2027- date
// literal in a file that does not pin" flags about sixty files (pure functions that take their moment as an argument, fixtures
// of dates in the past, ...), too noisy to enforce. `npm run test:clock` (the whole suite at shifted instants) covers the rest;
// it is manual, about 15 minutes, and not part of `npm run check`.
const sources = import.meta.glob<string>('/src/**/*.test.{ts,tsx}', { query: '?raw', import: 'default', eager: true });

const relativeTime =
  /\bago\b|\bjust now\b|\bin \d+ (?:seconds?|minutes?|min|hours?|days?)\b|\byesterday\b|\btomorrow\b/i;
// Fake timers pin the clock only when they start at a given instant: `vi.useFakeTimers()` or `({ shouldAdvanceTime: true })`
// alone start at the real one, so they need a `now` (`now: …` or the shorthand `{ now }`); `fakeTimers(` (pinClock.ts) starts at the pinned instant.
const pinsTheClock =
  /\bpinClock\(|\bsetNow\(|\bpinNow\(|\bfakeTimers\(|\bvi\.setSystemTime\(|\bvi\.useFakeTimers\(\s*\{[^}]*\bnow\b\s*[:,}]/;

// Files that mention relative time but need no pin, each with the reason.
const exceptions: Record<string, string> = {
  '/src/features/accounts/lib/connection.test.ts':
    'the minutes come from the error value (retryAfterSeconds), not from the clock',
  '/src/features/portfolio/ActionCenter.test.tsx':
    'the days to go come from the fixtures (daysLeft), the screen does not read the clock',
  '/src/features/portfolio/lib/actionCenterLib.test.ts': 'pure wording of the days to go given in the item (daysLeft)',
  '/src/features/portfolio/lib/liveChip.test.ts': 'pure: the moment is an argument (now) of the function under test',
  '/src/features/portfolio/lib/liveProperty.test.ts':
    'pure: "yesterday" is a fixed instant of the generator, no relative text is asserted',
  '/src/features/services/lib/countdown.test.ts': 'pure: the moment is an argument (now) of the function under test',
  '/src/shared/lib/format.test.ts': 'pure: the moment is an argument (now) of the function under test',
  '/src/shared/lib/syncHealth.test.ts': 'pure: the moment is an argument (now) of the function under test',
};

describe('tests do not depend on the real clock', () => {
  const files = Object.entries(sources).filter(([path]) => !(path in exceptions));

  it('finds the test files', () => {
    expect(files.length).toBeGreaterThan(50);
  });

  it('pins the clock in every test file that asserts relative time', () => {
    const unpinned = files
      .filter(([, source]) => relativeTime.test(source) && !pinsTheClock.test(source))
      .map(([path]) => path);

    expect(unpinned, 'Pin the clock with pinClock() from @/test/pinClock (see frontend/CLAUDE.md)').toEqual([]);
  });

  it('lists only exceptions that exist', () => {
    expect(Object.keys(exceptions).filter((path) => !(path in sources))).toEqual([]);
  });
});
