// The pure rules of the P8 local summary (spec P8-17): evidence files in, verdicts out. No file, no git and no clock is touched here
// (they are passed in), so every rule has a hand-table test (src/test/p8-evidence.test.ts). The same posture as the server check
// (Domain/Reporting/ReportsPhaseCheck): MET only on evidence that was read, a missing or stale record is NOT EVALUABLE, never MET.

export const MET = 'MET';
export const NOT_MET = 'NOT MET';
export const NOT_EVALUABLE = 'NOT EVALUABLE';

export const BUDGET_BYTES = 200_000;
export const MAX_SECONDS = 120;
export const MAX_INTERACTIONS = 4;
export const MAX_REQUEST_MS = 1000;
export const REQUIRED_TRIALS = 3;
export const COUNTED = [1, 2, 3, 4, 5, 7, 8];
export const READ_ONLY_PREFIXES = [
  '/api/reports',
  '/api/learning',
  '/api/cells',
  '/api/plans/notices',
  '/api/changelog',
  '/api/lessons',
  '/api/hypotheses',
  '/api/experiments',
  '/api/exports',
];

const result = (verdict, detail, caveat = null) => ({ verdict, detail, caveat });

/**
 * Whether evidence recorded at `recorded` counts for `head`. `codeChanged(recorded)` says whether backend/ or frontend/ differ
 * between the two (git diff --quiet <recorded> HEAD -- backend frontend). Unknown or malformed commits are stale, and so is any record while backend/ or
 * frontend/ has uncommitted changes (`dirty`, from git status --porcelain).
 */
export const COMMIT_PATTERN = /^[0-9a-f]{7,40}$/;

export function freshness(recorded, head, codeChanged, dirty = false) {
  if (typeof recorded !== 'string' || !COMMIT_PATTERN.test(recorded))
    return { state: 'stale', note: 'no valid commit recorded' };
  if (dirty)
    return {
      state: 'stale',
      note: `recorded at ${short(recorded)}, but backend or frontend has uncommitted changes: commit, then run again`,
    };
  if (recorded === head) return { state: 'current', note: 'recorded at HEAD' };
  const changed = codeChanged(recorded);
  if (changed === null)
    return { state: 'stale', note: `recorded at ${short(recorded)}, which this repository does not know` };
  return changed
    ? { state: 'stale', note: `recorded at ${short(recorded)}, HEAD is ${short(head)}, code changed: run again` }
    : { state: 'accepted', note: `recorded at ${short(recorded)}, no change in backend or frontend since` };
}

const short = (commit) => String(commit).slice(0, 8);

/** Criterion 7: the bundle record written by `npm run size` (XD_EVIDENCE_DIR set). */
export function judgeBundle(record, head, codeChanged, dirty = false) {
  if (record == null)
    return result(
      NOT_EVALUABLE,
      'data/p8-evidence/bundle.json is absent: run scripts/check.sh (it records the size) or XD_EVIDENCE_DIR=data/p8-evidence npm run size',
    );
  const fresh = freshness(record.commit, head, codeChanged, dirty);
  if (fresh.state === 'stale') return result(NOT_EVALUABLE, `bundle record ${fresh.note}`);
  if (!Number.isFinite(record.total) || record.total <= 0 || !Number.isFinite(record.budget))
    return result(NOT_EVALUABLE, 'bundle record has no readable size');
  const kb = (record.total / 1000).toFixed(1);
  if (record.total > BUDGET_BYTES)
    return result(
      NOT_MET,
      `first-load JavaScript ${kb} KB gzip is over the ${BUDGET_BYTES / 1000} KB budget (${fresh.note})`,
    );
  return result(
    MET,
    `first-load JavaScript ${kb} KB gzip, within the ${BUDGET_BYTES / 1000} KB budget (${fresh.note})`,
    'per-route lazy budgets are not measured by npm run size; only the guarded first load is',
  );
}

/**
 * Criterion 8: the lines of browser-checklist.jsonl (the P8-16 format: screen, width, scheme, state, check, result pass|fail|na, note, commit, date).
 * `builtScreens` is the committed list of screens that are built (frontend/scripts/p8-built-screens.json): every one must have at least one line,
 * so a screen the runner skipped is NOT EVALUABLE, never silently absent. Lines of screens not on the list are ignored.
 */
