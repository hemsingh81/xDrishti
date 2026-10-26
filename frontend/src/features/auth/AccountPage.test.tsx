import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { buildAuditEntry, buildOwnerAccount, createAccountFake } from '@/test/accountFake';
import { pendingResponse } from '@/test/gate';
import { renderRoutes } from '@/test/render';
import { problemResponse, server } from '@/test/server';
import { sessionQueryKey, type SessionState } from './api/sessionQuery';
import { AccountPage } from './components/AccountPage';
import { UserMenu } from './index';

function renderPage(options: Parameters<typeof createAccountFake>[0] = {}) {
  const fake = createAccountFake(options);
  server.use(...fake.handlers);
  const view = renderRoutes([
    {
      path: '/',
      element: (
        <>
          <UserMenu />
          <AccountPage />
        </>
      ),
    },
  ]);
  return { fake, ...view };
}

const profile = () => screen.getByRole('form', { name: 'Profile' });
const passwordForm = () => screen.getByRole('form', { name: 'Change password' });

describe('My account', () => {
  it('has a page header and the three cards', async () => {
    renderPage();

    expect(await screen.findByRole('heading', { level: 1, name: 'My account' })).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Profile' })).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Change password' })).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Recent sign-in activity' })).toBeInTheDocument();
  });
});

