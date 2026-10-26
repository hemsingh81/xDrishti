import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { renderKit } from '@/test/analyticsKit';
import { appliedCss } from '@/test/css';
import { BarList } from './BarList';
import { ColumnBars } from './ColumnBars';
import { formatR } from './format';

describe('BarList', () => {
  it('draws the centre line one pixel wide (a bare 1 is 100 % in sx and overflowed the card)', () => {
    const { container } = renderKit(
      <BarList
        label="Months"
        format={formatR}
        rows={[{ id: 'a', label: 'Mar', state: { kind: 'value', value: 4.2 } }]}
      />,
    );
    const widths = [...container.querySelectorAll('li [aria-hidden="true"] *')].map((el) => appliedCss(el).base);
    expect(widths.some((css) => css.includes('width: 1px'))).toBe(true);
    expect(widths.some((css) => /(^|[^-])width: 100%/.test(css) && css.includes('left: 50%'))).toBe(false);
  });

  it('prints the figure as text and a state instead of a bar for a non-value', () => {
    const { container } = renderKit(
      <BarList
        label="Months"
        format={formatR}
        rows={[
          { id: 'a', label: 'Mar', state: { kind: 'value', value: 4.2 }, n: 10 },
          { id: 'b', label: 'Apr', state: { kind: 'value', value: -2.1 }, n: 8 },
          { id: 'c', label: 'May', state: { kind: 'insufficient', n: 3, min: 30 } },
        ]}
      />,
    );
    const rows = within(screen.getByRole('list', { name: 'Months' })).getAllByRole('listitem');
    expect(rows).toHaveLength(3);
    expect(rows[0]).toHaveTextContent('+4.20 R');
    expect(rows[1]).toHaveTextContent('-2.10 R');
    expect(rows[2]).toHaveTextContent('Insufficient data');
    expect(rows[2]).not.toHaveTextContent(/\d R/);
    expect(container.querySelectorAll('li [aria-hidden="true"]').length).toBeGreaterThan(0);
  });
});

describe('ColumnBars', () => {
  it('has the same numbers as a table on request', async () => {
    renderKit(
      <ColumnBars
        label="R distribution"
        binHeader="R"
        bins={[
          { id: '0', label: '-1.00 to -0.50', count: 4 },
          { id: '1', label: '-0.50 to 0.00', count: 9 },
        ]}
      />,
    );
    await userEvent.setup().click(screen.getByRole('button', { name: 'Show R distribution as table' }));
    const table = screen.getByRole('table', { name: 'R distribution table' });
    expect(within(table).getByText('9')).toBeInTheDocument();
  });
});