export function judgeChecklist(lines, head, codeChanged, builtScreens, dirty = false) {
  if (!Array.isArray(builtScreens) || builtScreens.length === 0)
    return result(NOT_EVALUABLE, 'no list of built screens (frontend/scripts/p8-built-screens.json)');
  if (!lines || lines.length === 0)
    return result(
      NOT_EVALUABLE,
      'data/p8-evidence/browser-checklist.jsonl is absent or empty: run npm run test:browser (P8-16)',
    );
  const parsed = [];
  for (const [i, line] of lines.entries()) {
    try {
      parsed.push(JSON.parse(line));
    } catch {
      return result(NOT_EVALUABLE, `browser-checklist.jsonl line ${i + 1} is not JSON`);
    }
  }
  const rows = parsed.filter((r) => builtScreens.includes(r.screen));
  const missing = builtScreens.filter((s) => !rows.some((r) => r.screen === s));
  if (missing.length > 0)
    return result(
      NOT_EVALUABLE,
      `no checklist line for ${missing.length} of ${builtScreens.length} built screens: ${missing.join(', ')}`,
    );
  const stale = new Map();
  for (const commit of new Set(rows.map((r) => r.commit))) {
    const fresh = freshness(commit, head, codeChanged, dirty);
    if (fresh.state === 'stale') stale.set(commit, fresh.note);
  }
  if (stale.size > 0) return result(NOT_EVALUABLE, `checklist ${[...stale.values()][0]}`);
  const results = new Set(['pass', 'fail', 'na']);
  const bad = rows.find((r) => !results.has(r.result));
  if (bad)
    return result(
      NOT_EVALUABLE,
      `a checklist line has result '${String(bad.result)}' (${String(bad.screen)} ${String(bad.check)})`,
    );
  const failed = rows.filter((r) => r.result === 'fail');
  if (failed.length > 0)
    return result(
      NOT_MET,
      `${failed.length} failed checks over ${builtScreens.length} built screens, first: ${String(failed[0].screen)} ${String(failed[0].width)} ${String(failed[0].check)}`,
    );
  const unexplained = rows.filter((r) => r.result === 'na' && !(typeof r.note === 'string' && r.note.trim() !== ''));
  if (unexplained.length > 0)
    return result(
      NOT_MET,
      `${unexplained.length} checks marked na without a reason, first: ${String(unexplained[0].screen)} ${String(unexplained[0].check)}`,
    );
  const pass = rows.filter((r) => r.result === 'pass').length;
  return result(
    MET,
    `${builtScreens.length} built screens, ${pass} checks passed, ${rows.length - pass} na with a reason, none failed`,
    'recorded by the runner, not verified here; the owner walkthrough sheet is in the ADR',
  );
}

/** The server's timing rule on the answer path: NOT_MET above 1 s, MET when read and within, null when something is missing. */
export function serverTiming(timings) {
  const path = timings.filter((t) => t.answerPath);
  const values = path.flatMap((t) => [t.coldMs, t.warmMs]);
  if (values.some((v) => v != null && v > MAX_REQUEST_MS)) return NOT_MET;
  if (path.length === 0 || values.some((v) => v == null)) return null;
  return MET;
}