describe('My account: profile', () => {
  it('shows the name and e-mail, and says there is nothing to save when Save is pressed unchanged', async () => {
    renderPage();

    expect(await screen.findByLabelText('Display name')).toHaveValue('Test Owner');
    expect(within(profile()).getByLabelText('E-mail')).toHaveValue('example@invalid.test');
    expect(within(profile()).getByLabelText('E-mail')).toHaveAttribute('autocomplete', 'username');
    expect(screen.getByRole('button', { name: 'Save profile' })).toBeEnabled();

    await userEvent.click(screen.getByRole('button', { name: 'Save profile' }));
    expect(screen.getByRole('status')).toHaveTextContent('No changes to save');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();

    await userEvent.type(screen.getByLabelText('Display name'), ' Jr');
    expect(screen.queryByText('No changes to save')).not.toBeInTheDocument();
  });

  it('asks for the password, saves, shows a toast and updates the name in the top-bar menu', async () => {
    const { fake } = renderPage();
    const name = await screen.findByLabelText('Display name');
    expect(await screen.findByRole('button', { name: 'Account menu, Test Owner' })).toBeInTheDocument();

    await userEvent.clear(name);
    await userEvent.type(name, 'Asha Rao');
    await userEvent.clear(screen.getByLabelText('E-mail'));
    await userEvent.type(screen.getByLabelText('E-mail'), 'Asha@Example.test');
    await userEvent.click(screen.getByRole('button', { name: 'Save profile' }));

    const dialog = await screen.findByRole('dialog', { name: 'Confirm your password' });
    await userEvent.type(within(dialog).getByLabelText(/Password/), 'correct-password');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Confirm' }));

    expect(await screen.findByText('Profile saved')).toBeInTheDocument();
    expect(fake.profileRequests).toEqual([{ displayName: 'Asha Rao', email: 'Asha@Example.test' }]);
    // The form shows what was stored (the e-mail is lower-cased) and the avatar menu follows the session.
    expect(screen.getByLabelText('E-mail')).toHaveValue('asha@example.test');
    expect(await screen.findByRole('button', { name: 'Account menu, Asha Rao' })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Save profile' }));
    expect(screen.getByText('No changes to save')).toBeInTheDocument();
    expect(fake.profileRequests).toHaveLength(1);
  });

  it('does not ask again while the session is still elevated', async () => {
    const { fake } = renderPage();
    fake.gate.elevated = true;

    await userEvent.type(await screen.findByLabelText('Display name'), 'x');
    await userEvent.click(screen.getByRole('button', { name: 'Save profile' }));

    expect(await screen.findByText('Profile saved')).toBeInTheDocument();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('saves nothing and shows no error when the password dialog is cancelled', async () => {
    const { fake } = renderPage();

    await userEvent.type(await screen.findByLabelText('Display name'), 'x');
    await userEvent.click(screen.getByRole('button', { name: 'Save profile' }));
    const dialog = await screen.findByRole('dialog', { name: 'Confirm your password' });
    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));

    await within(profile()).findByRole('button', { name: 'Save profile' });
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(screen.queryByText('Profile saved')).not.toBeInTheDocument();
    expect(fake.profileRequests).toHaveLength(0);
  });

  it('checks the fields before sending anything', async () => {
    const { fake } = renderPage();
    const name = await screen.findByLabelText('Display name');

    await userEvent.clear(name);
    await userEvent.type(screen.getByLabelText('E-mail'), '{Backspace}');
    await userEvent.click(screen.getByRole('button', { name: 'Save profile' }));

    expect(await screen.findByText('Enter your name.')).toBeInTheDocument();
    expect(name).toHaveFocus();
    expect(name).toBeInvalid();
    expect(fake.profileRequests).toHaveLength(0);

    await userEvent.type(name, 'Asha');
    const email = screen.getByLabelText('E-mail');
    await userEvent.clear(email);
    await userEvent.type(email, 'not-an-address');
    await userEvent.click(screen.getByRole('button', { name: 'Save profile' }));

    expect(await screen.findByText('Enter a valid e-mail address.')).toBeInTheDocument();
    expect(email).toHaveFocus();
    expect(screen.queryByText('Enter your name.')).not.toBeInTheDocument();
    expect(fake.gate.stepUpCalls).toBe(0);
  });

  it('shows the field messages of the API next to the fields', async () => {
    const { fake } = renderPage();
    fake.gate.elevated = true;
    server.use(
      http.put('/api/auth/profile', () =>
        problemResponse(
          {
            title: 'The e-mail is not valid.',
            code: 'auth.owner_email_invalid',
            status: 400,
            errors: { email: ['That e-mail is not accepted.'] },
          },
          400,
        ),
      ),
    );

    await userEvent.type(await screen.findByLabelText('Display name'), 'x');
    await userEvent.click(screen.getByRole('button', { name: 'Save profile' }));

    const email = screen.getByLabelText('E-mail');
    expect(await screen.findByText('That e-mail is not accepted.')).toBeInTheDocument();
    expect(email).toBeInvalid();
    expect(email).toHaveFocus();
  });

  it('shows the name message of the API and a general failure as an alert', async () => {
    const { fake } = renderPage();
    fake.gate.elevated = true;
    server.use(
      http.put('/api/auth/profile', () =>
        problemResponse(
          { title: 'Bad name', code: 'auth.owner_name_invalid', status: 400, errors: { displayName: ['Too long.'] } },
          400,
        ),
      ),
    );
    await userEvent.type(await screen.findByLabelText('Display name'), 'x');
    await userEvent.click(screen.getByRole('button', { name: 'Save profile' }));
    expect(await screen.findByText('Too long.')).toBeInTheDocument();

    server.use(http.put('/api/auth/profile', () => problemResponse({ title: 'Try later', status: 503 }, 503)));
    await userEvent.type(screen.getByLabelText('Display name'), 'y');
    await userEvent.click(screen.getByRole('button', { name: 'Save profile' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Try later');
  });

  it('shows a loading state, then an error with Retry when the profile cannot be read', async () => {
    const fake = createAccountFake();
    server.use(
      http.get('/api/auth/account', () => pendingResponse()),
      ...fake.handlers,
    );
    renderRoutes([{ path: '/', element: <AccountPage /> }]);
    expect(await screen.findByText('Loading your profile')).toBeInTheDocument();
  });

  it('shows an error with Retry when the profile cannot be read', async () => {
    const fake = createAccountFake();
    let calls = 0;
    server.use(
      http.get('/api/auth/account', () => {
        calls += 1;
        return calls === 1
          ? problemResponse({ title: 'Database down', status: 500 }, 500)
          : HttpResponse.json(buildOwnerAccount());
      }),
      ...fake.handlers,
    );
    renderRoutes([{ path: '/', element: <AccountPage /> }]);

    const card = await screen.findByRole('region', { name: 'Profile' });
    expect(await within(card).findByText('Database down')).toBeInTheDocument();
    await userEvent.click(within(card).getByRole('button', { name: 'Retry' }));

    expect(await screen.findByLabelText('Display name')).toHaveValue('Test Owner');
  });
});

describe('My account: change password', () => {
  const fill = async (current: string, next: string, confirm = next) => {
    const form = passwordForm();
    if (current) await userEvent.type(within(form).getByLabelText('Current password'), current);
    if (next) await userEvent.type(within(form).getByLabelText('New password'), next);
    if (confirm) await userEvent.type(within(form).getByLabelText('Confirm new password'), confirm);
  };
  const submit = () => userEvent.click(within(passwordForm()).getByRole('button', { name: 'Change password' }));

  it('labels the fields, hints the length and sets the autocomplete hints for password managers', async () => {
    renderPage();
    await screen.findByRole('region', { name: 'Change password' });
    const form = passwordForm();

    expect(within(form).getByLabelText('Current password')).toHaveAttribute('autocomplete', 'current-password');
    expect(within(form).getByLabelText('New password')).toHaveAttribute('autocomplete', 'new-password');
    expect(within(form).getByLabelText('Confirm new password')).toHaveAttribute('autocomplete', 'new-password');
    expect(within(form).getByText('At least 10 characters')).toBeInTheDocument();
    expect(await screen.findByText(/Last changed 1 Oct 2026/)).toBeInTheDocument();
  });

  it('shows and hides each password with a button named after its field', async () => {
    renderPage();
    await screen.findByRole('region', { name: 'Change password' });
    const form = passwordForm();
    const field = within(form).getByLabelText('New password');
    expect(field).toHaveAttribute('type', 'password');

    await userEvent.click(within(form).getByRole('button', { name: 'Show new password' }));
    expect(field).toHaveAttribute('type', 'text');
    expect(within(form).getByLabelText('Current password')).toHaveAttribute('type', 'password');

    await userEvent.click(within(form).getByRole('button', { name: 'Hide new password' }));
    expect(field).toHaveAttribute('type', 'password');
    expect(within(form).getByRole('button', { name: 'Show current password' })).toBeInTheDocument();
    expect(within(form).getByRole('button', { name: 'Show confirm new password' })).toBeInTheDocument();
  });

  it('points out a mismatch while typing and a missing or short password on submit, without sending anything', async () => {
    const { fake } = renderPage();
    await screen.findByRole('region', { name: 'Change password' });

    await fill('', 'long-enough-1', 'long-enough-');
    expect(await screen.findByText('The passwords do not match.')).toBeInTheDocument();
    expect(within(passwordForm()).getByLabelText('Confirm new password')).toBeInvalid();

    await submit();
    expect(screen.getByText('Enter your current password.')).toBeInTheDocument();
    expect(within(passwordForm()).getByLabelText('Current password')).toHaveFocus();

    await userEvent.type(within(passwordForm()).getByLabelText('Current password'), 'correct-password');
    await userEvent.clear(within(passwordForm()).getByLabelText('New password'));
    await userEvent.clear(within(passwordForm()).getByLabelText('Confirm new password'));
    await userEvent.type(within(passwordForm()).getByLabelText('New password'), 'short');
    await userEvent.type(within(passwordForm()).getByLabelText('Confirm new password'), 'short');
    await submit();
    expect(screen.getByText('Use at least 10 characters.')).toBeInTheDocument();
    expect(within(passwordForm()).getByLabelText('New password')).toHaveFocus();
    expect(fake.passwordRequests).toHaveLength(0);
  });

  it('changes the password, tells the owner, and clears the fields; the session stays signed in', async () => {
    const { fake, client } = renderPage();
    await screen.findByRole('region', { name: 'Change password' });

    await fill('correct-password', 'a-brand-new-password');
    await userEvent.click(within(passwordForm()).getByRole('button', { name: 'Show new password' }));
    await submit();

    expect(await screen.findByText('Password changed — other devices were signed out')).toBeInTheDocument();
    expect(fake.passwordRequests).toEqual([
      { currentPassword: 'correct-password', newPassword: 'a-brand-new-password' },
    ]);
    const form = passwordForm();
    expect(within(form).getByLabelText('Current password')).toHaveValue('');
    expect(within(form).getByLabelText('New password')).toHaveValue('');
    expect(within(form).getByLabelText('Confirm new password')).toHaveValue('');
    expect(within(form).getByLabelText('New password')).toHaveAttribute('type', 'password');
    expect(client.getQueryData<SessionState>(sessionQueryKey)?.status).toBe('authenticated');
    // "Last changed" follows the new time.
    expect(await screen.findByText(/Last changed 14 Oct 2026/)).toBeInTheDocument();
  });

  it('says the current password is wrong, empties that field and keeps the owner signed in', async () => {
    const { client } = renderPage();
    await screen.findByRole('region', { name: 'Change password' });

    await fill('not-the-password', 'a-brand-new-password');
    await submit();

    const current = within(passwordForm()).getByLabelText('Current password');
    expect(await screen.findByText('The current password is incorrect.')).toBeInTheDocument();
    expect(current).toHaveValue('');
    expect(current).toBeInvalid();
    expect(current).toHaveFocus();
    expect(within(passwordForm()).getByLabelText('New password')).toHaveValue('a-brand-new-password');
    expect(screen.queryByText('Password changed — other devices were signed out')).not.toBeInTheDocument();
    expect(client.getQueryData<SessionState>(sessionQueryKey)?.status).toBe('authenticated');
  });

  it.each([
    ['auth.password_invalid', 'Use between 10 and 1024 characters.'],
    ['auth.password_unchanged', 'The new password must differ from the current one.'],
  ])('shows the message of %s at the new password', async (code, message) => {
    renderPage();
    server.use(
      http.post('/api/auth/change-password', () =>
        problemResponse({ title: 'Rejected', code, status: 400, errors: { newPassword: [message] } }, 400),
      ),
    );
    await screen.findByRole('region', { name: 'Change password' });

    await fill('correct-password', 'a-brand-new-password');
    await submit();

    expect(await screen.findByText(message)).toBeInTheDocument();
    const next = within(passwordForm()).getByLabelText('New password');
    expect(next).toBeInvalid();
    expect(next).toHaveFocus();
    expect(within(passwordForm()).getByLabelText('Current password')).toHaveValue('correct-password');

    // Editing the field takes the message away.
    await userEvent.type(next, '!');
    expect(screen.queryByText(message)).not.toBeInTheDocument();
  });

  it('gives password managers a rendered (visually hidden) username field with the account e-mail', async () => {
    renderPage();
    await screen.findByRole('region', { name: 'Change password' });

    const username = await within(passwordForm()).findByDisplayValue('example@invalid.test');
    expect(username).toHaveAttribute('autocomplete', 'username');
    expect(username).toHaveAttribute('readonly');
    expect(username).toHaveAttribute('tabindex', '-1');
    expect(username).toHaveAttribute('aria-hidden', 'true');
    // Rendered, not display:none (password managers skip those).
    expect(getComputedStyle(username).display).not.toBe('none');
  });

  it('blocks the form with a countdown while the account is locked', async () => {
    renderPage();
    server.use(
      http.post('/api/auth/change-password', () =>
        problemResponse({ title: 'Locked', code: 'auth.locked', status: 423, retryAfterSeconds: 90 }, 423),
      ),
    );
    await screen.findByRole('region', { name: 'Change password' });

    await fill('correct-password', 'a-brand-new-password');
    await submit();

    expect(await screen.findByText(/Too many failed attempts/)).toBeInTheDocument();
    expect(screen.getByText('1:30')).toBeInTheDocument();
    expect(within(passwordForm()).getByRole('button', { name: 'Change password' })).toBeDisabled();
    expect(within(passwordForm()).getByLabelText('Current password')).toHaveAttribute('readonly');
    // The values stay (nothing was wrong with them) so the owner can try again once the lock ends.
    expect(within(passwordForm()).getByLabelText('New password')).toHaveValue('a-brand-new-password');
  });

  it('says so when another change is in progress, and when the server cannot be reached', async () => {
    renderPage();
    server.use(
      http.post('/api/auth/change-password', () =>
        problemResponse({ title: 'Busy', code: 'auth.busy', status: 409 }, 409),
      ),
    );
    await screen.findByRole('region', { name: 'Change password' });
    await fill('correct-password', 'a-brand-new-password');
    await submit();
    expect(await screen.findByText(/Another change is in progress/)).toBeInTheDocument();

    server.use(http.post('/api/auth/change-password', () => HttpResponse.error()));
    await submit();
    expect(await screen.findByText(/could not be changed/)).toBeInTheDocument();
    expect(screen.queryByText(/Another change is in progress/)).not.toBeInTheDocument();
  });

  it('explains that the Keychain items from first set-up are not updated', async () => {
    renderPage();
    expect(await screen.findByText(/not updated by a change here/)).toHaveTextContent('owner-reset');
  });
});

describe('My account: recent sign-in activity', () => {
  it('lists the events newest first with a friendly name, the time and the address', async () => {
    renderPage({
      audit: [
        buildAuditEntry({ event: 'PasswordChanged', at: '2026-10-14T10:00:00Z', ip: '192.0.2.10' }),
        buildAuditEntry({ event: 'LoginFailed', at: '2026-10-13T08:00:00Z', ip: '198.51.100.7' }),
        buildAuditEntry({ event: 'SomethingNew', at: '2026-10-12T08:00:00Z', ip: null }),
      ],
    });

    const list = await screen.findByRole('list', { name: 'Recent sign-in activity' });
    const items = within(list).getAllByRole('listitem');
    expect(items).toHaveLength(3);
    expect(items[0]).toHaveTextContent('Password changed');
    expect(items[0]).toHaveTextContent('192.0.2.10');
    expect(items[0]).toHaveTextContent('14 Oct 2026');
    expect(items[1]).toHaveTextContent('Sign-in failed');
    expect(items[1]).toHaveTextContent('198.51.100.7');
    expect(items[2]).toHaveTextContent('Something new');
    expect(items[2]).toHaveTextContent('address unknown');
  });

  it('asks for the last ten events', async () => {
    let take: string | null = null;
    const fake = createAccountFake();
    server.use(
      http.get('/api/auth/audit', ({ request }) => {
        take = new URL(request.url).searchParams.get('take');
        return HttpResponse.json([]);
      }),
      ...fake.handlers,
    );
    renderRoutes([{ path: '/', element: <AccountPage /> }]);

    expect(await screen.findByText('No sign-in activity recorded yet.')).toBeInTheDocument();
    expect(take).toBe('10');
    expect(screen.queryByRole('list', { name: 'Recent sign-in activity' })).not.toBeInTheDocument();
  });

  it('shows a loading state while the events are read', async () => {
    const fake = createAccountFake();
    server.use(
      http.get('/api/auth/audit', () => pendingResponse()),
      ...fake.handlers,
    );
    renderRoutes([{ path: '/', element: <AccountPage /> }]);

    expect(await screen.findByText('Loading sign-in activity')).toBeInTheDocument();
  });

  it('shows an error with Retry when the events cannot be read', async () => {
    const fake = createAccountFake();
    let calls = 0;
    server.use(
      http.get('/api/auth/audit', () => {
        calls += 1;
        return calls === 1
          ? problemResponse({ title: 'Audit unavailable', status: 500 }, 500)
          : HttpResponse.json([buildAuditEntry({ event: 'Logout' })]);
      }),
      ...fake.handlers,
    );
    renderRoutes([{ path: '/', element: <AccountPage /> }]);

    const card = await screen.findByRole('region', { name: 'Recent sign-in activity' });
    expect(await within(card).findByText('Audit unavailable')).toBeInTheDocument();
    await userEvent.click(within(card).getByRole('button', { name: 'Retry' }));

    expect(await within(card).findByText('Signed out')).toBeInTheDocument();
  });
});
