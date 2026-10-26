import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { appliedCss } from '@/test/css';
import { stubPhoneWidth } from '@/test/matchMedia';
import { problemResponse } from '@/test/server';
import { createServicesFake } from '@/test/servicesFake';
import {
  buildAccountSyncService,
  buildFailedRun,
  buildMasterService,
  buildServiceRun,
  buildServiceView,
  serviceIds,
  serviceRunId,
} from '@/test/servicesFixtures';
import { chooseAction, passMinutes, renderServices, serviceCard, servicesList, setNow } from './testing';
import { pendingResponse } from '@/test/gate';

afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe('Services: the cards', () => {
  it('has the title row and one card per service, each headed by its name', async () => {
    renderServices();

    expect(await screen.findByRole('heading', { level: 1, name: 'Services' })).toBeInTheDocument();
    const list = await servicesList();
    expect(within(list).getAllByRole('listitem', { name: /./ })).toHaveLength(3);
    for (const name of ['End-of-day data run', 'Instrument master refresh', 'Account & portfolio sync']) {
      const card = within(list).getByRole('listitem', { name });
      expect(within(card).getByRole('heading', { level: 3, name })).toBeInTheDocument();
    }
  });

  it('shows the name and what it does, the schedule in words with retries, timeout and catch-up as chips, and who it runs as', async () => {
    renderServices();

    const card = await serviceCard('End-of-day data run');
    expect(
      within(card).getByText('Loads the day from Dhan, builds every timeframe and checks the data.'),
    ).toBeInTheDocument();
    expect(within(card).getByText('Every trading day at 15:50 IST')).toBeInTheDocument();
    expect(within(card).getByText('2 retries')).toBeInTheDocument();
    expect(within(card).getByText('60 min timeout')).toBeInTheDocument();
    expect(within(card).getByText('Catches up')).toBeInTheDocument();
    expect(within(card).getByText('service identity')).toBeInTheDocument();
    const master = await serviceCard('Instrument master refresh');
    expect(within(master).getByText('1 retry')).toBeInTheDocument();
    expect(within(master).getByText('30 min timeout')).toBeInTheDocument();
  });

  it('leaves the Catch-up chip out when the service does not catch up', async () => {
    renderServices({
      fake: createServicesFake({
        services: [
          buildServiceView({ execution: { retries: 0, timeoutMinutes: 5, catchUp: false, notifyOnFailure: false } }),
        ],
      }),
    });

    const card = await serviceCard('End-of-day data run');
    expect(within(card).getByText('0 retries')).toBeInTheDocument();
    expect(within(card).queryByText('Catches up')).not.toBeInTheDocument();
  });

  it('says the status in a word with an icon, not by colour alone', async () => {
    renderServices({
      fake: createServicesFake({
        services: [
          buildServiceView(),
          buildMasterService({ enabled: false, status: 'paused', nextRun: null }),
          buildServiceView({
            id: 'alerts',
            name: 'Price alerts',
            status: 'failed',
            lastRun: buildFailedRun({ serviceId: 'alerts' }),
          }),
        ],
      }),
    });

    const healthy = await serviceCard('End-of-day data run');
    expect(within(healthy).getByText('Healthy')).toBeInTheDocument();
    expect(within(healthy).getByTestId('CheckCircleOutlinedIcon')).toBeInTheDocument();
    const paused = await serviceCard('Instrument master refresh');
    expect(within(paused).getByText('Paused')).toBeInTheDocument();
    expect(within(paused).getByTestId('PauseCircleOutlineOutlinedIcon')).toBeInTheDocument();
    const failed = await serviceCard('Price alerts');
    expect(within(failed).getByText('Failed')).toBeInTheDocument();
    expect(within(failed).getAllByTestId('ErrorOutlineOutlinedIcon').length).toBeGreaterThan(0);
  });

  it('draws a paused card with a dashed border and the others solid', async () => {
    renderServices({
      fake: createServicesFake({
        services: [buildServiceView(), buildMasterService({ enabled: false, status: 'paused', nextRun: null })],
      }),
    });

    expect(getComputedStyle(await serviceCard('Instrument master refresh')).borderStyle).toBe('dashed');
    expect(getComputedStyle(await serviceCard('End-of-day data run')).borderStyle).toBe('solid');
  });

  it('shows the last run in India time with its trigger chip, and why a failed run failed', async () => {
    renderServices({
      fake: createServicesFake({
        services: [
          buildServiceView({
            status: 'failed',
            lastRun: buildFailedRun({ trigger: 'catch_up' }),
          }),
          buildMasterService(),
        ],
      }),
    });

    const failed = await serviceCard('End-of-day data run');
    expect(within(failed).getByText('Mon 12 Oct 15:51')).toHaveAttribute('datetime', '2026-10-12T15:51:00+05:30');
    expect(within(failed).getByText('Catch-up')).toBeInTheDocument();
    expect(within(failed).getByText('Failed: Dhan refused the access token.')).toBeInTheDocument();
    const fine = await serviceCard('Instrument master refresh');
    expect(within(fine).getByText('Mon 12 Oct 18:31')).toBeInTheDocument();
    expect(within(fine).getByText('Scheduled')).toBeInTheDocument();
  });

  it('shows the next run in India time, "After …" for a run-after service and a dash for a paused one', async () => {
    renderServices({
      fake: createServicesFake({
        services: [
          buildServiceView(),
          buildMasterService({
            schedule: { type: 'after', after: serviceIds.eod, tradingDaysOnly: false },
            scheduleWords: 'After End-of-day data run finishes',
            nextRun: null,
          }),
          buildServiceView({ id: 'quiet', name: 'Quiet service', enabled: false, status: 'paused', nextRun: null }),
        ],
      }),
    });

    const eod = await serviceCard('End-of-day data run');
    expect(within(eod).getByText('Tue 13 Oct 15:50')).toHaveAttribute('datetime', '2026-10-13T15:50:00+05:30');
    expect(within(eod).getByText('Next run (IST)')).toBeInTheDocument();
    expect(within(eod).getByText('Last run (IST)')).toBeInTheDocument();
    expect(
      within(await serviceCard('Instrument master refresh')).getByText('After End-of-day data run'),
    ).toBeInTheDocument();
    expect(within(await serviceCard('Quiet service')).getByText('Not scheduled')).toBeInTheDocument();
  });

  it('says how long until the next run, and counts down as the minutes pass', async () => {
    // 14:05 in India.
    setNow('2026-10-13T08:35:00Z');
    renderServices();

    const card = await serviceCard('End-of-day data run');
    expect(within(card).getByText('in 1 h 45 min')).toBeInTheDocument();
    expect(within(await serviceCard('Instrument master refresh')).getByText('in 4 h 25 min')).toBeInTheDocument();

    passMinutes(1);

    expect(await within(card).findByText('in 1 h 44 min')).toBeInTheDocument();
  });

  it('says "due now" for a start that has come and not yet been read again', async () => {
    setNow('2026-10-13T10:21:00Z');
    renderServices({
      fake: createServicesFake({ services: [buildServiceView({ nextRun: '2026-10-13T15:50:00+05:30' })] }),
    });

    expect(within(await serviceCard('End-of-day data run')).getByText('due now')).toBeInTheDocument();
  });

  it('shows a service that never ran', async () => {
    renderServices({
      fake: createServicesFake({ services: [buildServiceView({ lastRun: null, status: 'healthy' })] }),
    });

    expect(within(await serviceCard('End-of-day data run')).getByText('Never run')).toBeInTheDocument();
  });

  it('shows the findings of the checks under the schedule of the service they are about', async () => {
    renderServices({
      fake: createServicesFake({
        services: [
          buildServiceView({
            issues: [
              {
                serviceId: serviceIds.eod,
                field: 'schedule.time',
                severity: 'warning',
                code: 'x',
                message: 'The end-of-day fetch before 15:35 may miss bars.',
                relatedServiceId: null,
              },
            ],
          }),
          buildMasterService(),
        ],
      }),
    });

    const card = await serviceCard('End-of-day data run');
    const list = within(card).getByRole('list', { name: 'Schedule checks' });
    expect(within(list).getByText('The end-of-day fetch before 15:35 may miss bars.')).toBeInTheDocument();
    expect(within(list).getByText('Warning:')).toBeInTheDocument();
    expect(
      within(await serviceCard('Instrument master refresh')).queryByRole('list', { name: 'Schedule checks' }),
    ).toBeNull();
  });

  it('lays the cards out in as many columns as fit, and gives the grid the rest of the height from md', async () => {
    renderServices();

    const list = await servicesList();
    expect(appliedCss(list).base).toContain('grid-template-columns: repeat(auto-fill, minmax(min(100%, 340px), 1fr))');
    const area = list.parentElement!;
    expect(appliedCss(area).base).toContain('flex: 1 1 auto');
    expect(
      appliedCss(area).responsive.some((r) => r.query.includes('min-width') && r.css.includes('overflow-y: auto')),
    ).toBe(true);
  });
});

