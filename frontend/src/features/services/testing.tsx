import { act, screen, within } from '@testing-library/react';
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

/** The list of service cards once the services are loaded. */
export const servicesList = () => screen.findByRole('list', { name: 'Services' });

/** The card of a service, found by its name. */
export const serviceCard = (name: string) => screen.findByRole('listitem', { name });

/** Opens the "Actions for <service>" menu of a card and returns the menu. */
export async function openActions(name: string) {
  await userEvent.click(await screen.findByRole('button', { name: `Actions for ${name}` }));
  return screen.findByRole('menu', { name: `Actions for ${name}` });
}

/** Chooses an item of a service's actions menu (Configure, Pause, Resume, History, Settings…). */
export async function chooseAction(name: string, item: string | RegExp) {
  const menu = await openActions(name);
  await userEvent.click(within(menu).getByRole('menuitem', { name: item }));
}

/** Opens Configure for a service and returns the dialog. */
export async function openConfigure(name: string) {
  await chooseAction(name, 'Configure');
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

let clockNow = 0;

/**
 * Sets the clock the screens read (`Date.now()`) to a moment, without faking timers: the page keeps its real timers
 * and requests, so the network mocks behave as in every other test. Undone by `vi.restoreAllMocks()`.
 */
export function setNow(iso: string) {
  clockNow = Date.parse(iso);
  vi.spyOn(Date, 'now').mockImplementation(() => clockNow);
}

/** Lets minutes pass on the clock set by `setNow`: the shared clock re-reads it as when a hidden tab is shown again. */
export function passMinutes(minutes: number) {
  clockNow += minutes * 60_000;
  act(() => {
    document.dispatchEvent(new Event('visibilitychange'));
  });
}
