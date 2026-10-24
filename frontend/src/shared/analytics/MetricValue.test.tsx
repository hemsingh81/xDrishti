import { screen, within } from '@testing-library/react';
import { nonValueStates, renderKit, usesGoodColour, words } from '@/test/analyticsKit';
import { formatR, formatRate } from './format';
import type { MetricState } from './metric';
import { MetricCell } from './MetricCell';
import { MetricTile } from './MetricTile';
import { MetricValue } from './MetricValue';

describe('MetricValue: a value', () => {
  it('prints the formatted number and no status word', () => {
    const { container } = renderKit(<MetricValue state={{ kind: 'value', value: 0.3 }} />);
    expect(container).toHaveTextContent('+0.30');
    for (const word of Object.values(words)) expect(screen.queryByText(word)).not.toBeInTheDocument();
  });

  it('uses the formatter it is given', () => {
    renderKit(<MetricValue state={{ kind: 'value', value: 0.542 }} format={formatRate} />);
    expect(screen.getByText('54.2%')).toBeInTheDocument();
  });

  it('prints a real zero as a number', () => {
    renderKit(<MetricValue state={{ kind: 'value', value: 0 }} format={formatR} />);
    expect(screen.getByText('0.00 R')).toBeInTheDocument();
  });

  it('colours gain and loss only on request, and the sign is in the text either way', () => {
    const { container, rerender } = renderKit(<MetricValue state={{ kind: 'value', value: 0.3 }} tone="gain-loss" />);
    expect(screen.getByText('+0.30')).toBeInTheDocument();
    expect(usesGoodColour(container)).toBe(true);
    rerender(<MetricValue state={{ kind: 'value', value: -0.4 }} tone="gain-loss" />);
    expect(screen.getByText('-0.40')).toBeInTheDocument();
    expect(usesGoodColour(container)).toBe(false);
  });
});

describe('MetricValue: provisional', () => {
  it('prints the number with the word and the reason', () => {
    renderKit(
      <MetricValue state={{ kind: 'provisional', value: 0.8, reason: 'Plans are provisional' }} tone="gain-loss" />,
    );
    expect(screen.getByText('+0.80')).toBeInTheDocument();
    expect(screen.getByText(words.provisional)).toBeInTheDocument();
    expect(screen.getByText('Plans are provisional')).toBeInTheDocument();
  });

  it('is never drawn in the good colour, even for a positive figure', () => {
    const { container } = renderKit(
      <MetricValue state={{ kind: 'provisional', value: 0.8, reason: 'Provisional' }} tone="gain-loss" />,
    );
    expect(usesGoodColour(container)).toBe(false);
  });
});

describe.each(nonValueStates.map((state) => [state.kind, state] as const))('MetricValue: %s', (kind, state) => {
  it('prints its word and its reason, as text', () => {
    renderKit(<MetricValue state={state} />);
    expect(screen.getByText(words[kind as keyof typeof words])).toBeInTheDocument();
    if ('reason' in state && state.reason) expect(screen.getByText(state.reason)).toBeInTheDocument();
  });

  it('never prints a digit as its figure (only the n of an insufficient sample)', () => {
    const { container } = renderKit(<MetricValue state={state} format={(v) => `FIGURE ${v}`} />);
    expect(container).not.toHaveTextContent('FIGURE');
    const text = container.textContent;
    if (kind === 'insufficient') expect(text).toBe('Insufficient datan 12 of at least 30');
    else expect(text).not.toMatch(/\d/);
  });

  it('is never drawn in the good colour, in either mode', () => {
    for (const mode of ['light', 'dark'] as const) {
      const { container, unmount } = renderKit(<MetricValue state={state} tone="gain-loss" />, mode);
      expect(usesGoodColour(container, mode)).toBe(false);
      unmount();
    }
  });

  it('has a decorative icon and the word as the status: nothing depends on colour', () => {
    const { container } = renderKit(<MetricValue state={state} />);
    expect(screen.queryByRole('img')).not.toBeInTheDocument();
    const icon = container.querySelector('svg');
    expect(icon).toHaveAttribute('aria-hidden', 'true');
  });
});

describe('MetricValue: figures that are not numbers', () => {
  it.each([Number.NaN, Number.POSITIVE_INFINITY, Number.NEGATIVE_INFINITY])(
    'prints Not available for a value of %s, never NaN, Infinity or 0',
    (value) => {
      const { container } = renderKit(<MetricValue state={{ kind: 'value', value }} format={formatR} />);
      expect(screen.getByText(words.unavailable)).toBeInTheDocument();
      expect(container.textContent).not.toMatch(/NaN|Infinity|\d/);
    },
  );

  it('ignores a stray number on a non-value from an API that sent one', () => {
    const stray = { kind: 'unavailable', reason: 'No fills', value: 0 } as unknown as MetricState;
    const { container } = renderKit(<MetricValue state={stray} />);
    expect(container.textContent).not.toMatch(/\d/);
  });
});

describe('MetricCell', () => {
  const inTable = (cell: React.ReactElement) => (
    <table>
      <tbody>
        <tr>{cell}</tr>
      </tbody>
    </table>
  );

  it('is a right-aligned cell with the figure and its n', () => {
    renderKit(inTable(<MetricCell state={{ kind: 'value', value: 1.5 }} n={42} />));
    const cell = screen.getByRole('cell');
    expect(within(cell).getByText('+1.50')).toBeInTheDocument();
    expect(within(cell).getByText('n 42')).toBeInTheDocument();
    expect(cell).toHaveClass('MuiTableCell-alignRight');
  });

  it('shows no n beside a non-value, which says its own', () => {
    renderKit(inTable(<MetricCell state={{ kind: 'insufficient', n: 7, min: 30 }} n={7} />));
    const cell = screen.getByRole('cell');
    expect(within(cell).getByText('Insufficient data')).toBeInTheDocument();
    expect(within(cell).getAllByText(/n 7/)).toHaveLength(1);
  });
});

describe('MetricTile', () => {
  it('shows the label, the figure and the compact evidence', () => {
    renderKit(
      <MetricTile
        label="Expectancy"
        state={{ kind: 'value', value: 0.31 }}
        format={formatR}
        evidence={{ n: 128, range: { from: '2024-04-01', to: '2026-04-30' }, basis: 'gross' }}
      />,
    );
    expect(screen.getByText('Expectancy')).toBeInTheDocument();
    expect(screen.getByText('+0.31 R')).toBeInTheDocument();
    expect(screen.getByText('n 128 · 1 Apr 2024 to 30 Apr 2026 · Gross R (before costs)')).toBeInTheDocument();
  });

  it.each(nonValueStates.map((state) => [state.kind, state] as const))(
    'a %s tile prints its word, no figure and no good colour',
    (kind, state) => {
      const { container } = renderKit(
        <MetricTile label="Win rate" state={state} format={formatRate} tone="gain-loss" />,
      );
      expect(screen.getByText(words[kind as keyof typeof words])).toBeInTheDocument();
      expect(usesGoodColour(container)).toBe(false);
      expect(container).not.toHaveTextContent('%');
    },
  );
});