describe('Services: an entry that is set elsewhere', () => {
  it('has Settings and a menu instead of Run now, Configure, Pause and History', async () => {
    const { router } = renderServices();

    const card = await serviceCard('Account & portfolio sync');
    expect(within(card).getByRole('button', { name: 'Settings: Account & portfolio sync' })).toBeInTheDocument();
    expect(within(card).queryByRole('button', { name: /^Run now/ })).not.toBeInTheDocument();
    // Retries and timeout are not its own, so no chips.
    expect(within(card).queryByText(/retries|timeout/)).not.toBeInTheDocument();

    await chooseAction('Account & portfolio sync', 'Manage in Accounts');
    expect(await screen.findByText('Accounts screen')).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/accounts');
  });

  it('shows its last and next run like any other', async () => {
    renderServices({
      fake: createServicesFake({
        services: [
          buildAccountSyncService({
            lastRun: buildServiceRun({ id: serviceRunId(4), serviceId: serviceIds.sync, trigger: 'scheduled' }),
          }),
        ],
      }),
    });

    const card = await serviceCard('Account & portfolio sync');
    expect(within(card).getByText('Tue 13 Oct 16:00')).toBeInTheDocument();
    expect(within(card).getByText('Mon 12 Oct 15:58')).toBeInTheDocument();
  });
});

