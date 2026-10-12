import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { RequestHandler } from 'msw';
import { createDataFake, type DataFake } from '@/test/dataFake';
import { renderRoutes } from '@/test/render';
import { server } from '@/test/server';
import { DataPage } from './components/DataPage';

/** Renders the Data & import screen at `/data` + `search` (e.g. "?status=behind") over the stateful data fake. */
export function renderData({
  search = '',
  fake = createDataFake(),
  handlers = [],
}: {
  search?: string;
  fake?: DataFake;
  /** Handlers that win over the fake's (a read that never answers). */
  handlers?: RequestHandler[];
} = {}) {
  server.use(...handlers, ...fake.handlers);
  const result = renderRoutes(
    [
      { path: '/data', element: <DataPage /> },
      { path: '/accounts', element: <p>Accounts screen</p> },
    ],
    { initialPath: `/data${search}` },
  );
  return { ...result, fake };
}

/** The Data status ribbon, which is on the page once the overview is loaded. */
export const ready = () => screen.findByRole('region', { name: 'Data status' });

/** Opens a tab of the screen (the tabs carry counts in their names, so the name is a prefix). */
export async function openTab(name: string) {
  await userEvent.click(await screen.findByRole('tab', { name: new RegExp(`^${name}`) }));
}

/** Opens the details popover of a pipeline step and returns it. */
export async function openStep(title: 'CSV files' | 'Dhan Data API' | 'Aggregates') {
  const strip = await screen.findByRole('region', { name: 'Pipeline' });
  await userEvent.click(within(strip).getByRole('button', { name: new RegExp(`^\\d\\. ${title}`) }));
  return screen.findByRole('dialog', { name: `${title} details` });
}

/** Opens the "Review CSV files" dialog from the CSV step's popover and returns it. */
export async function openCsvReview() {
  const details = await openStep('CSV files');
  await userEvent.click(within(details).getByRole('button', { name: /^Review files/ }));
  return screen.findByRole('dialog', { name: 'Review CSV files' });
}
