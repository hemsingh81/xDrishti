import { screen, waitFor, within } from '@testing-library/react';
import type { InstrumentSummary } from '@/shared/api';
import { createBasketsFake, type BasketsFake } from '@/test/basketsFake';
import { basketIds, sampleInstruments } from '@/test/fixtures';
import { renderRoutes } from '@/test/render';
import { server } from '@/test/server';
import { createTrackingFake } from '@/test/trackingFake';
import { BasketsPage } from './components/BasketsPage';

export { basketIds };

/** MyChoice holds three shares, the other two baskets are empty (the seeded set). */
export const threeInMyChoice = { [basketIds.myChoice]: ['NSE:TCS', 'NSE:RELIANCE', 'NSE:HDFCBANK'] };

interface Options {
  /** "?basket=…" style search of the URL. */
  search?: string;
  members?: Readonly<Record<string, readonly string[]>>;
  instruments?: readonly InstrumentSummary[];
  /** Runs before the screen renders: seed more history, queue a failure. */
  setup?: (fake: BasketsFake) => void;
}

/** Renders the Baskets screen at `/baskets` over the stateful tracking and baskets fakes. */
export function renderBaskets({
  search = '',
  members = threeInMyChoice,
  instruments = sampleInstruments,
  setup,
}: Options = {}) {
  const tracking = createTrackingFake(instruments);
  const fake = createBasketsFake({ tracking, members });
  setup?.(fake);
  server.use(...tracking.handlers, ...fake.handlers);
  const view = renderRoutes(
    [
      { path: '/baskets', element: <BasketsPage /> },
      { path: '/instruments', element: <p>Instruments screen</p> },
    ],
    { initialPath: `/baskets${search}` },
  );
  return { fake, tracking, ...view };
}

/** The members grid once its rows have loaded. */
export async function membersGrid() {
  const grid = await screen.findByRole('grid', { name: 'Basket members' });
  await waitFor(() => {
    expect(grid).toHaveAttribute('aria-busy', 'false');
  });
  return grid;
}

/** The symbols in the members grid, in order (the symbol is the cell after the grip). */
export function symbolsShown(): string[] {
  const grid = screen.getByRole('grid', { name: 'Basket members' });
  return within(grid)
    .getAllByRole('row')
    .slice(1)
    .map(
      (row) =>
        within(row)
          .getAllByRole('gridcell')
          .find((cell) => cell.textContent.startsWith('NSE:'))?.textContent ?? '',
    );
}

export const basketCard = (name: string) => screen.getByRole('button', { name: new RegExp(`^${name}`) });
