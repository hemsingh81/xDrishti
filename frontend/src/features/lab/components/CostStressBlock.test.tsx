import { render, screen } from '@testing-library/react';
import type { Schemas } from '@/shared/api';
import { CostStressBlock } from './CostStressBlock';
import { VerdictPanel } from './VerdictPanel';

const block = {
  mode: 'ReportOnly',
  enforce: false,
  settings: 'ReportOnly/enforce=off',
  settingsHash: 'h',
  costsChecked: false,
  sheetId: 'default',
  rows: [
    {
      multiplier: 1,
      trades: 90,
      expectancyR: 0.2,
      threshold: 0.1,
      comparison: '>=',
      status: 'unavailable',
      reason: 'unavailable: costs not checked against a bill: stress gates cannot PASS',
    },
    {
      multiplier: 1.5,
      trades: 90,
      expectancyR: 0.14,
      threshold: 0.08,
      comparison: '>=',
      status: 'unavailable',
      reason: 'x',
    },
    { multiplier: 2, trades: 90, expectancyR: 0.08, threshold: 0, comparison: '>', status: 'unavailable', reason: 'x' },
  ],
  tradable: 'unavailable',
  delay: {
    minutes: 1,
    signals: 100,
    delayedFilled: 90,
    baseMeanR: 0.2,
    delayedMeanR: 0.16,
    retention: 0.8,
    required: 0.7,
    status: 'unavailable',
    reason: 'r',
    note: 'n',
  },
  capped: false,
  formula: 'stressed net R = RExact + m x (net R - RExact)',
  limit: 'exact for charges, first-order for slippage.',
} as Schemas['CostStressView'];

describe('CostStressBlock (CF-05)', () => {
  it('shows the three multipliers, the delay line and the not-checked label, with the status as a word', () => {
    render(<CostStressBlock block={block} />);

    const section = screen.getByRole('region', { name: 'Costs and delay' });
    expect(section).toHaveTextContent('Costs not checked against a bill: stress gates cannot PASS.');
    expect(section).toHaveTextContent('Report only: the outcome is not changed.');
    expect(screen.getByRole('table', { name: 'Cost stress' })).toHaveTextContent('1.5x');
    expect(screen.getAllByText('unavailable').length).toBeGreaterThanOrEqual(3);
    expect(section).toHaveTextContent('Fill 1 minute later: unavailable. 100 signals, 90 still fill');
    expect(section).toHaveTextContent('first-order for slippage');
    expect(screen.getByRole('table', { name: 'Cost stress' })).toHaveTextContent(
      'unavailable: costs not checked against a bill',
    );
  });

  it('drops the label once the sheet is checked and says when the outcome was capped', () => {
    render(
      <CostStressBlock
        block={{
          ...block,
          costsChecked: true,
          enforce: true,
          capped: true,
          tradable: 'fail',
          rows: block.rows.map((r) => ({ ...r, reason: 'met' })),
        }}
      />,
    );

    const section = screen.getByRole('region', { name: 'Costs and delay' });
    expect(section).not.toHaveTextContent('not checked against a bill');
    expect(section).toHaveTextContent('Enforced: the outcome was capped at MARGINAL.');
  });

  it('renders nothing for a result stored before the block', () => {
    const { container } = render(<CostStressBlock block={null} />);

    expect(container).toBeEmptyDOMElement();
  });

  it('is part of the verdict panel when the verdict has it', () => {
    const verdict = {
      outcome: 'MARGINAL',
      variantCount: 1,
      adjustmentFactor: 1,
      evidence: { oosTrades: 1, folds: 1, positiveFolds: 1, calibrationErrorPp: 0, method: 'walk_forward' },
      gates: [],
      limits: ['x'],
      cells: [],
      fullPeriod: [],
      labels: [],
      costStress: block,
    } as unknown as Schemas['LabVerdictView'];

    render(<VerdictPanel verdict={verdict} />);

    expect(screen.getByRole('region', { name: 'Costs and delay' })).toBeInTheDocument();
  });
});
