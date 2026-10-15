// Runs the whole suite once per shifted clock (XD_TEST_NOW, see src/test/pinClock.ts). A test that fails at one of the
// instants depends on the real clock: pin it with `pinClock`. Pass instants as arguments to run just those.
import { spawnSync } from 'node:child_process';

const defaults = [
  '2026-06-01T06:00:00Z', // before the dates the fixtures use: nothing may need "today" to be later than them
  '2026-10-13T05:30:00Z', // 11:00 IST on a Tuesday: the market is open
  '2026-10-14T00:30:00Z', // 06:00 IST, early morning
  '2026-10-14T13:00:00Z', // 18:30 IST, after the close
  '2026-10-14T19:00:00Z', // 00:30 IST on 15 Oct, still 14 Oct in UTC: IST and UTC on different days
  '2026-10-16T09:00:00Z', // two days on: day thresholds ("yesterday", "N days ago")
  '2026-12-31T23:30:00Z', // 05:00 IST on 1 Jan, 23:30 on 31 Dec in UTC: year boundary
  '2027-03-01T05:00:00Z', // months later: month thresholds
  '2027-12-01T06:00:00Z', // after the old expiry of the fixtures' API key (9 Oct 2027): the key must not expire in any test
  '2030-01-15T06:00:00Z', // years on: nothing may depend on the year
];
const instants = process.argv.length > 2 ? process.argv.slice(2) : defaults;

const failed = [];
for (const instant of instants) {
  console.log(`\n=== XD_TEST_NOW=${instant} ===`);
  const run = spawnSync('npx', ['vitest', 'run'], {
    stdio: 'inherit',
    env: { ...process.env, XD_TEST_NOW: instant },
  });
  if (run.status !== 0) failed.push(instant);
}

console.log(
  failed.length === 0 ? `\nClock-independent at all ${instants.length} instants.` : `\nFailed at: ${failed.join(', ')}`,
);
process.exit(failed.length === 0 ? 0 : 1);
