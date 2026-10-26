// `npm run test:browser [-- playwright args]` (P8-16): runs Playwright, then the results recorder, and exits with the worse
// of the two statuses, so a crashed or failed run never looks green. `--perf` runs only the throttled timing tests.
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const args = process.argv.slice(2);
const perf = args.includes('--perf');
const playwrightArgs = [...args.filter((a) => a !== '--perf'), ...(perf ? ['--grep', '@perf'] : [])];
const run = (command, argv) => spawnSync(command, argv, { stdio: 'inherit', shell: false });

const playwright = run('npx', ['playwright', 'test', ...playwrightArgs]);
const recorder = run(process.execPath, [fileURLToPath(new URL('./p8Checklist.mjs', import.meta.url))]);
// A signal or a spawn error has no status: treat it as a failure.
const status = (r) => r.status ?? 1;
process.exit(Math.max(status(playwright), status(recorder)));
