import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { RequestHandler } from 'msw';
import { renderRoutes } from '@/test/render';
import { server } from '@/test/server';
import { createServicesFake, type ServicesFake } from '@/test/servicesFake';
import { ServicesPage } from './components/ServicesPage';

/** Renders the Services screen at `/services` over the stateful services fake. */
export function renderServices({
  fake = createServicesFake(),
  handlers = [],
}: {
  fake?: ServicesFake;
  /** Handlers that win over the fake's (a read that never answers). */
  handlers?: RequestHandler[];
} = {}) {
  server.use(...handlers, ...fake.handlers);
  const result = renderRoutes(
    [
      { path: '/services', element: <ServicesPage /> },
      { path: '/accounts', element: <p>Accounts screen</p> },
    ],
    { initialPath: '/services' },
  );
  return { ...result, fake };
}

/** The services table (a grid) once the list is loaded. */
export const servicesGrid = () => screen.findByRole('grid', { name: 'Services' });

/** The row of a service, found by its name. */
export async function serviceRow(name: string) {
  const grid = await servicesGrid();
  const row = within(grid)
    .getAllByRole('row')
    .find((candidate) => within(candidate).queryByText(name) !== null);
  if (!row) throw new Error(`No row for ${name}`);
  return row;
}

/** Opens Configure for a service and returns the dialog. */
export async function openConfigure(name: string) {
  await userEvent.click(await screen.findByRole('button', { name: `Configure: ${name}` }));
  return screen.findByRole('dialog', { name: `Configure — ${name}` });
}

/** The latest phone-sized screen: every media query matches (the theme's `down('md')`). */
export function stubPhone() {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: true,
    media: query,
    onchange: null,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
    addListener: () => undefined,
    removeListener: () => undefined,
    dispatchEvent: () => false,
  }));
}
