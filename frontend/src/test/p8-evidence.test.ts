import { describe, expect, it } from 'vitest';
import {
  approvedWrites,
  freshness,
  headline,
  judgeBundle,
  judgeChecklist,
  judgeExitQuestion,
  judgeReadOnlyContract,
  mergeCriteria,
  parseServerText,
  parseStopwatch,
} from '../../scripts/p8EvidenceCore.mjs';

// Spec P8-17: the local summary never turns a missing, stale or unread record into MET, and never invents the owner's stopwatch.
const HEAD = 'bbbbbbbb11111111';
const OLD = 'aaaaaaaa22222222';
const same = () => false;
const changed = () => true;
const unknown = () => null;

const fast = [
  { answerPath: true, coldMs: 300, warmMs: 100 },
  { answerPath: true, coldMs: 20, warmMs: 10 },
];
const interaction = { commit: HEAD, passed: true };
const trials = [
  { seconds: 84, interactions: 3 },
  { seconds: 97, interactions: 3 },
  { seconds: 71, interactions: 4 },
];
const stopwatch = { commit: HEAD, date: '2026-10-30', trials };

describe('freshness', () => {
  it('accepts HEAD, accepts an earlier commit with no code change, and refuses the rest', () => {
    expect(freshness(HEAD, HEAD, changed).state).toBe('current');
    expect(freshness(OLD, HEAD, same).state).toBe('accepted');
    expect(freshness(OLD, HEAD, changed)).toEqual({
      state: 'stale',
      note: 'recorded at aaaaaaaa, HEAD is bbbbbbbb, code changed: run again',
    });
    expect(freshness(OLD, HEAD, unknown).state).toBe('stale');
    expect(freshness(undefined, HEAD, same).state).toBe('stale');
    expect(freshness('--output=x', HEAD, same).state).toBe('stale');
    expect(freshness(HEAD, HEAD, same, true).state).toBe('stale');
  });
});

describe('bundle (criterion 7)', () => {
  const record = { commit: HEAD, total: 196_900, budget: 200_000 };
  it('is MET only for a current record within 200 KB, with its caveat', () => {
    expect(judgeBundle(record, HEAD, changed).verdict).toBe('MET');
    expect(judgeBundle(record, HEAD, changed).caveat).toContain('lazy budgets');
    expect(judgeBundle({ ...record, total: 200_001 }, HEAD, changed).verdict).toBe('NOT MET');
  });
  it('is NOT EVALUABLE when absent, stale or unreadable', () => {
    expect(judgeBundle(null, HEAD, changed).verdict).toBe('NOT EVALUABLE');
    expect(judgeBundle({ ...record, commit: OLD }, HEAD, changed).verdict).toBe('NOT EVALUABLE');
    expect(judgeBundle({ ...record, commit: OLD }, HEAD, same).verdict).toBe('MET');
    expect(judgeBundle({ commit: HEAD }, HEAD, changed).verdict).toBe('NOT EVALUABLE');
    expect(judgeBundle({ ...record, total: 0 }, HEAD, changed).verdict).toBe('NOT EVALUABLE');
    expect(judgeBundle(record, HEAD, changed, true).verdict).toBe('NOT EVALUABLE');
  });
});

