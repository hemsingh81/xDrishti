import { fireEvent, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { buildEventFilter, buildFilterRow } from '@/test/eventsFixtures';
import { pendingResponse } from '@/test/gate';
import { renderRoutes } from '@/test/render';
import { problemResponse, server } from '@/test/server';
import { SettingsPage } from './components/SettingsPage';

const renderCard = () =>
  renderRoutes([{ path: '/settings', element: <SettingsPage /> }], { initialPath: '/settings?tab=selection' });

const type = (name: RegExp, value: string) => {
  fireEvent.change(screen.getByLabelText(name), { target: { value } });
};
const change = () => screen.getByRole('button', { name: 'Change the event filter' });
const fill = (reason = 'checked the verdict', password = 'pw') => {
  type(/^Reason/, reason);
  type(/^Your password for this change/, password);
};

describe('Settings → Selection: the event filter', () => {
  it('shows a loading state, then the switch, N, approval and why it is not acting', async () => {
    server.use(http.get('/api/selection/event-filter', () => HttpResponse.json(buildEventFilter())));
    renderCard();
    expect(await screen.findByText('Loading the event filter')).toBeInTheDocument();
    expect(await screen.findByText(/window N=2 sessions, approval manual/)).toBeInTheDocument();
    expect(screen.getByText('Not acting: the switch is off')).toBeInTheDocument();
    expect(screen.getByText(/3 of 30 tracked instruments have a results entry/)).toBeInTheDocument();
  });

  it('says the calendar coverage is unavailable instead of a number when it could not be read', async () => {
    server.use(
      http.get('/api/selection/event-filter', () =>
        HttpResponse.json(buildEventFilter({ instrumentsTracked: null, instrumentsWithEntry: null })),
      ),
    );
    renderCard();
    expect(await screen.findByText('the calendar could not be read')).toBeInTheDocument();
  });

  it('shows the problem title when the filter cannot be read', async () => {
    server.use(http.get('/api/selection/event-filter', () => problemResponse({ title: 'Selection is down.' }, 500)));
    renderCard();
    expect(await screen.findByText('Selection is down.')).toBeInTheDocument();
  });

  it('says a development KEEP that the hold-out has not confirmed will not turn the filter on automatically', async () => {
    server.use(
      http.get('/api/selection/event-filter', () =>
        HttpResponse.json(buildEventFilter({ approval: 'auto', autoVerdict: 'KEEP_NOT_CONFIRMED' })),
      ),
    );
    renderCard();
    expect(
      await screen.findByText(/not yet confirmed on the hold-out: auto approval will not turn the filter on/),
    ).toBeInTheDocument();
  });

  it('labels an automatic row as applied automatically and never as the owner', async () => {
    server.use(
      http.get('/api/selection/event-filter', () =>
        HttpResponse.json(
          buildEventFilter({
            enabled: true,
            decidedBy: 'auto',
            history: [
              buildFilterRow({ id: 2, decidedBy: 'auto', actor: 'system', verdictRef: 'abc123', reason: 'auto' }),
              buildFilterRow({ id: 1, decidedBy: 'owner', actor: 'hem' }),
            ],
          }),
        ),
      ),
    );
    renderCard();
    const table = await screen.findByRole('table', { name: 'Event filter history' });
    expect(within(table).getByText('applied automatically from verdict abc123')).toBeInTheDocument();
    expect(within(table).getByText('owner (hem)')).toBeInTheDocument();
    expect(within(table).queryByText(/owner \(system\)/)).not.toBeInTheDocument();
  });

  it('keeps the button disabled without a password and a reason, and enables it with both', async () => {
    server.use(http.get('/api/selection/event-filter', () => HttpResponse.json(buildEventFilter())));
    renderCard();
    await screen.findByText(/window N=2 sessions/);
    expect(change()).toBeDisabled();
    type(/^Reason/, 'because');
    expect(change()).toBeDisabled();
    type(/^Your password for this change/, 'pw');
    expect(change()).toBeEnabled();
    type(/^Reason/, 'x'.repeat(301));
    expect(change()).toBeDisabled();
  });

  it('sends the change with the password, clears the password and shows the new history row', async () => {
    let body: unknown;
    let current = buildEventFilter();
    server.use(
      http.get('/api/selection/event-filter', () => HttpResponse.json(current)),
      http.put('/api/selection/event-filter', async ({ request }) => {
        body = await request.json();
        current = buildEventFilter({
          enabled: true,
          decidedBy: 'owner',
          history: [buildFilterRow({ id: 5, reason: 'checked the verdict', actor: 'hem' })],
        });
        return HttpResponse.json(current);
      }),
    );
    const { client } = renderCard();
    await screen.findByText(/window N=2 sessions/);
    await userEvent.click(screen.getByRole('checkbox', { name: 'Event filter on' }));
    fill();
    await userEvent.click(change());
    expect(await screen.findByText('The event filter was changed; the new row is in the history.')).toBeInTheDocument();
    expect(body).toEqual({
      enabled: true,
      resultsWithinDays: 2,
      approval: 'manual',
      reason: 'checked the verdict',
      password: 'pw',
    });
    const table = await screen.findByRole('table', { name: 'Event filter history' });
    expect(within(table).getByText('checked the verdict')).toBeInTheDocument();
    expect(screen.getByLabelText(/^Your password for this change/)).toHaveValue('');
    // The password does not stay in the mutation cache either.
    expect(
      JSON.stringify(
        client
          .getMutationCache()
          .getAll()
          .map((m) => m.state.variables ?? null),
      ),
    ).not.toContain('"pw"');
  });

  it.each([
    [401, 'auth.invalid_credentials', 'The password was not accepted. Nothing was changed.'],
    [423, 'auth.locked', 'Too many wrong passwords; sign-in is locked for now. Locked.'],
  ])('shows the auth message for a %s and keeps the owner here', async (status, code, text) => {
    server.use(
      http.get('/api/selection/event-filter', () => HttpResponse.json(buildEventFilter())),
      http.put('/api/selection/event-filter', () => problemResponse({ title: 'Locked.', code }, status)),
    );
    renderCard();
    await screen.findByText(/window N=2 sessions/);
    fill();
    await userEvent.click(change());
    expect(await screen.findByText(text)).toBeInTheDocument();
    expect(screen.getByLabelText(/^Your password for this change/)).toHaveValue('');
  });

  it('marks the setting fields on 400 selection.invalid_setting', async () => {
    server.use(
      http.get('/api/selection/event-filter', () => HttpResponse.json(buildEventFilter())),
      http.put('/api/selection/event-filter', () =>
        problemResponse({ title: 'That setting is not allowed.', code: 'selection.invalid_setting' }, 400),
      ),
    );
    renderCard();
    await screen.findByText(/window N=2 sessions/);
    fill();
    await userEvent.click(change());
    expect(await screen.findByText('The server refused this setting.')).toBeInTheDocument();
    expect(screen.getByLabelText('Window N (sessions, 0 to 10)')).toHaveAttribute('aria-invalid', 'true');
  });

  it('reads the filter again on 409 selection.stale', async () => {
    let reads = 0;
    server.use(
      http.get('/api/selection/event-filter', () => {
        reads += 1;
        return HttpResponse.json(buildEventFilter());
      }),
      http.put('/api/selection/event-filter', () =>
        problemResponse({ title: 'Changed meanwhile.', code: 'selection.stale' }, 409),
      ),
    );
    renderCard();
    await screen.findByText(/window N=2 sessions/);
    fill();
    await userEvent.click(change());
    expect(await screen.findByText(/Changed meanwhile\. The filter was read again/)).toBeInTheDocument();
    await vi.waitFor(() => {
      expect(reads).toBe(2);
    });
  });

  it('disables the button while the change is in flight', async () => {
    server.use(
      http.get('/api/selection/event-filter', () => HttpResponse.json(buildEventFilter())),
      http.put('/api/selection/event-filter', () => pendingResponse()),
    );
    renderCard();
    await screen.findByText(/window N=2 sessions/);
    fill();
    await userEvent.click(change());
    expect(change()).toBeDisabled();
  });
});
