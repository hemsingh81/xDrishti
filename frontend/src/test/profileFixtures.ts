import type { Schemas } from '@/shared/api';
import { basketIds } from './fixtures';
import { nonValue, value } from './reportFixtures';

export type Profile = Schemas['BasketProfileView'];
export type Ranking = Schemas['BasketRankingView'];
type Metric = Schemas['MetricView'];

const day = (i: number) => `2026-09-${String(14 + i).padStart(2, '0')}`;
const dates = Array.from({ length: 12 }, (_, i) => day(i));

const share = (m: Metric, voters = 8): Schemas['BreadthFigureView'] => ({
  share: m,
  voters,
  excluded: [],
  excludedReason: 'short_history',
});

export const evidence = (): Schemas['BenchmarkEvidenceView'] => ({
  basis: 'equal weight, daily rebalanced, price return, dividends not included',
  n: 3,
  range: { from: dates[0] ?? '', to: dates.at(-1) ?? '', source: 'query', fromSource: 'query', toSource: 'query' },
  limits: ['The profile describes the past; no figure here predicts.'],
  unavailableInputs: [],
});

/** A basket of three with a full profile: figures where the engine has enough, honest states where it has not. */
export function buildProfile(over: Partial<Profile> = {}): Profile {
  const tooFew = nonValue('insufficient', 'n = 3, at least 5 needed');
  return {
    basket: { id: basketIds.myChoice, name: 'MyChoice', kind: 'manual', paramsHash: null },
    window: { from: dates[0] ?? '', to: dates.at(-1) ?? '', source: 'query', fromSource: 'query', toSource: 'query' },
    membersAsOf: '2026-09-25',
    membershipBasis: 'single_snapshot',
    membershipFrom: '2026-06-01',
    members: { total: 3, counted: 3, excluded: [] },
    index: { id: '00000000-0000-4000-8000-0000000000f1', symbol: 'NIFTY', isDefault: true },
    hold: value(4),
    series: {
      dates,
      basket: dates.map((_, i) => 100 + i),
      index: dates.map((_, i) => 100 + i * 0.5),
      relativeStrength: dates.map((_, i) => 100 + i * 0.4),
      drawdown: dates.map(() => 0),
      rollingBeta60: dates.map((_, i) => (i > 5 ? 1.1 : null)),
    },
    risk: {
      vol20: value(0.12),
      vol60: nonValue('insufficient', 'n = 11, at least 61 needed'),
      vol252: nonValue('insufficient', 'n = 11, at least 253 needed'),
      mdd: value(-0.034),
      mddDuration: value(4),
      drawdownOpen: true,
      daysSincePeak: 3,
      beta60: nonValue('insufficient', 'n = 11, at least 60 needed'),
      beta252: nonValue('insufficient', 'n = 11, at least 252 needed'),
      alpha: nonValue('insufficient', 'n = 11, at least 252 needed'),
      trackingError: nonValue('insufficient', 'n = 11, at least 252 needed'),
      informationRatio: nonValue('insufficient', 'n = 11, at least 252 needed'),
      upCapture: nonValue('insufficient', '6 up days, at least 10 needed'),
      downCapture: nonValue('insufficient', '5 down days, at least 10 needed'),
    },
    indexDifference: { basket: value(4), index: value(10), difference: value(-6) },
    breadth: {
      b20: share(tooFew, 3),
      b50: share(nonValue('insufficient', 'n = 0, at least 5 needed'), 0),
      b200: share(nonValue('insufficient', 'n = 0, at least 5 needed'), 0),
      adShare: share(tooFew, 3),
      newHighShare: share(tooFew, 0),
      noBar: [],
      seriesDates: [],
      seriesB20: [],
    },
    dispersion: { today: tooFew, mean20: tooFew, iqr: tooFew, voters: 3 },
    correlation: { rhoBar: tooFew, nEff: tooFew, members: 3, pairs: 3, sampled: false, matrix: null },
    contribution: {
      top3Share: value(1),
      included: 3,
      top: [
        { symbol: 'AAA', returnPct: 24, share: 2 },
        { symbol: 'CCC', returnPct: 12, share: 1 },
      ],
      bottom: [{ symbol: 'BBB', returnPct: -24, share: -2 }],
      rows: [
        { symbol: 'AAA', returnPct: 24, share: 2 },
        { symbol: 'CCC', returnPct: 12, share: 1 },
        { symbol: 'BBB', returnPct: -24, share: -2 },
      ],
    },
    regime: {
      status: nonValue('unavailable', 'no regime code is recorded for the window'),
      unknown: 0,
      minDays: 30,
      rows: [],
    },
    turnover: { turnover: nonValue('unavailable', 'membership history starts on 2026-06-01'), adds: 0, removes: 0 },
    capacity: {
      medianAdv: value(250_000_000),
      p10Adv: value(100_000_000),
      basketCapacity: value(30_000_000),
      asmGsmShare: value(0),
      fnoShare: value(1),
      counted: 3,
      shortMembers: [],
      capacityPct: 0.01,
    },
    holdOut: {
      status: nonValue('empty', 'no closed trade of this basket in the default run set'),
      trades: 0,
      minPerHalf: 30,
      cells: [],
    },
    relativeStrength: [21, 63, 126, 252].map((window) => ({
      window,
      return: nonValue('insufficient', `n = 12, at least ${String(window + 1)} needed`),
    })),
    evidence: evidence(),
    ...over,
  };
}

