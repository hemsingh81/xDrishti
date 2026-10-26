import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { priceStub } from '@/test/chartStub';
import { renderWithProviders } from '@/test/render';
import { barsTableCap } from './PriceBarsTable';
import { PriceChart } from './PriceChart';
import type { PriceBar, PriceEvent } from './priceChartTypes';

const base = Date.UTC(2026, 2, 2, 3, 45) / 1000;
const bars = (n: number): PriceBar[] =>
  Array.from({ length: n }, (_, i) => ({ t: base + i * 60, o: 100, h: 101, l: 99, c: 100.5, v: 10 }));
const events: PriceEvent[] = [
  {
    id: 'in',
    t: base + 600,
    price: 100,
    kind: 'entry',
    side: 'BUY',
    text: 'Entry 100%',
    announce: 'Entry, 09:25, price 100.00, share 100%',
  },
  {
    id: 'out',
    t: base + 1200,
    price: 105,
    kind: 'exit',
    side: 'BUY',
    text: 'Exit 100%',
    announce: 'Exit, 09:35, price 105.00, share 100%',
  },
];
const lines = [
  { id: 'stop', kind: 'stop' as const, price: 98, title: 'Stop 98.00' },
  { id: 'target', kind: 'target' as const, price: 0, title: 'Target 0.00' },
];

describe('PriceChart', () => {
  it('hands the lazy canvas candles, only the drawable levels, markers and the theme colours', async () => {
    renderWithProviders(<PriceChart label="ORB BUY, 1m" bars={bars(30)} lines={lines} events={events} />);

    await screen.findByTestId('price-canvas');
    expect(priceStub.last?.candles).toHaveLength(30);
    expect(priceStub.last?.lines.map((l) => l.title)).toEqual(['Stop 98.00']);
    expect(priceStub.last?.markers.map((m) => [m.shape, m.text])).toEqual([
      ['arrowUp', 'Entry 100%'],
      ['square', 'Exit 100%'],
    ]);
    expect(priceStub.last?.colors.up).not.toBe(priceStub.last?.colors.down);
    expect(screen.getByRole('img', { name: /30 bars/ })).toBeInTheDocument();
  });

  it('moves the crosshair to each event with the buttons and announces it politely', async () => {
    const user = userEvent.setup();
    renderWithProviders(<PriceChart label="x" bars={bars(30)} events={events} />);
    await screen.findByTestId('price-canvas');

    await user.click(screen.getByRole('button', { name: 'Next event' }));
    expect(priceStub.last?.focus).toEqual({ time: base + 600, price: 100 });
    expect(screen.getByRole('status')).toHaveTextContent('Entry, 09:25, price 100.00, share 100%');

    await user.click(screen.getByRole('button', { name: 'Next event' }));
    await user.click(screen.getByRole('button', { name: 'Next event' }));
    expect(priceStub.last?.focus).toEqual({ time: base + 1200, price: 105 });
    expect(screen.getByRole('status')).toHaveTextContent('Exit, 09:35, price 105.00, share 100%');

    await user.click(screen.getByRole('button', { name: 'Previous event' }));
    expect(priceStub.last?.focus?.price).toBe(100);
  });

  it('has the same bars as a table, collapsed, and says when it lists only the first 500', async () => {
    const user = userEvent.setup();
    renderWithProviders(<PriceChart label="x" bars={bars(barsTableCap + 20)} />);
    await screen.findByTestId('price-canvas');
    expect(screen.queryByRole('table')).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: `Bars (${String(barsTableCap + 20)})` }));

    expect(screen.getByRole('table', { name: 'x, bars' })).toBeInTheDocument();
    expect(screen.getAllByRole('row')).toHaveLength(barsTableCap + 1);
    expect(
      screen.getByText(`The first ${String(barsTableCap)} of ${String(barsTableCap + 20)} bars are listed.`),
    ).toBeInTheDocument();
  });

  it('names an event outside the window instead of drawing it', async () => {
    renderWithProviders(<PriceChart label="x" bars={bars(3)} events={events} />);
    await screen.findByTestId('price-canvas');
    expect(priceStub.last?.markers).toHaveLength(0);
    expect(screen.getByText(/Outside this window, not drawn: Entry, 09:25/)).toBeInTheDocument();
  });

  it('opens the table and offers another try when the chart library cannot load', async () => {
    priceStub.fail = true;
    renderWithProviders(<PriceChart label="x" bars={bars(5)} />);
    expect(await screen.findByText(/The chart could not be loaded/)).toBeInTheDocument();
    expect(screen.getByRole('table', { name: 'x, bars' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument();
  });

  it('shows the empty state for no bars', () => {
    renderWithProviders(<PriceChart label="x" bars={[]} empty={<p>Nothing here</p>} />);
    expect(screen.getByText('Nothing here')).toBeInTheDocument();
  });
});