/** Criterion 1 across the three witnesses: the server timing, the interaction test and the owner's stopwatch note (all for this commit). */
export function judgeExitQuestion({ runs, timings, interaction, stopwatch }, head, codeChanged, dirty = false) {
  if (!runs) return result(NOT_EVALUABLE, 'no stored run: the answer path has nothing to time');
  const fails = [];
  const missing = [];
  const server = serverTiming(timings);
  if (server === NOT_MET) fails.push('a request of the answer path took over 1 s');
  if (server === null) missing.push('the server timing of the answer path');

  let interactionOk = null;
  if (interaction == null) missing.push('the interaction test result (data/p8-evidence/interaction.json)');
  else {
    const fresh = freshness(interaction.commit, head, codeChanged, dirty);
    if (fresh.state === 'stale') missing.push(`a current interaction test result (${fresh.note})`);
    else if (interaction.passed === true) interactionOk = true;
    else {
      interactionOk = false;
      fails.push('the interaction test failed');
    }
  }

  let trials = [];
  let date = null;
  if (stopwatch == null)
    missing.push("the owner's stopwatch note (node frontend/scripts/p8-evidence.mjs stopwatch ...)");
  else {
    const fresh = freshness(stopwatch.commit, head, codeChanged, dirty);
    if (fresh.state === 'stale') missing.push(`a current stopwatch note (${fresh.note})`);
    else {
      trials = Array.isArray(stopwatch.trials) ? stopwatch.trials : [];
      date = typeof stopwatch.date === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(stopwatch.date) ? stopwatch.date : null;
      const over = trials.filter((t) => !(t.seconds <= MAX_SECONDS)).length;
      const many = trials.filter((t) => !(t.interactions <= MAX_INTERACTIONS)).length;
      if (over > 0) fails.push(`${over} stopwatch trials over ${MAX_SECONDS} s`);
      if (many > 0) fails.push(`${many} stopwatch trials over ${MAX_INTERACTIONS} interactions`);
      if (fails.length === 0 && (trials.length < REQUIRED_TRIALS || date === null))
        missing.push(`a complete stopwatch note (${REQUIRED_TRIALS} trials and a date)`);
    }
  }

  if (fails.length > 0) return result(NOT_MET, fails.join('; '));
  if (missing.length > 0) return result(NOT_EVALUABLE, `missing: ${missing.join(', ')}`);
  return result(
    MET,
    `answer path within 1 s; interaction test passed; ${trials.length} stopwatch trials each within ${MAX_SECONDS} s and ${MAX_INTERACTIONS} interactions, dated ${date}`,
    "the stopwatch note is the owner's own, recorded and not verified; the interaction test runs on a fixture",
  );
}

/**
 * Criterion 5 from the committed OpenAPI contract, for the host that cannot show a route table: every operation under a guarded prefix must be GET,
 * unless its "METHOD path" is in `approved` (the list in Domain/Reporting/ApprovedReportWrites.cs).
 */
export function judgeReadOnlyContract(contract, approved) {
  const paths = contract?.paths;
  if (!paths) return result(NOT_EVALUABLE, 'the OpenAPI contract has no paths');
  const under = (p) => READ_ONLY_PREFIXES.some((x) => p === x || p.startsWith(`${x}/`));
  const scanned = Object.entries(paths).filter(([p]) => under(p));
  if (scanned.length === 0) return result(NOT_EVALUABLE, 'no path under the guarded prefixes in the contract');
  const methods = ['get', 'put', 'post', 'delete', 'patch', 'head', 'options', 'trace'];
  const writes = scanned
    .flatMap(([p, item]) => methods.filter((m) => m !== 'get' && item[m]).map((m) => `${m.toUpperCase()} ${p}`))
    .filter((w) => !approved.includes(w));
  if (writes.length > 0) return result(NOT_MET, `not read-only: ${writes.join(', ')}`);
  return result(
    MET,
    `${scanned.length} paths under the guarded prefixes, GET only (${approved.length} approved writes), read from the OpenAPI contract`,
    'the contract lists declared operations; the running host judges its real route table (GET /api/reports/phase-check); frontend folders are checked by the no-write test',
  );
}

/** The approved writes written in ApprovedReportWrites.cs: quoted "METHOD /api/..." strings. */
export function approvedWrites(source) {
  return [...source.matchAll(/"((?:POST|PUT|PATCH|DELETE) \/api\/[^"]*)"/g)].map((m) => m[1]);
}

/** The headline rule (same as ReportsPhaseReport.HeadlineOf). */
export function headline(criteria) {
  const numbers = criteria.map((c) => c.number);
  if (new Set(numbers).size !== numbers.length || COUNTED.some((n) => !numbers.includes(n)))
    return 'P8 exit: NOT EVALUABLE, malformed report';
  if (criteria.some((c) => c.verdict === NOT_MET)) return 'P8 exit: NOT MET';
  const open = criteria.filter((c) => COUNTED.includes(c.number) && c.verdict !== MET).length;
  if (open > 0) return `P8 exit: NOT EVALUABLE (${open} of ${COUNTED.length} need evidence)`;
  const caveats = criteria.filter((c) => COUNTED.includes(c.number) && c.caveat).map((c) => `${c.number}: ${c.caveat}`);
  return caveats.length === 0 ? 'P8 exit: MET' : `P8 exit: MET (criterion ${caveats.join('; criterion ')})`;
}

