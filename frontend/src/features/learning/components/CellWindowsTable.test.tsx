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