/** A basket of eight, big enough for breadth, dispersion and a correlation matrix of four. */
export function buildRichProfile(over: Partial<Profile> = {}): Profile {
  const symbols = ['AAA', 'BBB', 'CCC', 'DDD'];
  return buildProfile({
    members: { total: 8, counted: 7, excluded: [{ symbol: 'ZZZ', reason: 'starts_late' }] },
    breadth: {
      b20: share(value(0.625)),
      b50: share(value(0.5)),
      b200: share(value(0.25)),
      adShare: share(value(0.6)),
      newHighShare: share(value(0.25)),
      noBar: [],
      seriesDates: dates,
      seriesB20: dates.map((_, i) => (i === 0 ? null : 0.5 + i / 100)),
    },
    dispersion: { today: value(0.012), mean20: value(0.011), iqr: value(0.015), voters: 8 },
    correlation: {
      rhoBar: value(0.42),
      nEff: value(3.2),
      members: 8,
      pairs: 28,
      sampled: false,
      matrix: {
        total: 8,
        symbols,
        rho: symbols.map((_, r) =>
          symbols.map((__, c) => (r === c ? 1 : r + c === 3 ? null : 0.3 + 0.1 * Math.min(r, c))),
        ),
      },
    },
    regime: {
      status: value(2),
      unknown: 1,
      minDays: 30,
      rows: [
        {
          code: 1,
          label: 'UP_LOW_VOL',
          days: 40,
          meanReturn: value(0.001),
          vol: value(0.1),
          hitRate: value(0.55),
          state: 'value',
        },
        {
          code: 6,
          label: 'RANGE_HIGH_VOL',
          days: 9,
          meanReturn: nonValue('insufficient', 'n = 9, at least 30 needed'),
          vol: nonValue('insufficient', 'n = 9, at least 30 needed'),
          hitRate: nonValue('insufficient', 'n = 9, at least 30 needed'),
          state: 'insufficient',
        },
      ],
    },
    ...over,
  });
}

export function buildRankingRow(over: Partial<Schemas['RankingRowView']> = {}): Schemas['RankingRowView'] {
  return {
    basketId: basketIds.myChoice,
    name: 'MyChoice',
    rsReturn21: value(0.05),
    rsReturn63: nonValue('insufficient', 'n = 25, at least 64 needed'),
    rsReturn126: nonValue('insufficient', 'n = 25, at least 127 needed'),
    rsReturn252: nonValue('insufficient', 'n = 25, at least 253 needed'),
    rsRank: value(1),
    riskAdjusted: value(1.4),
    state: 'value',
    ...over,
  };
}

export function buildRanking(over: Partial<Ranking> = {}): Ranking {
  return {
    window: { from: dates[0] ?? '', to: dates.at(-1) ?? '', source: 'query', fromSource: 'query', toSource: 'query' },
    index: { id: '00000000-0000-4000-8000-0000000000f1', symbol: 'NIFTY', isDefault: true },
    rankWindow: 21,
    rows: [
      buildRankingRow(),
      buildRankingRow({
        basketId: basketIds.myLongTerm,
        name: 'MyLongTerm',
        rsReturn21: value(-0.02),
        rsRank: value(2),
        riskAdjusted: value(-0.3),
      }),
    ],
    overlap: [{ a: basketIds.myChoice, b: basketIds.myLongTerm, jaccard: value(0) }],
    separated: true,
    evidence: evidence(),
    ...over,
  };
}
