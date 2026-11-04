import { render, screen } from '@testing-library/react';
import type { Schemas } from '@/shared/api';
import { RobustnessSummary } from './RobustnessSummary';
import { VerdictPanel } from './VerdictPanel';

const verdict = {
  outcome: 'MARGINAL',
  variantCount: 5,
  adjustmentFactor: 1.6,
  evidence: {
    oosTrades: 100,
    expectancyR: 0.3,
    profitFactor: 1.5,
    folds: 6,
    positiveFolds: 5,
    maxDrawdownR: 4,
    calibrationErrorPp: 0,
    method: 'walk_forward',
  },
  gates: [],
  limits: ['x'],
  cells: [],
  fullPeriod: [],
  labels: [],
} as Schemas['LabVerdictView'];

const figure = {
  mode: 'ReportOnly',
  trials: 5,
  ledgerTrials: 5,
  trialSrSd: 0.05,
  luckBarSr: 0.09,
  sr: 0.15,
  nEff: 100,
  skew: 0,
  kurt: 3,
  probability: 0.72,
  minProbability: 0.95,
  basis: 'nEff',
  settings: 'ReportOnly/0.95/stored/NEff',
  note: 'N counts neighbouring variants as independent: the bar is higher than strictly needed.',
} as Schemas['DeflatedSharpeView'];

describe('VerdictPanel, deflated Sharpe ratio (CF-03)', () => {
  it('prints the probability beside the factor, says it decides nothing, and prints the limit', () => {
    render(<VerdictPanel verdict={{ ...verdict, deflatedSharpe: figure }} />);

    const section = screen.getByRole('region', { name: 'Deflated Sharpe ratio' });
    expect(section).toHaveTextContent('Deflated Sharpe ratio 0.72');
    expect(section).toHaveTextContent('informational, no gate uses it');
    expect(section).toHaveTextContent('independent: the bar is higher than strictly needed');
    expect(screen.getByText(/thresholds/)).toBeInTheDocument();
  });

  it('degrades to the reason when the probability is unavailable', () => {
    const unavailable = {
      ...figure,
      probability: null,
      note: 'unavailable: trial Sharpe ratios not stored (fewer than 2)',
    };

    render(<VerdictPanel verdict={{ ...verdict, deflatedSharpe: unavailable }} />);

    const section = screen.getByRole('region', { name: 'Deflated Sharpe ratio' });
    expect(section).toHaveTextContent('not available');
    expect(section).toHaveTextContent('unavailable: trial Sharpe ratios not stored');
    expect(section).not.toHaveTextContent('Deflated Sharpe ratio 0');
  });

  it('says not computed for a result stored before the figure', () => {
    render(<VerdictPanel verdict={verdict} />);

    expect(screen.getByText('Deflated Sharpe ratio: not computed.')).toBeInTheDocument();
  });
});

describe('RobustnessSummary, block bootstrap (CF-03)', () => {
  const robustness = {
    sensitivity: { timeframe: '15m', exit: 'x', base: null, parameters: [], label: '' },
    monteCarlo: null,
    byYear: [],
    signFlipBaseline: null,
    byRegime: 'unavailable',
    limits: [],
  } as unknown as Schemas['RobustnessView'];

  it('prints the interval and its width against the independent one', () => {
    const block = {
      mode: 'ReportOnly',
      sessions: 40,
      blockLength: 3,
      draws: 1000,
      seed: '1',
      observedMeanR: 0.3,
      p05: 0.1,
      p50: 0.3,
      p95: 0.5,
      ddMedian: 3,
      ddP95: 6,
      shareNonPositive: 0.01,
      widthRatioToIid: 1.5,
      settings: 'ReportOnly/1000/auto',
      note: 'Whole entry sessions are resampled in blocks.',
    } as Schemas['MonteCarloBlockView'];

    render(<RobustnessSummary robustness={{ ...robustness, monteCarloBlock: block }} />);

    expect(screen.getByText(/Block bootstrap \(40 sessions, blocks of 3, 1000 draws\)/)).toBeInTheDocument();
    expect(screen.getByText(/1\.5 times the width/)).toBeInTheDocument();
  });

  it('says not available with the reason when there is no interval', () => {
    const block = {
      mode: 'ReportOnly',
      sessions: 1,
      blockLength: null,
      draws: 1000,
      seed: '1',
      observedMeanR: null,
      p05: null,
      p50: null,
      p95: null,
      ddMedian: null,
      ddP95: null,
      shareNonPositive: null,
      widthRatioToIid: null,
      settings: 'ReportOnly/1000/auto',
      note: 'unavailable: fewer than 2 sessions with a trade',
    } as Schemas['MonteCarloBlockView'];

    render(<RobustnessSummary robustness={{ ...robustness, monteCarloBlock: block }} />);

    expect(screen.getByText(/Block bootstrap: not available/)).toBeInTheDocument();
    expect(screen.getByText(/unavailable: fewer than 2 sessions/)).toBeInTheDocument();
  });

  it('says not computed for a result stored before the block bootstrap', () => {
    render(<RobustnessSummary robustness={robustness} />);

    expect(screen.getByText('Block bootstrap: not computed.')).toBeInTheDocument();
  });
});
