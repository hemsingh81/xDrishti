import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { renderRoutes } from '@/test/render';
import { server } from '@/test/server';
import { buildEssential, buildEssentials } from '@/test/settingsFixtures';
import { SettingsPage } from './components/SettingsPage';

const renderSettings = () =>
  renderRoutes([{ path: '/settings', element: <SettingsPage /> }], { initialPath: '/settings' });

const noticesOff = () => {
  server.use(
    http.get('/api/settings/essentials', () =>
      HttpResponse.json({
        items: [
          ...buildEssentials().items.filter((i) => i.key !== 'notices.failure'),
          buildEssential({
            key: 'notices.failure',
            label: 'Failure notices',
            value: 'false',
            default: 'true',
            isDefault: false,
            canReset: true,
          }),
        ],
      }),
    ),
  );
};

const problem = (status: number, code: string, detail: string) =>
  HttpResponse.json({ title: detail, detail, code, status }, { status });

describe('Essential settings acts', () => {
  it('turns failure notices off with the password and reads the list again', async () => {
    let body: unknown;
    server.use(
      http.put('/api/settings/essentials/notices.failure', async ({ request }) => {
        body = await request.json();
        return HttpResponse.json(buildEssential({ key: 'notices.failure', label: 'Failure notices', value: 'false' }));
      }),
    );
    renderSettings();

    await userEvent.click(await screen.findByRole('button', { name: 'Turn off failure notices' }));
    await userEvent.type(await screen.findByLabelText(/Password/), 'secret');
    await userEvent.click(screen.getByRole('button', { name: 'Turn off' }));

    await screen.findByRole('button', { name: 'Turn off failure notices' });
    expect(body).toEqual({ on: false, password: 'secret' });
    expect(screen.queryByLabelText(/Password/)).not.toBeInTheDocument();
  });

  it('shows the server wording for a wrong password and keeps the dialog open', async () => {
    server.use(
      http.put('/api/settings/essentials/notices.failure', () =>
        problem(401, 'auth.invalid_credentials', 'That password was not right.'),
      ),
    );
    renderSettings();

    await userEvent.click(await screen.findByRole('button', { name: 'Turn off failure notices' }));
    await userEvent.type(await screen.findByLabelText(/Password/), 'nope');
    await userEvent.click(screen.getByRole('button', { name: 'Turn off' }));

    expect(await screen.findByText('That password was not right.')).toBeInTheDocument();
    expect(screen.getByLabelText(/Password/)).toBeInTheDocument();
  });

  it('shows a lock in the server words', async () => {
    server.use(
      http.put('/api/settings/essentials/notices.failure', () => problem(423, 'auth.locked', 'Locked for 15 minutes.')),
    );
    renderSettings();

    await userEvent.click(await screen.findByRole('button', { name: 'Turn off failure notices' }));
    await userEvent.type(await screen.findByLabelText(/Password/), 'x');
    await userEvent.click(screen.getByRole('button', { name: 'Turn off' }));

    expect(await screen.findByText('Locked for 15 minutes.')).toBeInTheDocument();
  });

  it('turns notices on after a confirm, without a password', async () => {
    noticesOff();
    let body: unknown;
    server.use(
      http.put('/api/settings/essentials/notices.failure', async ({ request }) => {
        body = await request.json();
        return HttpResponse.json(buildEssential({ key: 'notices.failure', label: 'Failure notices', value: 'true' }));
      }),
    );
    renderSettings();

    await userEvent.click(await screen.findByRole('button', { name: 'Turn on failure notices' }));
    await userEvent.click(await screen.findByRole('button', { name: 'Turn on' }));

    await screen.findByRole('button', { name: 'Turn on failure notices' });
    expect(body).toEqual({ on: true, password: null });
  });

  it('resets to default with the password', async () => {
    noticesOff();
    let body: unknown;
    server.use(
      http.post('/api/settings/essentials/notices.failure/reset', async ({ request }) => {
        body = await request.json();
        return HttpResponse.json(buildEssential({ key: 'notices.failure', label: 'Failure notices', value: 'true' }));
      }),
    );
    renderSettings();

    await userEvent.click(await screen.findByRole('button', { name: 'Reset Failure notices to default' }));
    await userEvent.type(await screen.findByLabelText(/Password/), 'secret');
    await userEvent.click(screen.getByRole('button', { name: 'Reset to default' }));

    await screen.findByRole('button', { name: 'Reset Failure notices to default' });
    expect(body).toEqual({ password: 'secret' });
  });

  it('shows a refused reset in the server words', async () => {
    noticesOff();
    server.use(
      http.post('/api/settings/essentials/notices.failure/reset', () =>
        problem(403, 'auth.step_up_required', 'Enter your password to continue.'),
      ),
    );
    renderSettings();

    await userEvent.click(await screen.findByRole('button', { name: 'Reset Failure notices to default' }));
    await userEvent.type(await screen.findByLabelText(/Password/), 'x');
    await userEvent.click(screen.getByRole('button', { name: 'Reset to default' }));

    expect(await screen.findByText('Enter your password to continue.')).toBeInTheDocument();
  });
});
