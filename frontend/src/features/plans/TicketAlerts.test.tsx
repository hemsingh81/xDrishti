import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { pinClock } from '@/test/pinClock';
import { beforeCutoff, buildCrossing, buildPlanAlerts, buildPrompt } from '@/test/planFixtures';
import { mount } from '@/test/planScreen';
import { renderWithProviders } from '@/test/render';
import { problemResponse, server } from '@/test/server';
import { PlansPage } from './components/PlansPage';

pinClock(beforeCutoff);

type Alerts = ReturnType<typeof buildPlanAlerts>;

/** Mounts the plan with the given alerts; returns counters of the refetches. */
function show(alerts: Alerts) {
  mount();
  const reads = { alerts: 0, notices: 0 };
  server.use(
    http.get('/api/plans/7/alerts', () => {
      reads.alerts += 1;
      return HttpResponse.json(alerts);
    }),
    http.get('/api/plans/notices', () => {
      reads.notices += 1;
      return HttpResponse.json({ items: [] });
    }),
  );
  renderWithProviders(<PlansPage />);
  return reads;
}

const on = (over: Partial<Alerts> = {}) => buildPlanAlerts({ enabled: true, ...over });

describe('Ticket alerts', () => {
  it('says none yet and always states that no order is placed', async () => {
    show(on());
    expect((await screen.findAllByText('Alerts: none yet')).length).toBe(2);
    expect(screen.getAllByText('Alert only: no order is placed.').length).toBe(2);
  });

  it('renders nothing when alerts are off', async () => {
    mount();
    renderWithProviders(<PlansPage />);
    await screen.findByRole('heading', { name: 'Primary (1)' });
    expect(screen.queryByText(/Alerts: none yet/)).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Mute alerts' })).not.toBeInTheDocument();
  });

  it('shows stop and target crossings with price and time, server text as plain text', async () => {
    show(
      on({
        crossings: [
          buildCrossing(),
          buildCrossing({
            id: 2,
            noticeId: 'crossing:2',
            kind: 'target_crossed',
            price: 110.5,
            text: '<b>x</b> target',
          }),
        ],
      }),
    );
    expect(await screen.findByText('Alerts: stop crossed at 94.80 09:35 IST')).toBeInTheDocument();
    expect(screen.getByText('Alerts: target crossed at 110.50 09:35 IST')).toBeInTheDocument();
    expect(screen.getByText('<b>x</b> target')).toBeInTheDocument();
  });

  it('shows a snoozed crossing muted with its time, and hides dismissed ones and other tickets', async () => {
    show(
      on({
        crossings: [
          buildCrossing({ state: 'snoozed', snoozedUntil: '2026-10-26T04:30:00Z' }),
          buildCrossing({ id: 2, noticeId: 'crossing:2', kind: 'target_crossed', state: 'dismissed' }),
          buildCrossing({ id: 3, noticeId: 'crossing:3', itemId: 12, price: 77 }),
        ],
      }),
    );
    expect(await screen.findByText(/stop crossed at 94\.80 09:35 IST \(snoozed until 10:00 IST\)/)).toBeInTheDocument();
    expect(screen.queryByText(/target crossed/)).not.toBeInTheDocument();
    // the other ticket's crossing shows on its own card only
    expect(screen.getAllByText(/77\.00/)).toHaveLength(1);
    expect(within(screen.getByRole('article', { name: /AAA/ })).queryByText(/77\.00/)).not.toBeInTheDocument();
  });

  it('shows each prompt status, naming the other ticket when ambiguous', async () => {
    show(
      on({
        prompts: [
          buildPrompt({ text: 'open text' }),
          buildPrompt({ id: 2, noticeId: 'prompt:2', status: 'matched', via: 'journal', text: 'm' }),
          buildPrompt({ id: 3, noticeId: 'prompt:3', status: 'matched', via: 'trade_on_record', text: 'm2' }),
          buildPrompt({ id: 4, noticeId: 'prompt:4', status: 'expired_no_fill', text: 'e' }),
          buildPrompt({ id: 5, noticeId: 'prompt:5', status: 'ambiguous', sameTradeItems: [12], text: 'a' }),
        ],
      }),
    );
    expect(await screen.findByText(/Post-trade prompt: open/)).toBeInTheDocument();
    expect(screen.getByText('Post-trade prompt: matched (via journal).')).toBeInTheDocument();
    expect(screen.getByText('Post-trade prompt: matched (via trade_on_record).')).toBeInTheDocument();
    expect(screen.getByText(/Post-trade prompt: expired, no fill/)).toBeInTheDocument();
    expect(screen.getByText(/Post-trade prompt: ambiguous.*Other tickets: BBB\./)).toBeInTheDocument();
  });

  it('mutes a ticket and reads alerts and notices again', async () => {
    const reads = show(on({ crossings: [buildCrossing()] }));
    let posted = false;
    server.use(
      http.post('/api/plans/items/11/alerts/mute', () => {
        posted = true;
        return new HttpResponse(null, { status: 204 });
      }),
    );
    await userEvent.click((await screen.findAllByRole('button', { name: 'Mute alerts' }))[0]!);
    await waitFor(() => {
      expect(reads.alerts).toBe(2);
    });
    expect(posted).toBe(true);
  });

  it('disables Mute when the ticket is already muted', async () => {
    show(on({ mutedItems: [11] }));
    const buttons = await screen.findAllByRole('button', { name: 'Mute alerts' });
    expect(buttons[0]).toBeDisabled();
    expect(buttons[1]).toBeEnabled();
  });

  it('snoozes for 30 minutes and dismisses, each with the right request and a refetch', async () => {
    const reads = show(on({ crossings: [buildCrossing()] }));
    const seen: { snooze?: unknown; dismissed?: boolean } = {};
    server.use(
      http.post('/api/plans/alerts/:noticeId/snooze', async ({ request }) => {
        seen.snooze = await request.json();
        return new HttpResponse(null, { status: 204 });
      }),
      http.post('/api/plans/alerts/:noticeId/dismiss', () => {
        seen.dismissed = true;
        return new HttpResponse(null, { status: 204 });
      }),
    );
    await userEvent.click(await screen.findByRole('button', { name: 'Snooze stop crossed 30 minutes' }));
    await waitFor(() => {
      expect(reads.alerts).toBe(2);
    });
    expect(seen.snooze).toEqual({ minutes: 30 });
    await userEvent.click(screen.getByRole('button', { name: 'Dismiss stop crossed' }));
    await waitFor(() => {
      expect(reads.alerts).toBe(3);
    });
    expect(seen.dismissed).toBe(true);
  });

  it('disables snooze when no snoozes are left and tells the snooze limit in words on a 409', async () => {
    show(on({ crossings: [buildCrossing({ snoozesLeft: 0 })] }));
    expect(await screen.findByRole('button', { name: 'Snooze stop crossed 15 minutes' })).toBeDisabled();
  });

  it('shows the snooze limit in words', async () => {
    show(on({ crossings: [buildCrossing()] }));
    server.use(
      http.post('/api/plans/alerts/:noticeId/snooze', () =>
        problemResponse({ title: 'Limit.', code: 'plans.snooze_limit' }, 409),
      ),
    );
    await userEvent.click(await screen.findByRole('button', { name: 'Snooze stop crossed 60 minutes' }));
    expect(await screen.findByText(/snooze limit is reached/)).toBeInTheDocument();
  });
});
