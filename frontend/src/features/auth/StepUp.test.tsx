import { useMutation } from '@tanstack/react-query';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { api, ensureOk } from '@/shared/api';
import { renderWithProviders } from '@/test/render';
import { createStepUpGate, problemResponse, server } from '@/test/server';
import { sessionQueryKey, useSession, type SessionState } from './api/sessionQuery';
import { useStepUp } from './hooks/useStepUp';
import { isStepUpCancelled } from './lib/stepUpErrors';

const profileId = '00000000-0000-4000-8000-0000000000a9';

/** A stand-in for any screen that makes a sensitive change. */
function SensitiveAction() {
  const { withStepUp } = useStepUp();
  const session = useSession(); // loads the session into the cache, as every real screen does
  const action = useMutation({
    mutationFn: withStepUp(async () => {
      ensureOk(await api.DELETE('/api/risk-profiles/{id}', { params: { path: { id: profileId } } }));
    }),
  });
  if (!session.data) {
    return null;
  }
  return (
    <>
      <button
        type="button"
        onClick={() => {
          action.mutate();
        }}
      >
        Do it
      </button>
      {action.isSuccess && <p>Done</p>}
      {action.isError && <p>{isStepUpCancelled(action.error) ? 'Cancelled' : `Failed: ${action.error.message}`}</p>}
    </>
  );
}

function setup() {
  const gate = createStepUpGate();
  let deleteCalls = 0;
  server.use(
    gate.handler,
    http.delete('/api/risk-profiles/:id', () => {
      deleteCalls += 1;
      return gate.require() ?? new HttpResponse(null, { status: 204 });
    }),
  );
  const view = renderWithProviders(<SensitiveAction />);
  return { gate, view, deleteCalls: () => deleteCalls };
}

describe('step-up (password re-entry)', () => {
  it('asks for the password on 403, then retries the action once and succeeds', async () => {
    const { gate, view, deleteCalls } = setup();

    await userEvent.click(await screen.findByRole('button', { name: 'Do it' }));
    const dialog = await screen.findByRole('dialog', { name: 'Confirm your password' });
    await userEvent.type(within(dialog).getByLabelText(/Password/), 'correct-password');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Confirm' }));

    expect(await screen.findByText('Done')).toBeInTheDocument();
    expect(deleteCalls()).toBe(2); // first attempt (403) + one retry
    expect(gate.stepUpCalls).toBe(1);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    // The elevation is remembered in the cached session (and nowhere else).
    const session = view.client.getQueryData<SessionState>(sessionQueryKey);
    expect(session?.status === 'authenticated' && session.session.elevatedUntil).toBe('2026-10-10T12:05:00Z');
  });

  it('does not ask again while the session is elevated', async () => {
    const { gate, deleteCalls } = setup();
    gate.elevated = true;

    await userEvent.click(await screen.findByRole('button', { name: 'Do it' }));

    expect(await screen.findByText('Done')).toBeInTheDocument();
    expect(deleteCalls()).toBe(1);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('keeps the dialog open with a message on a wrong password, and the session stays signed in', async () => {
    const { view, deleteCalls } = setup();

    await userEvent.click(await screen.findByRole('button', { name: 'Do it' }));
    const dialog = await screen.findByRole('dialog', { name: 'Confirm your password' });
    await userEvent.type(within(dialog).getByLabelText(/Password/), 'wrong-password');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Confirm' }));

    expect(await within(dialog).findByText('The password is incorrect.')).toBeInTheDocument();
    expect(within(dialog).getByLabelText(/Password/)).toHaveValue(''); // a rejected password is not kept
    expect(deleteCalls()).toBe(1);
    expect(view.client.getQueryData<SessionState>(sessionQueryKey)?.status).toBe('authenticated');

    // A second try with the right password completes the original action.
    await userEvent.type(within(dialog).getByLabelText(/Password/), 'correct-password');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Confirm' }));
    expect(await screen.findByText('Done')).toBeInTheDocument();
  });

  it('asks for the password before sending an empty one', async () => {
    const { gate } = setup();

    await userEvent.click(await screen.findByRole('button', { name: 'Do it' }));
    const dialog = await screen.findByRole('dialog', { name: 'Confirm your password' });
    await waitFor(() => {
      expect(within(dialog).getByLabelText(/Password/)).toHaveFocus();
    });
    await userEvent.click(within(dialog).getByRole('button', { name: 'Confirm' }));

    expect(await within(dialog).findByText('Enter your password.')).toBeInTheDocument();
    expect(gate.stepUpCalls).toBe(0);
  });

  it('rejects cleanly when the dialog is cancelled and does not repeat the action', async () => {
    const { deleteCalls } = setup();

    await userEvent.click(await screen.findByRole('button', { name: 'Do it' }));
    const dialog = await screen.findByRole('dialog', { name: 'Confirm your password' });
    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));

    expect(await screen.findByText('Cancelled')).toBeInTheDocument();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(deleteCalls()).toBe(1);
  });

  it('shows a countdown when too many wrong passwords locked the account', async () => {
    const { gate } = setup();
    server.use(
      http.post('/api/auth/step-up', () => {
        gate.stepUpCalls += 1;
        return problemResponse({ title: 'Locked', code: 'auth.locked', status: 423, retryAfterSeconds: 90 }, 423);
      }),
    );

    await userEvent.click(await screen.findByRole('button', { name: 'Do it' }));
    const dialog = await screen.findByRole('dialog', { name: 'Confirm your password' });
    await userEvent.type(within(dialog).getByLabelText(/Password/), 'anything');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Confirm' }));

    expect(await within(dialog).findByText(/Too many failed attempts/)).toBeInTheDocument();
    expect(within(dialog).getByText('1:30')).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Confirm' })).toBeDisabled();
  });

  it('passes other errors through without a dialog', async () => {
    server.use(
      http.delete('/api/risk-profiles/:id', () => problemResponse({ title: 'In use', code: 'accounts.conflict' }, 409)),
    );
    renderWithProviders(<SensitiveAction />);

    await userEvent.click(await screen.findByRole('button', { name: 'Do it' }));

    expect(await screen.findByText('Failed: In use')).toBeInTheDocument();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });
});