describe('browser checklist (criterion 8)', () => {
  const built = ['reports', 'learning'];
  // The P8-16 line format: screen, width, scheme, state, check, result pass|fail|na, note, commit, date.
  const line = (over: object = {}) =>
    JSON.stringify({
      screen: 'reports',
      width: 360,
      scheme: 'light',
      state: 'full',
      check: 'no-hscroll',
      result: 'pass',
      note: '',
      commit: HEAD,
      date: '2026-10-30',
      ...over,
    });
  const both = [line(), line({ screen: 'learning' })];
  const judge = (lines: string[], dirty = false, screens: string[] | null = built) =>
    judgeChecklist(lines, HEAD, changed, screens, dirty);
  it('is MET with passes and explained na over every built screen, and says how many', () => {
    const met = judge([...both, line({ result: 'na', note: 'no chart here' })]);
    expect(met.verdict).toBe('MET');
    expect(met.detail).toContain('2 built screens');
  });
  it('is NOT MET with a fail or an unexplained na', () => {
    expect(judge([...both, line({ result: 'fail' })]).verdict).toBe('NOT MET');
    expect(judge([...both, line({ result: 'na', note: ' ' })]).verdict).toBe('NOT MET');
  });
  it('is NOT EVALUABLE when a built screen has no line, whatever the other lines say', () => {
    const missing = judge([line(), line({ screen: 'other', result: 'pass' })]);
    expect(missing.verdict).toBe('NOT EVALUABLE');
    expect(missing.detail).toContain('1 of 2 built screens: learning');
    expect(judge(both, false, null).verdict).toBe('NOT EVALUABLE');
    expect(judge(both, false, []).verdict).toBe('NOT EVALUABLE');
  });
  it('counts a runner row named with its slice id plus the tab (P8-02-overview) for the slice P8-02', () => {
    const rows = [line({ screen: 'reports-overview' }), line({ screen: 'learning-setups' })];
    expect(judge(rows).verdict).toBe('MET');
    expect(judge([line({ screen: 'reportsx' }), line({ screen: 'learning' })]).verdict).toBe('NOT EVALUABLE');
  });
  it('ignores the lines of a screen that is not built, even a failed one', () => {
    expect(judge([...both, line({ screen: 'drilldown', result: 'fail' })]).verdict).toBe('MET');
  });
  it('is NOT EVALUABLE when absent, stale, dirty, malformed or with a bad result', () => {
    expect(judge([]).verdict).toBe('NOT EVALUABLE');
    expect(judge([line({ commit: OLD }), line({ screen: 'learning', commit: OLD })]).verdict).toBe('NOT EVALUABLE');
    expect(judge(both, true).verdict).toBe('NOT EVALUABLE');
    expect(judge(['{nope']).verdict).toBe('NOT EVALUABLE');
    expect(judge([...both, line({ result: 'maybe' })]).verdict).toBe('NOT EVALUABLE');
    expect(judge([line({ commit: '--output=x' }), line({ screen: 'learning' })]).verdict).toBe('NOT EVALUABLE');
  });
  it('accepts an earlier commit when the code did not change', () => {
    const old = [line({ commit: OLD }), line({ screen: 'learning', commit: OLD })];
    expect(judgeChecklist(old, HEAD, same, built).verdict).toBe('MET');
  });
  it('reads a sample line as the P8-16 runner writes it', () => {
    const sample =
      '{"screen":"reports","width":1440,"scheme":"dark","state":"thin","check":"C7-contrast","result":"na","note":"no text on this state","commit":"bbbbbbbb11111111","date":"2026-10-30"}';
    expect(judgeChecklist([sample], HEAD, changed, ['reports']).verdict).toBe('MET');
  });
});

describe('the exit question (criterion 1)', () => {
  const run = (over: Record<string, unknown> = {}) =>
    judgeExitQuestion({ runs: 3, timings: fast, interaction, stopwatch, ...over }, HEAD, changed).verdict;
  it('is MET only with all three witnesses', () => {
    expect(run()).toBe('MET');
    expect(judgeExitQuestion({ runs: 3, timings: fast, interaction, stopwatch }, HEAD, changed).caveat).toContain(
      "owner's own",
    );
  });
  it('is NOT EVALUABLE for any gap', () => {
    expect(run({ stopwatch: null })).toBe('NOT EVALUABLE');
    expect(run({ interaction: null })).toBe('NOT EVALUABLE');
    expect(run({ timings: [{ answerPath: true, coldMs: null, warmMs: null }] })).toBe('NOT EVALUABLE');
    expect(run({ timings: [], interaction: null, stopwatch: null })).toBe('NOT EVALUABLE');
    expect(run({ runs: 0 })).toBe('NOT EVALUABLE');
    expect(run({ stopwatch: { ...stopwatch, trials: trials.slice(0, 2) } })).toBe('NOT EVALUABLE');
    expect(run({ stopwatch: { ...stopwatch, date: null } })).toBe('NOT EVALUABLE');
    expect(run({ stopwatch: { ...stopwatch, commit: OLD } })).toBe('NOT EVALUABLE');
    expect(run({ interaction: { ...interaction, commit: OLD } })).toBe('NOT EVALUABLE');
  });
  it('is NOT MET for a trial over 120 s, over 4 interactions, a failed test or a slow request', () => {
    const rest = trials.slice(1);
    expect(run({ stopwatch: { ...stopwatch, trials: [{ seconds: 121, interactions: 3 }, ...rest] } })).toBe('NOT MET');
    expect(run({ stopwatch: { ...stopwatch, trials: [{ seconds: 90, interactions: 5 }, ...rest] } })).toBe('NOT MET');
    expect(run({ interaction: { ...interaction, passed: false } })).toBe('NOT MET');
    expect(run({ timings: [{ answerPath: true, coldMs: 1500, warmMs: 100 }] })).toBe('NOT MET');
    expect(run({ timings: [{ answerPath: true, coldMs: 1500, warmMs: null }], stopwatch: null })).toBe('NOT MET');
  });
});

