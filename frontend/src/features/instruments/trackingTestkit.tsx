import { screen, within } from '@testing-library/react';
import type { InstrumentSummary } from '@/shared/api';
import { buildInstrument, sampleInstruments, trackedInfo } from '@/test/fixtures';
import { server } from '@/test/server';
import { createTrackingFake } from '@/test/trackingFake';
import { renderInstruments } from './testing';

/** The sample list with TCS tracked (with some history) and a tracked instrument that has retired. */
export const trackedSample: InstrumentSummary[] = [
  ...sampleInstruments.map((item) =>
    item.symbol === 'NSE:TCS'
      ? {
          ...item,
          tracked: trackedInfo({ dataFrom: '2021-01-01', dataTo: '2026-10-09', lastUpdate: '2026-10-09T10:30:00Z' }),
        }
      : item,
  ),
  buildInstrument({
    id: '00000000-0000-4000-8000-000000000299',
    symbol: 'NSE:GONECO',
    name: 'GONE CO LTD',
    displayName: 'Gone Co',
    isin: 'INE000G01010',
    retired: true,
    tracked: trackedInfo({ retiredButTracked: true }),
  }),
];

/** `count` made-up shares, so lists need several pages. */
export const manyInstruments = (count: number): InstrumentSummary[] =>
  Array.from({ length: count }, (_, n) =>
    buildInstrument({
      id: `00000000-0000-4000-8000-00000000${String(5000 + n)}`,
      symbol: `NSE:M${String(n).padStart(4, '0')}`,
      displayName: `Many ${String(n)}`,
      isin: null,
    }),
  );

/** Renders the screen over the stateful tracking fake. */
export function renderTracking(items: readonly InstrumentSummary[] = trackedSample, search = '') {
  const fake = createTrackingFake(items);
  server.use(...fake.handlers);
  return { fake, ...renderInstruments(search) };
}

export const gridOf = (name = 'Instruments') => screen.findByRole('grid', { name });

/** The symbols of the rows shown (the second cell: the first is the checkbox). */
export const symbolsShown = (name = 'Instruments') =>
  within(screen.getByRole('grid', { name }))
    .getAllByRole('row')
    .slice(1)
    .map((row) => within(row).getAllByRole('gridcell')[1]?.textContent);
