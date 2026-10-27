import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { pinClock } from '@/test/pinClock';
import { renderRoutes } from '@/test/render';
import { problemResponse, server } from '@/test/server';
import { AssistantPage } from './components/AssistantPage';
import { mountEmptyHistory, mountStatus, statusBuilder } from './testing';

pinClock('2026-10-27T09:00:00Z');

const open = () => renderRoutes([{ path: '/assistant', element: <AssistantPage /> }], { initialPath: '/assistant' });

let held: () => string[] = () => [];

async function openDialog() {
  const user = userEvent.setup();
  const { client } = open();
  // The passwords still held anywhere in the mutation cache.
  held = () =>
    client
      .getMutationCache()
      .getAll()
      .map((m) => JSON.stringify(m.state.variables ?? null));
  const settings = await screen.findByRole('button', { name: 'Settings' });
  await waitFor(() => {
    expect(settings).toBeEnabled();
  });
  await user.click(settings);
  await screen.findByRole('dialog', { name: 'Assistant settings' });
  return user;
}

const bodies: Record<string, unknown>[] = [];
const mountPut = (
  answer: () => Response = () => HttpResponse.json(statusBuilder({ portfolioContext: true, settingsId: 8 })),
) => {
  bodies.length = 0;
  server.use(
    http.put('/api/assistant/settings', async ({ request }) => {
      bodies.push((await request.json()) as Record<string, unknown>);
      return answer();
    }),
  );
};

beforeEach(() => {
  mountStatus(statusBuilder());
  mountEmptyHistory();
});

describe('settings dialog (owner act)', () => {
  it('turning off needs no password, says so, and sends none', async () => {
    mountPut(() => HttpResponse.json(statusBuilder({ enabled: false, settingsId: 8 })));
    const user = await openDialog();
    await user.click(screen.getByRole('switch', { name: 'Assistant' }));
    expect(screen.getByText('No password needed to turn off')).toBeInTheDocument();
    expect(screen.queryByLabelText(/Your password/)).not.toBeInTheDocument();
    await user.type(screen.getByLabelText(/Reason/), 'not needed this week');
    await user.click(screen.getByRole('button', { name: 'Save' }));
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(bodies).toHaveLength(1);
    expect(bodies[0]).toMatchObject({
      enabled: false,
      portfolioContext: false,
      maxToolCalls: 6,
      expectedSettingsId: 7,
      reason: 'not needed this week',
    });
    expect(bodies[0]?.password ?? null).toBeNull();
    expect(await screen.findByText('Settings saved')).toBeInTheDocument();
  });

  it('lowering the budget needs no password either', async () => {
    mountPut();
    const user = await openDialog();
    const calls = screen.getByLabelText('Maximum tool calls');
    await user.clear(calls);
    await user.type(calls, '3');
    expect(screen.queryByLabelText(/Your password/)).not.toBeInTheDocument();
  });

  it('shows what portfolio data off means', async () => {
    await openDialog();
    expect(screen.getByText("Off: portfolio questions answer 'not available'")).toBeInTheDocument();
  });

  it('raising exposure asks for the password, sends it once, and clears it after a wrong try', async () => {
    mountPut();
    server.use(
      http.put('/api/assistant/settings', async ({ request }) => {
        bodies.push((await request.json()) as Record<string, unknown>);
        return bodies.length === 1
          ? problemResponse({ title: 'Wrong', code: 'auth.invalid_credentials' }, 401)
          : HttpResponse.json(statusBuilder({ portfolioContext: true, settingsId: 8 }));
      }),
    );
    const user = await openDialog();
    await user.click(screen.getByRole('switch', { name: 'Portfolio data' }));
    await user.type(screen.getByLabelText(/Reason/), 'portfolio questions');
    const save = screen.getByRole('button', { name: 'Save' });
    expect(save).toBeDisabled();
    await user.type(screen.getByLabelText(/Your password/), 'wrong-pass');
    await user.click(save);
    expect(await screen.findByText('The password was not accepted. Nothing was changed.')).toBeInTheDocument();
    expect(screen.getByRole('dialog', { name: 'Assistant settings' })).toBeInTheDocument();
    expect(screen.getByLabelText(/Your password/)).toHaveValue('');
    expect(held().join()).not.toContain('wrong-pass');
    expect(bodies[0]).toMatchObject({ password: 'wrong-pass', portfolioContext: true });
    // A wrong password is an answer, not an ended session: the owner is still on the page.
    expect(screen.getByRole('button', { name: 'Settings', hidden: true })).toBeInTheDocument();

    await user.type(screen.getByLabelText(/Your password/), 'right-pass');
    await user.click(screen.getByRole('button', { name: 'Save' }));
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(bodies[1]).toMatchObject({ password: 'right-pass' });
    expect(held().join()).not.toContain('right-pass');
  });

  it('shows the lock and the retry seconds on 423', async () => {
    mountPut(() => problemResponse({ title: 'Locked', code: 'auth.locked', retryAfterSeconds: 90 }, 423));
    const user = await openDialog();
    await user.click(screen.getByRole('switch', { name: 'Portfolio data' }));
    await user.type(screen.getByLabelText(/Reason/), 'r');
    await user.type(screen.getByLabelText(/Your password/), 'pw');
    await user.click(screen.getByRole('button', { name: 'Save' }));
    expect(await screen.findByText(/this is locked for now\. Try again in 90 seconds/)).toBeInTheDocument();
    expect(screen.getByLabelText(/Your password/)).toHaveValue('');
  });

  it('on a stale form (409) says what happened and shows the current values', async () => {
    mountPut(() => problemResponse({ title: 'Stale', code: 'assistant.stale' }, 409));
    const user = await openDialog();
    await user.click(screen.getByRole('switch', { name: 'Assistant' }));
    await user.type(screen.getByLabelText(/Reason/), 'r');
    mountStatus(statusBuilder({ enabled: true, maxToolCalls: 9, settingsId: 9 }));
    await user.click(screen.getByRole('button', { name: 'Save' }));
    expect(await screen.findByText(/changed elsewhere/)).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.getByLabelText('Maximum tool calls')).toHaveValue(9);
    });
    expect(screen.getByRole('switch', { name: 'Assistant' })).toBeChecked();
  });

  it('keeps the maximum between 1 and 12', async () => {
    const user = await openDialog();
    const calls = screen.getByLabelText('Maximum tool calls');
    await user.clear(calls);
    await user.type(calls, '13');
    await user.type(screen.getByLabelText(/Reason/), 'r');
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled();
  });
});