describe('read-only from the contract (criterion 5)', () => {
  const contract = {
    paths: {
      '/api/reports/runs': { get: {} },
      '/api/learning/overview': { get: {} },
      '/api/portfolio/trades': { post: {} },
    },
  };
  it('judges only the guarded prefixes and lists a write', () => {
    expect(judgeReadOnlyContract(contract, []).verdict).toBe('MET');
    const bad = { paths: { ...contract.paths, '/api/reports/export': { get: {}, post: {} } } };
    const judged = judgeReadOnlyContract(bad, []);
    expect(judged.verdict).toBe('NOT MET');
    expect(judged.detail).toContain('POST /api/reports/export');
    expect(judgeReadOnlyContract(bad, ['POST /api/reports/export']).verdict).toBe('MET');
    expect(judgeReadOnlyContract({ paths: { '/api/other': { get: {} } } }, []).verdict).toBe('NOT EVALUABLE');
    expect(judgeReadOnlyContract({}, []).verdict).toBe('NOT EVALUABLE');
  });
  it('reads the approved writes from the C# list', () => {
    expect(approvedWrites('public static IReadOnlyList<string> All { get; } = [];')).toEqual([]);
    expect(approvedWrites('= ["POST /api/experiments", "DELETE /api/lessons/{id}"];')).toEqual([
      'POST /api/experiments',
      'DELETE /api/lessons/{id}',
    ]);
  });
});

describe('headline', () => {
  const all = (v: string) =>
    Array.from({ length: 10 }, (_, i) => ({ number: i + 1, verdict: v, caveat: null as string | null }));
  it('follows the server rule', () => {
    expect(headline(all('MET'))).toBe('P8 exit: MET');
    expect(headline(all('MET').map((c) => (c.number === 6 ? { ...c, verdict: 'NOT MET' } : c)))).toBe(
      'P8 exit: NOT MET',
    );
    expect(
      headline(all('MET').map((c) => (c.number === 1 || c.number === 8 ? { ...c, verdict: 'NOT EVALUABLE' } : c))),
    ).toBe('P8 exit: NOT EVALUABLE (2 of 7 need evidence)');
    expect(headline(all('MET').filter((c) => c.number !== 3))).toBe('P8 exit: NOT EVALUABLE, malformed report');
    expect(headline(all('MET').map((c) => (c.number === 1 ? { ...c, caveat: 'owner stopwatch' } : c)))).toContain(
      'owner stopwatch',
    );
  });
});

describe('the server text and the merge', () => {
  const text = [
    'phase check P8',
    'stored: 3 runs, 300 trades in the default set, 2 baskets with trades',
    '1. NOT EVALUABLE  The exit question in under two minutes',
    '   missing: x',
    '4. MET (the default set was not checked: no stored run)  No double counting',
    '   both probe sets are refused',
    'timing GetReportWhatWorks (answer path): cold 300 ms, warm not read',
    'timing GetReportSummary: cold 5 ms, warm 4 ms',
  ].join('\n');
  it('parses criteria, runs and timings', () => {
    const parsed = parseServerText(text);
    expect(parsed.runs).toBe(3);
    expect(parsed.criteria.map((c) => [c.number, c.verdict, c.caveat])).toEqual([
      [1, 'NOT EVALUABLE', null],
      [4, 'MET', 'the default set was not checked: no stored run'],
    ]);
    expect(parsed.timings).toEqual([
      { operation: 'GetReportWhatWorks', answerPath: true, coldMs: 300, warmMs: null },
      { operation: 'GetReportSummary', answerPath: false, coldMs: 5, warmMs: 4 },
    ]);
  });
  it('lets local evidence fill a gap but never override a server NOT MET', () => {
    const server = {
      criteria: [
        { number: 1, name: 'a', verdict: 'NOT MET', caveat: null, detail: 'slow' },
        { number: 7, name: 'b', verdict: 'NOT EVALUABLE', caveat: null, detail: 'x' },
      ],
    };
    const met = { verdict: 'MET', detail: 'd', caveat: null };
    expect(mergeCriteria(server, { 1: met, 7: met }).map((c) => c.verdict)).toEqual(['NOT MET', 'MET']);
  });
});

describe('the stopwatch recorder', () => {
  const ok = {
    seconds: '84,97,71',
    interactions: '3,3,3',
    basket: 'MyChoice',
    answer: 'NOTHING',
    date: '2026-10-30',
    note: 'cold',
  };
  it('records what the owner typed and nothing else', () => {
    expect(parseStopwatch(ok)).toEqual({
      trials: [
        { seconds: 84, interactions: 3 },
        { seconds: 97, interactions: 3 },
        { seconds: 71, interactions: 3 },
      ],
      date: '2026-10-30',
      answer: 'NOTHING',
      basket: 'MyChoice',
      note: 'cold',
    });
  });
  it('refuses missing, mismatched or odd input instead of filling it in', () => {
    expect(parseStopwatch({})).toHaveProperty('error');
    expect(parseStopwatch({ ...ok, interactions: '3,3' })).toHaveProperty('error');
    expect(parseStopwatch({ ...ok, seconds: '84,x,71' })).toHaveProperty('error');
    expect(parseStopwatch({ ...ok, date: '30/10/2026' })).toHaveProperty('error');
    expect(parseStopwatch({ ...ok, answer: 'YES' })).toHaveProperty('error');
    expect(parseStopwatch({ ...ok, basket: ' ' })).toHaveProperty('error');
  });
});