describe('Services: loading, errors and nothing to show', () => {
  it('shows a loading state until the list arrives', async () => {
    renderServices({ handlers: [http.get('/api/services', () => pendingResponse())] });

    expect(await screen.findByText('Loading services')).toBeInTheDocument();
    expect(screen.queryByRole('list', { name: 'Services' })).not.toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Services summary' })).not.toBeInTheDocument();
  });

  it('says the list could not be read, with Retry that loads it', async () => {
    const fake = createServicesFake();
    fake.failures.list = true;
    renderServices({ fake });

    expect(await screen.findByText('The services are not available.')).toBeInTheDocument();
    fake.failures.list = false;
    await userEvent.click(screen.getByRole('button', { name: 'Retry' }));

    expect(await screen.findByRole('list', { name: 'Services' })).toBeInTheDocument();
    expect(screen.queryByText('The services are not available.')).not.toBeInTheDocument();
  });

  it('says there are no services', async () => {
    renderServices({ fake: createServicesFake({ services: [] }) });

    expect(await screen.findByText('No services')).toBeInTheDocument();
    expect(screen.queryByRole('list', { name: 'Services' })).not.toBeInTheDocument();
  });

  it('explains a failure to reach the server', async () => {
    renderServices({ handlers: [http.get('/api/services', () => HttpResponse.error())] });

    expect(
      await screen.findByText('Could not reach the server. Check your connection and try again.'),
    ).toBeInTheDocument();
  });

  it('uses the title the API sends for a problem', async () => {
    renderServices({
      handlers: [http.get('/api/services', () => problemResponse({ title: 'Services are down.', code: 'x' }, 503))],
    });

    expect(await screen.findByText('Services are down.')).toBeInTheDocument();
  });
});

describe('Services: on a phone', () => {
  it('shows the same cards, with their facts, Run now and the menu', async () => {
    stubPhoneWidth();
    renderServices();

    const list = await screen.findByRole('list', { name: 'Services' });
    const card = within(list).getByRole('listitem', { name: 'End-of-day data run' });
    expect(within(card).getByText('Healthy')).toBeInTheDocument();
    expect(within(card).getByText('Every trading day at 15:50 IST')).toBeInTheDocument();
    expect(within(card).getByText('Tue 13 Oct 15:50')).toBeInTheDocument();
    expect(within(card).getByText('Scheduled')).toBeInTheDocument();
    expect(within(card).getByRole('button', { name: 'Run now: End-of-day data run' })).toBeInTheDocument();
    expect(within(card).getByRole('button', { name: 'Actions for End-of-day data run' })).toBeInTheDocument();
  });

  it('opens Configure from the menu of a card', async () => {
    stubPhoneWidth();
    renderServices();

    await chooseAction('End-of-day data run', 'Configure');

    expect(await screen.findByRole('dialog', { name: 'Configure — End-of-day data run' })).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.getByRole('region', { name: 'Preview' })).toBeInTheDocument();
    });
  });
});