/** Reads the verb's text (xd reports phase-check): criteria with their detail line, the stored run count and the timings. */
export function parseServerText(text) {
  const lines = text.split('\n');
  const criteria = [];
  for (const [i, line] of lines.entries()) {
    const m = /^(\d+)\. (MET|NOT MET|NOT EVALUABLE)(?: \((.*)\))?  (.*)$/.exec(line);
    if (m)
      criteria.push({
        number: Number(m[1]),
        verdict: m[2],
        caveat: m[3] ?? null,
        name: m[4],
        detail: (lines[i + 1] ?? '').trim(),
      });
  }
  const runs = Number(/^stored: (\d+) runs/m.exec(text)?.[1] ?? 0);
  const num = (s) => (s === 'not read' ? null : Number(s.replace(' ms', '')));
  const timings = [
    ...text.matchAll(/^timing (\w+)( \(answer path\))?: cold (not read|\d+ ms), warm (not read|\d+ ms)$/gm),
  ].map((m) => ({
    operation: m[1],
    answerPath: m[2] !== undefined,
    coldMs: num(m[3]),
    warmMs: num(m[4]),
  }));
  return { criteria, runs, timings };
}

/** Merges the server's criteria with the local evidence into the final ten. A server NOT MET stays; local evidence only fills what the server cannot read. */
export function mergeCriteria(server, local) {
  const byNumber = new Map(server.criteria.map((c) => [c.number, c]));
  const out = [];
  for (let n = 1; n <= 10; n += 1) {
    const base = byNumber.get(n);
    const own = local[n];
    if (own && !(base && base.verdict === NOT_MET)) out.push({ number: n, name: base?.name ?? '', ...own });
    else if (base) out.push(base);
  }
  return out;
}

export function render(criteria, head) {
  const lines = ['phase check P8, local summary (server check + data/p8-evidence + OpenAPI contract; spec P8-17)'];
  for (const c of criteria) {
    lines.push(`${c.number}. ${c.verdict}${c.verdict === MET && c.caveat ? ` (${c.caveat})` : ''}  ${c.name}`);
    lines.push(`   ${c.detail}`);
  }
  lines.push(head);
  return `${lines.join('\n')}\n`;
}

/** Validates the arguments of the stopwatch recorder; the recorder never invents a trial. Returns { error } or { trials, date, ... }. */
export function parseStopwatch(args) {
  const list = (s) => (typeof s === 'string' && s !== '' ? s.split(',').map((x) => x.trim()) : []);
  const seconds = list(args.seconds).map(Number);
  const interactions = list(args.interactions).map(Number);
  if (seconds.length === 0) return { error: '--seconds is required, for example 84,97,71' };
  if (seconds.length !== interactions.length)
    return { error: '--seconds and --interactions need the same number of trials' };
  if (seconds.some((s) => !Number.isFinite(s) || s <= 0)) return { error: '--seconds holds positive numbers' };
  if (interactions.some((n) => !Number.isInteger(n) || n < 0)) return { error: '--interactions holds whole numbers' };
  if (!/^\d{4}-\d{2}-\d{2}$/.test(args.date ?? '')) return { error: '--date is YYYY-MM-DD' };
  if (!['RANKED', 'NOTHING'].includes(args.answer)) return { error: '--answer is RANKED or NOTHING' };
  if (typeof args.basket !== 'string' || args.basket.trim() === '')
    return { error: '--basket names the basket you chose' };
  return {
    trials: seconds.map((s, i) => ({ seconds: s, interactions: interactions[i] })),
    date: args.date,
    answer: args.answer,
    basket: args.basket.trim(),
    note: typeof args.note === 'string' ? args.note : '',
  };
}
