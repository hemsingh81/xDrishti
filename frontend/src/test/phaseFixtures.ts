import { http, HttpResponse } from 'msw';
import type { Schemas } from '@/shared/api';
import { server } from './server';

type View = Schemas['PaperPhaseCheckView'];
type Criterion = Schemas['PhaseCriterion'];

const criterion = (
  id: string,
  title: string,
  verdict: string,
  label: string,
  over: Partial<Criterion> = {},
): Criterion => ({
  id,
  title,
  verdict,
  label,
  facts: [],
  blockers: [],
  ...over,
});

/** A report before any epoch: every criterion not evaluable, with the reason and whose act it is. */
export const buildPhase = (over: Partial<Schemas['PaperPhaseReport']> = {}): View => ({
  report: {
    epoch: {
      id: 2,
      startedOn: '2026-11-02',
      kind: 'owner_restart',
      reason: 'start',
      counted: 12,
      unexplainedChanges: 0,
      current: true,
    },
    through: '2026-11-20',
    elapsedDays: 19,
    criteria: [
      criterion('C1', 'Duration and coverage', 'NOT EVALUABLE', 'NOT EVALUABLE: day 19 of 84 (week 3 of 12)', {
        facts: ['19 elapsed days of 84'],
        blockers: ['time must pass; nobody can shorten the 12 weeks'],
      }),
      criterion('C2', 'Counted trades', 'NOT EVALUABLE', 'NOT EVALUABLE: 12 of 60'),
      criterion(
        'C3',
        'Expectancy within the backtest band, after costs',
        'NOT EVALUABLE',
        'NOT EVALUABLE: costs not confirmed',
        {
          blockers: ['costs not confirmed: the owner confirmation of the charge sheet is missing'],
        },
      ),
      criterion(
        'C4',
        'Drawdown within the 90th percentile of the backtest',
        'NOT EVALUABLE',
        'NOT EVALUABLE: costs not confirmed',
      ),
      criterion(
        'I',
        'Capture integrity',
        'MET',
        'MET: no replay disagreed with a stored entry and nothing stays pending',
      ),
    ],
    exitVerdict: 'NOT EVALUABLE',
    exitCriterion: 'NOT EVALUABLE',
    weeks: [
      {
        week: '2026-W45',
        sessions: 5,
        plansPublished: 5,
        failedClosed: 0,
        manualRuns: 1,
        captureFinished: 5,
        tradesCounted: 4,
        cumulativeCounted: 4,
        cumulativeR: 1.5,
        frozenChanges: 0,
        riskDownChanges: 0,
      },
    ],
    epochs: [
      {
        id: 1,
        startedOn: '2026-09-01',
        kind: 'first',
        reason: 'first',
        counted: 61,
        unexplainedChanges: 0,
        current: false,
      },
      {
        id: 2,
        startedOn: '2026-11-02',
        kind: 'owner_restart',
        reason: 'start',
        counted: 12,
        unexplainedChanges: 0,
        current: true,
      },
    ],
    printed: {
      disciplineLines: ['Followed: n 8'],
      sensitivityMean: 'n 20, mean 0.050 (simulated on plan prices, all proposals)',
      autoApproved: 0,
      ownerApproved: 12,
      plansWithoutPrimary: 1,
      manualRuns: 1,
      p7: 'NOT EVALUABLE',
      chargeSheetConfirmed: false,
      services: [{ id: 'paper-capture', role: 'the paper capture', required: true, state: 'paused' }],
      switches: [
        { name: 'Planning:OpenBook:UsePaperLedger', on: false, effect: 'not part of the exit', needed: false },
      ],
      blockers: ['C3: costs not confirmed'],
      notJudged: [{ name: 'no look-ahead', verdict: 'NOT EVALUABLE', proof: 'proved by the leak properties' }],
    },
    limits: ['Simulated fills on official 1-minute bars with no real execution.'],
    ...over,
  },
  text: 'phase check P10',
});

export function mountPhase(view: View = buildPhase()) {
  server.use(http.get('/api/paper/phase-check', () => HttpResponse.json(view)));
}
