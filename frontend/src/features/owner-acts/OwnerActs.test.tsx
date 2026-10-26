import { renderHook, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { createCapabilitiesFake } from '@/test/capabilitiesFake';
import { renderRoutes } from '@/test/render';
import { server } from '@/test/server';
import { useCapability } from '.';
import { OwnerActsPage } from './components/OwnerActsPage';

function renderPage(options: Parameters<typeof createCapabilitiesFake>[0] = {}) {
  const fake = createCapabilitiesFake(options);
  server.use(...fake.handlers);
  const view = renderRoutes([{ path: '/owner-acts', element: <OwnerActsPage /> }], { initialPath: '/owner-acts' });
  return { ...view, fake };
}

const card = (label: string) => screen.getByRole('form', { name: label });

async function fill(label: string, reason: string, password?: string) {
  const form = card(label);
  await userEvent.type(within(form).getByLabelText(/^Reason for/), reason);
  if (password !== undefined) await userEvent.type(within(form).getByLabelText(/^Your password/), password);
  return form;
}

describe('Owner-act switches', () => {
  it('lists all five off by default, with the words and no write control but Switch on', async () => {
    renderPage();

    expect(screen.getByRole('heading', { level: 1, name: 'Owner-act switches' })).toBeInTheDocument();
    await screen.findByText('Exports');
    expect(screen.getByText(/only allows an act/)).toBeInTheDocument();
    expect(screen.getAllByText('Off')).toHaveLength(5);
    expect(screen.getAllByText(/^Never switched/)).toHaveLength(5);
    const buttons = screen.getAllByRole('button');
    expect(buttons).toHaveLength(5);
    buttons.forEach((button) => {
      expect(button).toHaveAccessibleName(/^Switch on /);
      expect(button).toBeDisabled();
    });
    expect(screen.queryByRole('button', { name: /apply|activate|promote|publish/i })).toBeNull();
    expect(screen.queryByRole('link', { name: /apply|activate|promote|publish/i })).toBeNull();
    expect(screen.getByText('No switch yet.')).toBeInTheDocument();
  });

  it('switches on with the password, sends reason and password, and clears the password', async () => {
    const { fake } = renderPage();
    await screen.findByText('Exports');
    const form = await fill('Exports', 'I want to export my notes', 'correct-horse');
    expect(within(form).getByRole('button', { name: 'Switch on Exports' })).toBeEnabled();

    await userEvent.click(within(form).getByRole('button', { name: 'Switch on Exports' }));

    expect(await screen.findByRole('button', { name: 'Switch off Exports' })).toBeDisabled();
    expect(fake.state.bodies).toEqual([
      { enabled: true, reason: 'I want to export my notes', password: 'correct-horse' },
    ]);
    expect(within(card('Exports')).queryByLabelText(/^Your password/)).toBeNull();
    expect(screen.getByText(/by owner:example@invalid.test\. I want to export my notes/)).toBeInTheDocument();
  });

  it('keeps Switch on disabled until a reason and a password are typed', async () => {
    renderPage();
    await screen.findByText('Exports');
    const form = card('Exports');
    const button = within(form).getByRole('button', { name: 'Switch on Exports' });

    await userEvent.type(within(form).getByLabelText(/^Reason for/), 'why');
    expect(button).toBeDisabled();
    await userEvent.type(within(form).getByLabelText(/^Your password/), 'x');
    expect(button).toBeEnabled();
  });

  it('says a wrong password was not accepted, clears it and does not end the session', async () => {
    const { fake, router } = renderPage();
    await screen.findByText('Exports');
    const form = await fill('Exports', 'why', 'wrong');

    await userEvent.click(within(form).getByRole('button', { name: 'Switch on Exports' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('The password was not accepted. Nothing was changed.');
    await waitFor(() => {
      expect(within(card('Exports')).getByLabelText(/^Your password/)).toHaveValue('');
    });
    expect(fake.state.items.every((item) => !item.enabled)).toBe(true);
    expect(router.state.location.pathname).toBe('/owner-acts');
    expect(screen.getByRole('heading', { level: 1, name: 'Owner-act switches' })).toBeInTheDocument();
  });

  it('switches off with a reason only and sends no password key', async () => {
    const { fake } = renderPage();
    await screen.findByText('Exports');
    await userEvent.click(within(await fill('Exports', 'on first', 'pw')).getByRole('button', { name: /^Switch on/ }));
    const off = await screen.findByRole('button', { name: 'Switch off Exports' });
    expect(within(card('Exports')).queryByLabelText(/^Your password/)).toBeNull();
    expect(screen.getByText('On')).toBeInTheDocument();

    await userEvent.type(within(card('Exports')).getByLabelText(/^Reason for/), 'not needed');
    await userEvent.click(off);

    await waitFor(() => {
      expect(fake.state.bodies).toHaveLength(2);
    });
    expect(fake.state.bodies[1]).toEqual({ enabled: false, reason: 'not needed' });
    expect(Object.keys(fake.state.bodies[1] ?? {})).not.toContain('password');
    expect(await screen.findByRole('button', { name: 'Switch on Exports' })).toBeInTheDocument();
  });

  it.each([
    [422, 'capabilities.invalid', 'The reason is not valid.'],
    [429, 'auth.rate_limited', 'Too many tries. Wait and try again.'],
    [423, 'auth.locked', 'Sign-in is locked for a while.'],
  ])('shows the server words for a %s', async (status, code, title) => {
    renderPage({ refuse: { status, code, title } });
    await screen.findByText('Exports');
    const form = await fill('Exports', 'why', 'pw');

    await userEvent.click(within(form).getByRole('button', { name: 'Switch on Exports' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(title);
    expect(within(card('Exports')).getByLabelText(/^Your password/)).toHaveValue('');
  });

  it('has no control called apply, activate, promote or publish', async () => {
    renderPage();
    await screen.findByText('Exports');
    const named = screen
      .queryAllByRole('button')
      .concat(screen.queryAllByRole('link'), screen.queryAllByRole('textbox'), screen.queryAllByRole('checkbox'));
    named.forEach((element) => {
      expect(element).not.toHaveAccessibleName(/apply|activate|promote|publish/i);
    });
  });
});

describe('useCapability', () => {
  function wrapper({ children }: { children: ReactNode }) {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
  }

  it('is off while loading and for an unknown name, and follows the server once loaded', async () => {
    server.use(...createCapabilitiesFake().handlers);
    const known = renderHook(() => useCapability('exports'), { wrapper });
    const unknown = renderHook(() => useCapability('nope'), { wrapper });
    expect(known.result.current).toEqual({ enabled: false, loaded: false, failed: false });

    await waitFor(() => {
      expect(known.result.current.loaded).toBe(true);
    });
    await waitFor(() => {
      expect(unknown.result.current.loaded).toBe(true);
    });
    expect(known.result.current.enabled).toBe(false);
    expect(unknown.result.current.enabled).toBe(false);
  });

  it('reports a switched-on capability as enabled', async () => {
    const fake = createCapabilitiesFake();
    fake.state.items = fake.state.items.map((item) => (item.name === 'pwa' ? { ...item, enabled: true } : item));
    server.use(...fake.handlers);
    const { result } = renderHook(() => useCapability('pwa'), { wrapper });

    await waitFor(() => {
      expect(result.current).toEqual({ enabled: true, loaded: true, failed: false });
    });
  });
});
