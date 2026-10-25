import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { renderWithProviders } from '@/test/render';
import { HeatGrid, type HeatGridCell } from './HeatGrid';

const rows = [
  { id: 'a', label: 'Alpha', detail: 'BUY' },
  { id: 'b', label: 'Beta' },
];
const columns = [
  { id: '5m', label: '5m' },
  { id: '15m', label: '15m' },
  { id: '1h', label: '1h' },
];
const made = new Set(['a|5m', 'a|15m', 'a|1h', 'b|15m', 'b|1h']);

function cell(rowId: string, columnId: string): HeatGridCell | null {
  const id = `${rowId}|${columnId}`;
  if (!made.has(id)) return null;
  return {
    key: id,
    name: `cell ${id}`,
    state: id === 'a|5m' ? { kind: 'insufficient', n: 3, min: 30 } : { kind: 'value', value: 1 },
    content: id,
  };
}

function show(onOpen = vi.fn()) {
  renderWithProviders(
    <HeatGrid
      label="Demo"
      regionLabel="Demo region"
      rowHeader="Name"
      rows={rows}
      columns={columns}
      cell={cell}
      onOpen={onOpen}
    />,
  );
  return onOpen;
}

describe('HeatGrid', () => {
  it('is a grid with row and column headers and says where a row has no cell', () => {
    show();
    expect(screen.getByRole('grid', { name: 'Demo' })).toBeInTheDocument();
    expect(screen.getAllByRole('columnheader').map((c) => c.textContent)).toEqual(['Name', '5m', '15m', '1h']);
    expect(screen.getByRole('rowheader', { name: /Alpha BUY/ })).toBeInTheDocument();
    expect(screen.getByText('No cell')).toBeInTheDocument();
  });

  it('opens a cell by its key', async () => {
    const onOpen = show();
    await userEvent.click(screen.getByRole('button', { name: 'cell b|15m' }));
    expect(onOpen).toHaveBeenCalledWith('b|15m');
  });

  it('has one tab stop and moves with the arrows, Home, End and the Ctrl forms, skipping positions without a cell', async () => {
    show();
    const button = (name: string) => screen.getByRole('button', { name: `cell ${name}` });
    expect(button('a|5m')).toHaveAttribute('tabindex', '0');
    expect(button('a|15m')).toHaveAttribute('tabindex', '-1');

    button('a|5m').focus();
    await userEvent.keyboard('{ArrowRight}');
    expect(button('a|15m')).toHaveFocus();
    expect(button('a|15m')).toHaveAttribute('tabindex', '0');
    expect(button('a|5m')).toHaveAttribute('tabindex', '-1');

    await userEvent.keyboard('{ArrowDown}');
    expect(button('b|15m')).toHaveFocus();
    await userEvent.keyboard('{ArrowLeft}');
    expect(button('b|15m')).toHaveFocus(); // b has no 5m cell: stay
    await userEvent.keyboard('{End}');
    expect(button('b|1h')).toHaveFocus();
    await userEvent.keyboard('{Home}');
    expect(button('b|15m')).toHaveFocus();
    await userEvent.keyboard('{Control>}{Home}{/Control}');
    expect(button('a|5m')).toHaveFocus();
    await userEvent.keyboard('{Control>}{End}{/Control}');
    expect(button('b|1h')).toHaveFocus();
    await userEvent.keyboard('{ArrowUp}');
    expect(button('a|1h')).toHaveFocus();
  });

  it('marks the open cell, hatches insufficient cells and shades only value cells', () => {
    renderWithProviders(
      <HeatGrid
        label="Demo"
        regionLabel="Demo region"
        rowHeader="Name"
        rows={rows}
        columns={columns}
        selectedKey="a|15m"
        onOpen={vi.fn()}
        cell={(rowId, columnId) => {
          const item = cell(rowId, columnId);
          return item?.key === 'a|15m' ? { ...item, shade: { tone: 'success', alpha: 0.2 } } : item;
        }}
      />,
    );
    expect(screen.getByRole('button', { name: 'cell a|15m' })).toHaveAttribute('aria-expanded', 'true');
    expect(screen.getByRole('button', { name: 'cell a|1h' })).toHaveAttribute('aria-expanded', 'false');
    expect(getComputedStyle(screen.getByRole('button', { name: 'cell a|5m' })).backgroundImage).toContain(
      'repeating-linear-gradient',
    );
    expect(getComputedStyle(screen.getByRole('button', { name: 'cell a|1h' })).backgroundImage).not.toContain(
      'repeating-linear-gradient',
    );
    expect(getComputedStyle(screen.getByRole('button', { name: 'cell a|15m' })).backgroundColor).not.toBe(
      getComputedStyle(screen.getByRole('button', { name: 'cell a|1h' })).backgroundColor,
    );
  });
});
