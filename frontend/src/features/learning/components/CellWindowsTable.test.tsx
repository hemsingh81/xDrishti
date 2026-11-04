import { render, screen, within } from '@testing-library/react';
import { buildCell } from '@/test/learningFixtures';
import { CellWindowsTable } from './CellWindowsTable';

const row = (name: string) => screen.getByRole('row', { name: new RegExp(name) });

describe('CellWindowsTable, independent n (CF-01)', () => {
  it('prints independent n, the trades needed and the smallest visible edge beside n when the block is there', () => {
    const dependence = {
      mode: 'ReportOnly',
      n: 142,
      nEff: 38.5,
      designEffect: 3.7,
      method: 'cluster',
      neededTrades: 396,
      minDetectableEdgeR: 0.5,
      minTrl: 273,
      minTrlDependent: 1010,
      yearsToEvidence: 10,
      enoughByNEff: true,
      note: '',
      summary: 'n=142, effective 38.5; needs about 396 to see the target edge; smallest visible now 0.50 R',
    };

    render(<CellWindowsTable cells={[buildCell({ dependence })]} />);

    expect(within(row('Trades \\(n\\)')).getByText('142')).toBeInTheDocument();
    expect(within(row('Independent n')).getByText('38.50')).toBeInTheDocument();
    expect(within(row('Independent trades needed')).getByText('396')).toBeInTheDocument();
    expect(within(row('Smallest edge visible now')).getByText(/0\.5/)).toBeInTheDocument();
  });

  it('says the figure is unavailable, with the reason, when the block says so', () => {
    const dependence = {
      mode: 'ReportOnly',
      n: 4,
      nEff: null,
      designEffect: null,
      method: 'none',
      neededTrades: null,
      minDetectableEdgeR: null,
      minTrl: null,
      minTrlDependent: null,
      yearsToEvidence: null,
      enoughByNEff: null,
      note: 'unavailable: n < 5',
      summary: 'n=4; effective n unavailable: n < 5',
    };

    render(<CellWindowsTable cells={[buildCell({ dependence })]} />);

    expect(within(row('Independent n')).getByText(/unavailable/i)).toBeInTheDocument();
    expect(within(row('Independent n')).queryByText('0')).not.toBeInTheDocument();
  });

  it('reads not computed for a row stored before the block existed', () => {
    render(<CellWindowsTable cells={[buildCell()]} />);

    expect(within(row('Independent n')).getByText('Not computed')).toBeInTheDocument();
  });
});

describe('CellWindowsTable, shrunk R (CF-02)', () => {
  it('prints the shrunk mean and its lower bound beside the raw expectancy', () => {
    const shrinkage = {
      mode: 'ReportOnly',
      parentLevel: 'strategy',
      parentMeanR: 0.1,
      tauSquared: 0.04,
      tauSource: 'estimated',
      blend: 0.2174,
      shrunkMeanR: 0.1652,
      shrunkLowerR: -0.1258,
      estimatedStrength: 20,
      strengthSource: 'default',
      shrunkWinRate: 0.53,
      nUsed: 10,
      note: '',
    };

    render(<CellWindowsTable cells={[buildCell({ shrinkage })]} />);

    expect(within(row('Shrunk R \\(past edge')).getByText(/0\.17/)).toBeInTheDocument();
    expect(within(row('Shrunk R, lower bound')).getByText(/0\.13/)).toBeInTheDocument();
    expect(row('Expectancy')).toBeInTheDocument();
  });

  it('says the figure is unavailable, with the reason, and never prints a number', () => {
    const shrinkage = {
      mode: 'ReportOnly',
      parentLevel: null,
      parentMeanR: null,
      tauSquared: null,
      tauSource: null,
      blend: null,
      shrunkMeanR: null,
      shrunkLowerR: null,
      estimatedStrength: null,
      strengthSource: null,
      shrunkWinRate: null,
      nUsed: 10,
      note: 'unavailable: no parent with 30 trades',
    };

    render(<CellWindowsTable cells={[buildCell({ shrinkage })]} />);

    expect(within(row('Shrunk R \\(past edge')).getByText(/no parent with 30 trades/)).toBeInTheDocument();
    expect(within(row('Shrunk R, lower bound')).getByText(/no parent with 30 trades/)).toBeInTheDocument();
  });

  it('reads not computed for a row stored before the figures existed', () => {
    render(<CellWindowsTable cells={[buildCell()]} />);

    expect(within(row('Shrunk R \\(past edge')).getByText('Not computed')).toBeInTheDocument();
  });
});
