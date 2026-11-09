import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { renderRoutes } from '@/test/render';
import { server } from '@/test/server';
import { buildLearned, buildLearnedChange } from '@/test/settingsFixtures';
import { SettingsPage } from './components/SettingsPage';

const renderLearned = () =>
  renderRoutes([{ path: '/settings', element: <SettingsPage /> }], { initialPath: '/settings?tab=learned' });

const serve = (...items: ReturnType<typeof buildLearnedChange>[]) => {
  server.use(http.get('/api/settings/learned', () => HttpResponse.json(buildLearned(items))));
};

const problem = (status: number, code: string, detail: string) =>
  HttpResponse.json({ title: detail, detail, code, status }, { status });

describe('Learned changes panel', () => {
  it('says plainly when there is nothing, and how auto decisions are recorded', async () => {
    renderLearned();

    expect(await screen.findByText(/No learned changes yet/)).toBeInTheDocument();
    expect(screen.getByText(/recorded as auto, never as your approval/)).toBeInTheDocument();
    expect(screen.getByText(/take effect at the next weekly run/)).toBeInTheDocument();
  });

  it('shows loading and a failed read', async () => {
    server.use(
      http.get('/api/settings/learned', () => HttpResponse.json({ title: 'Service unavailable' }, { status: 503 })),
    );
    renderLearned();

    expect(await screen.findByText('Service unavailable')).toBeInTheDocument();
  });

  it('shows state as text, and an automatic decision as auto', async () => {
    serve(
      buildLearnedChange({
        id: 7,
        state: 'applied',
        decidedBy: 'auto',
        decidedAt: '2026-11-09T05:00:00Z',
        canUndo: true,
        undoNeedsPassword: true,
      }),
    );
    renderLearned();

    const card = await screen.findByRole('article', { name: 'Confidence bar: 0.55 to 0.6' });
    expect(within(card).getByText(/Applied · More cautious/)).toBeInTheDocument();
    expect(within(card).getByText(/Decided automatically \(recorded as auto\)/)).toBeInTheDocument();
    expect(within(card).queryByRole('button', { name: 'Approve' })).not.toBeInTheDocument();
  });

  it('approves a tightening after a confirm, with no password', async () => {
    serve(buildLearnedChange({ id: 3 }));
    let body: string | null = null;
    server.use(
      http.post('/api/settings/learned/3/approve', async ({ request }) => {
        body = await request.text();
        return HttpResponse.json(buildLearnedChange({ id: 3, state: 'applied' }));
      }),
    );
    renderLearned();

    await userEvent.click(await screen.findByRole('button', { name: 'Approve' }));
    expect(body).toBeNull();
    expect(screen.queryByLabelText(/Password/)).not.toBeInTheDocument();
    await userEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Approve' }));

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(body).toBe('');
  });

  it('asks for the password to approve a loosening and sends it', async () => {
    serve(buildLearnedChange({ id: 4, direction: 'loosen', approveNeedsPassword: true }));
    let body: unknown;
    server.use(
      http.post('/api/settings/learned/4/approve', async ({ request }) => {
        body = await request.json();
        return HttpResponse.json(buildLearnedChange({ id: 4, state: 'applied' }));
      }),
    );
    renderLearned();

    await userEvent.click(await screen.findByRole('button', { name: 'Approve' }));
    await userEvent.type(await screen.findByLabelText(/Password/), 'secret');
    await userEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Approve' }));

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(body).toEqual({ password: 'secret' });
  });

  it('undoes an applied tightening with the password', async () => {
    serve(
      buildLearnedChange({
        id: 5,
        state: 'applied',
        decidedBy: 'auto',
        decidedAt: '2026-11-09T05:00:00Z',
        canUndo: true,
        undoNeedsPassword: true,
      }),
    );
    let body: unknown;
    server.use(
      http.post('/api/settings/learned/5/undo', async ({ request }) => {
        body = await request.json();
        return HttpResponse.json(buildLearnedChange({ id: 5, state: 'undone' }));
      }),
    );
    renderLearned();

    await userEvent.click(await screen.findByRole('button', { name: 'Undo' }));
    await userEvent.type(await screen.findByLabelText(/Password/), 'secret');
    await userEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Undo' }));

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(body).toEqual({ password: 'secret' });
  });

  it('dismisses a proposal after a confirm, with no body', async () => {
    serve(buildLearnedChange({ id: 6 }));
    let text: string | null = null;
    server.use(
      http.post('/api/settings/learned/6/dismiss', async ({ request }) => {
        text = await request.text();
        return HttpResponse.json(buildLearnedChange({ id: 6, state: 'dismissed' }));
      }),
    );
    renderLearned();

    await userEvent.click(await screen.findByRole('button', { name: 'Dismiss' }));
    await userEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Dismiss' }));

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(text).toBe('');
  });

  it.each([
    [401, 'auth.invalid_credentials', 'That password was not right.'],
    [403, 'auth.step_up_required', 'Enter your password to continue.'],
    [423, 'auth.locked', 'Locked for 15 minutes.'],
  ])('shows %s %s in the server words and keeps the password dialog', async (status, code, detail) => {
    serve(buildLearnedChange({ id: 8, direction: 'loosen', approveNeedsPassword: true }));
    server.use(http.post('/api/settings/learned/8/approve', () => problem(status, code, detail)));
    renderLearned();

    await userEvent.click(await screen.findByRole('button', { name: 'Approve' }));
    await userEvent.type(await screen.findByLabelText(/Password/), 'x');
    await userEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Approve' }));

    expect(await screen.findByText(detail)).toBeInTheDocument();
    expect(screen.getByLabelText(/Password/)).toBeInTheDocument();
  });

  it.each([
    [404, 'learned.not_found', 'That change no longer exists.'],
    [409, 'learned.wrong_state', 'That change was already decided.'],
    [409, 'learned.stale', 'The setting moved on since this was proposed.'],
    [409, 'learned.config_invalid', 'The whole configuration would be invalid.'],
  ])('shows %s %s in the server words on a confirm', async (status, code, detail) => {
    serve(buildLearnedChange({ id: 9 }));
    server.use(http.post('/api/settings/learned/9/dismiss', () => problem(status, code, detail)));
    renderLearned();

    await userEvent.click(await screen.findByRole('button', { name: 'Dismiss' }));
    await userEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Dismiss' }));

    expect(await screen.findByText(detail)).toBeInTheDocument();
  });
});
