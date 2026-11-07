import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { buildSettings, mountReadiness, type ReadinessFake } from '@/test/readinessFixtures';
import { server } from '@/test/server';
import { renderReadiness } from './testing';

afterEach(() => {
  server.events.removeAllListeners();
});

async function openDialog(button: string) {
  await userEvent.click(await screen.findByRole('button', { name: button }));
  return screen.findByRole('dialog');
}

async function fill(dialog: HTMLElement, password = 'correct-horse') {
  await userEvent.type(within(dialog).getByLabelText(/Reason/), 'ready to refuse');
  await userEvent.type(within(dialog).getByLabelText(/Password/), password);
}

const enforce = () => screen.findByRole('region', { name: 'Enforce switch' });

describe('Enforce switch', () => {
  it('reads OFF by default, says what OFF and ON mean and sends nothing until confirmed', async () => {
    const seen = mountReadiness();
    renderReadiness();
    const card = await enforce();
    expect(await within(card).findByText('OFF')).toBeInTheDocument();
    expect(within(card).getByText(/None stored: it reads OFF/)).toBeInTheDocument();
    expect(within(card).getByText(/the move goes ahead with a warning/)).toBeInTheDocument();
    expect(
      within(card).getByText(/Candidate to Paper, Paper to Active and Paused to Paper are not refused/),
    ).toBeInTheDocument();

    const dialog = await openDialog('Turn enforce ON');
    expect(within(dialog).getByText(/409 confidence\.gate_failed/)).toBeInTheDocument();
    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));
    expect(seen.puts).toEqual([]);
  });

  it('needs a reason and a password to turn it ON, sends them once and then reads ON', async () => {
    const seen = mountReadiness();
    const { client } = renderReadiness();
    const dialog = await openDialog('Turn enforce ON');
    const confirm = within(dialog).getByRole('button', { name: 'Turn ON' });
    expect(confirm).toBeDisabled();
    await userEvent.type(within(dialog).getByLabelText(/Reason/), 'ready to refuse');
    expect(confirm).toBeDisabled();
    await userEvent.type(within(dialog).getByLabelText(/Password/), 'correct-horse');
    await userEvent.click(confirm);

    expect(await screen.findByText('Enforce is now ON.')).toBeInTheDocument();
    expect(seen.puts).toEqual([{ enforce: true, reason: 'ready to refuse', password: 'correct-horse' }]);
    const card = await enforce();
    expect(
      await within(card).findByText(/refuses the move it judges with 409 confidence\.gate_failed/),
    ).toBeInTheDocument();
    expect(await within(card).findByRole('button', { name: 'Turn enforce OFF' })).toBeInTheDocument();
    expect(document.body.innerHTML).not.toContain('correct-horse');
    expect(client.getMutationCache().getAll()).toHaveLength(0);
  });

  it('needs a fresh password to turn it OFF as well', async () => {
    const seen = mountReadiness({ settings: buildSettings({ enforce: true, switchId: 5 }) });
    renderReadiness();
    const dialog = await openDialog('Turn enforce OFF');
    const confirm = within(dialog).getByRole('button', { name: 'Turn OFF' });
    await userEvent.type(within(dialog).getByLabelText(/Reason/), 'too early');
    expect(confirm).toBeDisabled();
    await userEvent.type(within(dialog).getByLabelText(/Password/), 'correct-horse');
    await userEvent.click(confirm);

    expect(await screen.findByText('Enforce is now OFF.')).toBeInTheDocument();
    expect(seen.puts).toEqual([{ enforce: false, reason: 'too early', password: 'correct-horse' }]);
    expect(await within(await enforce()).findByRole('button', { name: 'Turn enforce ON' })).toBeInTheDocument();
  });

  it.each<[NonNullable<ReadinessFake['put']>, string]>([
    ['wrong-password', 'The password is wrong.'],
    ['step-up', 'A fresh password is required.'],
    ['invalid', 'The reason is not valid.'],
  ])('shows the refusal %s in the dialog and changes nothing', async (put, words) => {
    const seen = mountReadiness({ put });
    renderReadiness();
    const first = await openDialog('Turn enforce ON');
    await fill(first, 'bad');
    await userEvent.click(within(first).getByRole('button', { name: 'Turn ON' }));

    // The dialog is remounted after a refusal (its password is dropped), so it is looked up again.
    expect(await screen.findByText(words)).toBeInTheDocument();
    const dialog = screen.getByRole('dialog');
    expect(seen.puts).toHaveLength(1);
    expect(screen.queryByText('Enforce is now ON.')).toBeNull();
    // A wrong password is an answer, not a sign-out: the screen and its switch are still there.
    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(within(await enforce()).getByText('OFF')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Readiness' })).toBeInTheDocument();
  });

  it('keeps the dialog open after a 401 with the refusal shown, an empty password field and no password in the cache', async () => {
    const seen = mountReadiness({ put: 'wrong-password' });
    const { client } = renderReadiness();
    const first = await openDialog('Turn enforce ON');
    await fill(first, 'wrong-horse');
    await userEvent.click(within(first).getByRole('button', { name: 'Turn ON' }));

    expect(await screen.findByText('The password is wrong.')).toBeInTheDocument();
    const dialog = screen.getByRole('dialog', { name: 'Turn enforce ON' });
    expect(within(dialog).getByLabelText(/Password/)).toHaveValue('');
    expect(seen.puts).toEqual([{ enforce: true, reason: 'ready to refuse', password: 'wrong-horse' }]);
    const variables = client
      .getMutationCache()
      .getAll()
      .map((mutation) => JSON.stringify(mutation.state.variables ?? null));
    expect(variables.join('')).not.toContain('wrong-horse');
    expect(document.body.innerHTML).not.toContain('wrong-horse');
  });

  it('clears the typed fields when the dialog is closed and opened again', async () => {
    mountReadiness();
    renderReadiness();
    let dialog = await openDialog('Turn enforce ON');
    await fill(dialog);
    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    dialog = await openDialog('Turn enforce ON');
    expect(within(dialog).getByLabelText(/Password/)).toHaveValue('');
    expect(within(dialog).getByLabelText(/Reason/)).toHaveValue('');
  });
});
