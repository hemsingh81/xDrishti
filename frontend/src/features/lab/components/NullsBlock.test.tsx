import { render, screen, within } from '@testing-library/react';
import type { Schemas } from '@/shared/api';
import { NullsBlock } from './NullsBlock';

const block = {
  mode: 'ReportOnly',
  enforce: false,
  capped: false,
  settings: 'draws=1000',
  settingsHash: 'h',
  basis: 'Development window, in-sample walk.',
  notes: ['3 trades left out: no bars.'],
  randomEntry: {
    oosTrades: 120,
    trades: 118,
    draws: 1000,
    validSeeds: 998,
    realMeanR: 0.2,
    randomMeanR: 0.01,
    p05: -0.1,
    p50: 0.01,
    p95: 0.12,
    p99: 0.15,
    percentile: 95,
    percentileValue: 0.12,
    countAtLeast: 12,
    pValue: 0.013,
    threshold: 0.05,
    comparison: '<=',
    status: 'pass',
    reason: 'p 0.013 is at most 0.05',
    droppedShare: 0.02,
    redraws: 4,
    seed: '42',
  },
  placebos: {
    shapes: 20,
    comparable: 19,
    minComparable: 15,
    realMeanR: 0.2,
    realRank: 1,
    pValue: 0.05,
    threshold: 0.05,
    comparison: '<=',
    status: 'pass',
    reason: 'ranks first',
    rows: [
      { shape: 'mirror', trades: 118, meanR: -0.2, rank: 20, comparable: true, reason: null },
      { shape: 'shift_+15', trades: 2, meanR: null, rank: null, comparable: false, reason: 'too few trades' },
    ],
  },
  mirror: {
    trades: 118,
    mirrorMeanR: -0.2,
    realMeanR: 0.2,
    threshold: 0,
    comparison: '<=',
    status: 'fail',
    reason: 'mostly drift',
  },
  plateau: {
    mode: 'ReportOnly',
    enforce: false,
    settings: 's',
    settingsHash: 'h',
    retention: 0.7,
    minPoints: 3,
    comparison: '>=',
    status: 'unavailable',
    reason: 'one parameter has too few points',
    capped: false,
    basis: 'In-sample sweep.',
    parameters: [
      {
        param: 'orb_minutes',
        declared: 15,
        validPoints: 5,
        baseMeanR: 0.2,
        lowerValue: 10,
        lowerRetention: 0.9,
        upperValue: 20,
        upperRetention: null,
        median: 0.15,
        centreValue: 15,
        centreMeanR: 0.18,
        reading: 'plateau',
        status: 'pass',
        reason: 'neighbours keep the edge',
        points: [],
      },
    ],
  },
} as Schemas['NullsView'];

describe('NullsBlock (CF-07)', () => {
  it('shows each reading with its status as a word and the figures', () => {
    render(<NullsBlock block={block} />);

    const s = screen.getByRole('region', { name: 'Chance and plateau' });
    expect(s).toHaveTextContent('Report only: the outcome is not changed.');
    expect(s).toHaveTextContent('Random entry: pass');
    expect(s).toHaveTextContent('p = 0.013');
    expect(s).toHaveTextContent('12 of 998 seeds');
    expect(s).toHaveTextContent('Mirror: fail');
    expect(s).toHaveTextContent('Plateau (in-sample sweep): unavailable (one parameter has too few points');
    expect(s).toHaveTextContent('3 trades left out: no bars.');
    const placebos = screen.getByRole('table', { name: 'Placebo shapes' });
    expect(within(placebos).getByText('shift_+15')).toBeInTheDocument();
    expect(within(placebos).getByText('not compared: too few trades')).toBeInTheDocument();
    const plateau = screen.getByRole('table', { name: 'Plateau by parameter' });
    expect(within(plateau).getByText('orb_minutes')).toBeInTheDocument();
    expect(within(plateau).getByText('n/a')).toBeInTheDocument();
  });

  it('reads an unknown status as unavailable with the reason, and prints no non-finite number', () => {
    render(
      <NullsBlock
        block={{
          ...block,
          randomEntry: {
            ...block.randomEntry,
            status: 'maybe',
            reason: 'too few seeds',
            pValue: null,
            realMeanR: Number.NaN,
          },
        }}
      />,
    );

    const s = screen.getByRole('region', { name: 'Chance and plateau' });
    expect(s).toHaveTextContent('Random entry: unavailable (too few seeds)');
    expect(s).not.toHaveTextContent('NaN');
    expect(s).not.toHaveTextContent('Random entry: pass');
  });

  it('says when the sets are absent or empty and when the verdict has no block', () => {
    const { rerender } = render(
      <NullsBlock
        block={{
          ...block,
          placebos: { ...block.placebos!, rows: [] },
          mirror: null,
          plateau: { ...block.plateau!, parameters: [] },
        }}
      />,
    );
    expect(screen.getByText('No placebo shape could be walked.')).toBeInTheDocument();
    expect(screen.getByText('Mirror: not computed.')).toBeInTheDocument();
    expect(screen.getByText('No parameter was swept.')).toBeInTheDocument();

    rerender(<NullsBlock block={null} />);
    expect(screen.getByText('Chance and plateau: not computed.')).toBeInTheDocument();
  });

  it('does not say report only when the plateau is enforced or capped', () => {
    const plateau = { ...block.plateau!, enforce: true };
    const { rerender } = render(<NullsBlock block={{ ...block, plateau }} />);
    expect(screen.getByText('Partly enforced: see each line.')).toBeInTheDocument();
    expect(screen.queryByText(/Report only: the outcome/)).toBeNull();
    rerender(<NullsBlock block={{ ...block, plateau: { ...plateau, capped: true } }} />);
    expect(screen.getByText('Enforced: the outcome was capped at MARGINAL.')).toBeInTheDocument();
  });

  it('says when an enforced null capped the outcome', () => {
    render(<NullsBlock block={{ ...block, enforce: true, capped: true }} />);
    expect(screen.getByText('Enforced: the outcome was capped at MARGINAL.')).toBeInTheDocument();
  });
});
