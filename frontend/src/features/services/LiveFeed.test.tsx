import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { createServicesFake } from '@/test/servicesFake';
import { buildLiveFeedService, buildServiceView, serviceIds } from '@/test/servicesFixtures';
import { renderServices, serviceCard } from './testing';

/** The live feed is a service that another process (xd-feed) runs: the card shows its heartbeat line and Run now only queues a request. */
describe('Services: the live feed', () => {
  it('shows who runs it, its state and the line its heartbeat gives', async () => {
    renderServices({ fake: createServicesFake({ services: [buildServiceView(), buildLiveFeedService()] }) });

    const card = await serviceCard('Live feed');

    expect(within(card).getByText('xd-feed')).toBeInTheDocument();
    expect(within(card).getByText('Running')).toBeInTheDocument();
    expect(within(card).getByText('Up 09:00–15:35 IST on trading days')).toBeInTheDocument();
    expect(
      within(card).getByText('Connected · 58 instruments · last tick 10:00:12 IST · 4.3 ticks/s'),
    ).toBeInTheDocument();
  });

  it('says in the window that the feed is not running when its heartbeat is gone', async () => {
    const failed = buildLiveFeedService({
      status: 'failed',
      live: {
        state: 'not_running',
        connected: false,
        subscribed: 0,
        lastTickAt: null,
        ticksPerSecond: 0,
        lastSeenAt: null,
        summary: 'The live feed is not running.',
      },
    });
    renderServices({ fake: createServicesFake({ services: [failed] }) });

    const card = await serviceCard('Live feed');

    expect(within(card).getByText('Failed')).toBeInTheDocument();
    expect(within(card).getByText('The live feed is not running.')).toBeInTheDocument();
  });

  it('has no heartbeat line on a service that this application runs itself', async () => {
    renderServices();

    const card = await serviceCard('End-of-day data run');

    expect(within(card).queryByText(/instruments|not running/)).not.toBeInTheDocument();
  });

  it('says the request is only queued, in a toast, when Run now is answered with a queued run, and does not mark the feed as running', async () => {
    const idle = buildLiveFeedService({
      status: 'healthy',
      lastRun: null,
      live: {
        state: 'closed',
        connected: false,
        subscribed: 0,
        lastTickAt: null,
        ticksPerSecond: 0,
        lastSeenAt: '2026-10-13T18:00:00+05:30',
        summary: 'Idle: outside the window.',
      },
    });
    const queued = {
      id: '00000000-0000-0000-0000-000000000000',
      serviceId: serviceIds.feed,
      trigger: 'manual',
      attempt: 1,
      startedAt: '2026-10-13T18:00:00+05:30',
      finishedAt: null,
      status: 'queued',
      message: 'Requested. The live feed opens its run within 15 seconds.',
      dataJobId: null,
    };
    renderServices({
      fake: createServicesFake({ services: [idle] }),
      handlers: [http.post('/api/services/live-feed/run', () => HttpResponse.json(queued, { status: 202 }))],
    });

    await userEvent.click(await screen.findByRole('button', { name: 'Run now: Live feed' }));

    expect(await screen.findByText('Requested — the live feed opens its run within 15 seconds.')).toBeInTheDocument();
    expect(screen.queryByText('Live feed started.')).not.toBeInTheDocument();
    const card = await serviceCard('Live feed');
    expect(within(card).queryByText('Running')).not.toBeInTheDocument();
    expect(within(card).getByText('Healthy')).toBeInTheDocument();
  });
});
