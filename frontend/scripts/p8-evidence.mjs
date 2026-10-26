// P8 evidence recorder and local summary (spec P8-17). Subcommands:
//   bundle                                 judge data/p8-evidence/bundle.json (written by `npm run size` when XD_EVIDENCE_DIR is set)
//   stopwatch --seconds 84,97,71 --interactions 3,3,3 --basket NAME --answer RANKED|NOTHING --date YYYY-MM-DD [--note ".."]
//                                          record the OWNER's stopwatch trials for this commit; nothing is invented or sent anywhere
//   interaction --from vitest.json         record the result of the interaction-budget test (vitest --reporter=json) for this commit
//   checklist [FILE]                       fold the browser runner's lines (browser.jsonl) and write browser-checklist-summary.md (scripts/p8Checklist.mjs, P8-16)
//   summary --server-text FILE             merge the verb's text (xd reports phase-check) with the local evidence, print the ten criteria
// Evidence lives in $XD_EVIDENCE_DIR (default <repo>/data/p8-evidence, git-ignored). Files are recorded, never verified.
import { spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  headline,
  judgeBundle,
  judgeChecklist,
  judgeExitQuestion,
  judgeReadOnlyContract,
  approvedWrites,
  mergeCriteria,
  parseServerText,
  parseStopwatch,
  render,
  NOT_EVALUABLE,
  COMMIT_PATTERN,
} from './p8EvidenceCore.mjs';

const root = resolve(fileURLToPath(new URL('../..', import.meta.url)));
const dir = process.env.XD_EVIDENCE_DIR ? resolve(process.env.XD_EVIDENCE_DIR) : join(root, 'data', 'p8-evidence');

const git = (...args) => spawnSync('git', ['-C', root, ...args], { encoding: 'utf8' });
const head = () => git('rev-parse', 'HEAD').stdout.trim();
const codeChanged = (recorded) => {
  if (!COMMIT_PATTERN.test(recorded)) return null;
  const known = git('cat-file', '-e', `${recorded}^{commit}`);
  if (known.status !== 0) return null;
  return git('diff', '--quiet', recorded, 'HEAD', '--', 'backend', 'frontend').status !== 0;
};
// Uncommitted changes in backend/ or frontend/ make every record stale: the record was taken on code that is not what HEAD holds.
const dirty = () => git('status', '--porcelain', '--', 'backend', 'frontend').stdout.trim() !== '';
const readJson = (name) => {
  const path = join(dir, name);
  if (!existsSync(path)) return null;
  try {
    return JSON.parse(readFileSync(path, 'utf8'));
  } catch {
    return null;
  }
};
const readLines = (name) => {
  const path = join(dir, name);
  return existsSync(path)
    ? readFileSync(path, 'utf8')
        .split('\n')
        .filter((l) => l.trim() !== '')
    : [];
};
const flags = (argv) => {
  const out = {};
  for (let i = 0; i < argv.length; i += 2) out[String(argv[i]).replace(/^--/, '')] = argv[i + 1];
  return out;
};
const write = (name, value) => {
  mkdirSync(dir, { recursive: true });
  writeFileSync(join(dir, name), `${JSON.stringify(value, null, 2)}\n`);
};

const [command, ...rest] = process.argv.slice(2);
const args = flags(rest);

if (command === 'checklist') {
  const run = spawnSync(process.execPath, [fileURLToPath(new URL('./p8Checklist.mjs', import.meta.url)), ...rest], {
    stdio: 'inherit',
    env: { ...process.env, XD_EVIDENCE_DIR: dir },
  });
  process.exit(run.status ?? 1);
} else if (command === 'bundle') {
  const r = judgeBundle(readJson('bundle.json'), head(), codeChanged, dirty());
  console.log(`${r.verdict}  ${r.detail}`);
} else if (command === 'stopwatch') {
  const parsed = parseStopwatch(args);
  if (parsed.error) {
    console.error(`stopwatch: ${parsed.error}`);
    process.exit(2);
  }
  const record = { commit: head(), recordedBy: 'owner', ...parsed };
  write('stopwatch.json', record);
  console.log(JSON.stringify(record, null, 2));
  console.log(`recorded in ${join(dir, 'stopwatch.json')} (local only; copy the numbers into the ADR)`);
  if (parsed.trials.length < 3) console.log('note: three trials are needed before criterion 1 can be MET');
} else if (command === 'interaction') {
  const report = args.from && existsSync(args.from) ? JSON.parse(readFileSync(args.from, 'utf8')) : null;
  if (!report || typeof report.numFailedTests !== 'number') {
    console.error('interaction: --from needs the JSON of `vitest run src/features/reports/phase --reporter=json`');
    process.exit(2);
  }
  const record = {
    commit: head(),
    date: new Date().toISOString().slice(0, 10),
    // a run that executed fewer than 3 tests is not the interaction-budget run (that file has 5), so it can never stamp a pass
    passed: report.numFailedTests === 0 && report.numPassedTests >= 3 && report.numTotalTests >= 3,
    tests: report.numPassedTests,
    failed: report.numFailedTests,
  };
  write('interaction.json', record);
  console.log(JSON.stringify(record));
} else if (command === 'summary') {
  const text = args['server-text'] && existsSync(args['server-text']) ? readFileSync(args['server-text'], 'utf8') : '';
  const server = parseServerText(text);
  const h = head();
  const isDirty = dirty();
  const local = {};
  local[1] = judgeExitQuestion(
    {
      runs: server.runs,
      timings: server.timings,
      interaction: readJson('interaction.json'),
      stopwatch: readJson('stopwatch.json'),
    },
    h,
    codeChanged,
    isDirty,
  );
  local[7] = judgeBundle(readJson('bundle.json'), h, codeChanged, isDirty);
  const builtPath = join(root, 'frontend', 'scripts', 'p8-built-screens.json');
  const built = existsSync(builtPath) ? JSON.parse(readFileSync(builtPath, 'utf8')) : null;
  local[8] = judgeChecklist(readLines('browser.jsonl'), h, codeChanged, built?.built ?? null, isDirty);
  const serverFive = server.criteria.find((c) => c.number === 5);
  if (serverFive?.verdict === NOT_EVALUABLE) {
    const contract = JSON.parse(readFileSync(join(root, 'backend', 'contracts', 'XDrishti.Api.json'), 'utf8'));
    const approved = approvedWrites(
      readFileSync(join(root, 'backend', 'src', 'XDrishti.Domain', 'Reporting', 'ApprovedReportWrites.cs'), 'utf8'),
    );
    local[5] = judgeReadOnlyContract(contract, approved);
  }
  const names = {
    1: 'The exit question in under two minutes',
    7: 'Bundle',
    8: 'Responsive and accessible',
    5: 'Read-only P8',
  };
  const merged = mergeCriteria(server, local).map((c) => ({ ...c, name: c.name || names[c.number] || '' }));
  process.stdout.write(render(merged, headline(merged)));
} else {
  console.error(
    'usage: p8-evidence.mjs bundle | stopwatch --seconds .. --interactions .. --basket .. --answer .. --date .. | interaction --from FILE | checklist [FILE] | summary --server-text FILE',
  );
  process.exit(2);
}
