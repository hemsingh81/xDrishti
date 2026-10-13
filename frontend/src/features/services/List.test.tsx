import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { appliedCss } from '@/test/css';
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
import { renderServices, serviceRow, servicesGrid, stubPhone } from './testing';

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('Services: the table', () => {
  it('has the title row and one row per service with the columns of the prototype', async () => {
    renderServices();

    expect(await screen.findByRole('heading', { level: 1, name: 'Services' })).toBeInTheDocument();
    const grid = await servicesGrid();
    const headers = within(grid)
      .getAllByRole('columnheader')
      .map((h) => h.textContent);
    expect(headers).toEqual(['Service', 'Schedule', 'Runs as', 'Status', 'Last run', 'Next run (IST)', 'Actions']);
    expect(within(grid).getAllByRole('row')).toHaveLength(4);
  });

  it('shows the name and what it does, the schedule in words with retries, timeout and catch-up, and who it runs as', async () => {
    renderServices();

    const row = await serviceRow('End-of-day data run');
    expect(
      within(row).getByText('Loads the day from Dhan, builds every timeframe and checks the data.'),
    ).toBeInTheDocument();
    expect(within(row).getByText('Every trading day at 15:50 IST')).toBeInTheDocument();
    expect(within(row).getByText('retries 2 · timeout 60 min · catch-up')).toBeInTheDocument();
    expect(within(row).getByText('service identity')).toBeInTheDocument();
    const master = await serviceRow('Instrument master refresh');
    expect(within(master).getByText('retries 1 · timeout 30 min · catch-up')).toBeInTheDocument();
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

    const healthy = await serviceRow('End-of-day data run');
    expect(within(healthy).getByText('Healthy')).toBeInTheDocument();
    expect(within(healthy).getByTestId('CheckCircleOutlinedIcon')).toBeInTheDocument();
    const paused = await serviceRow('Instrument master refresh');
    expect(within(paused).getByText('Paused')).toBeInTheDocument();
    expect(within(paused).getByTestId('PauseCircleOutlineOutlinedIcon')).toBeInTheDocument();
    const failed = await serviceRow('Price alerts');
    expect(within(failed).getByText('Failed')).toBeInTheDocument();
    expect(within(failed).getByTestId('ErrorOutlineOutlinedIcon')).toBeInTheDocument();
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

    const failed = await serviceRow('End-of-day data run');
    expect(within(failed).getByText('Mon 12 Oct 15:51')).toHaveAttribute('datetime', '2026-10-12T15:51:00+05:30');
    expect(within(failed).getByText('Catch-up')).toBeInTheDocument();
    expect(within(failed).getByText('Failed: Dhan refused the access token.')).toBeInTheDocument();
    const fine = await serviceRow('Instrument master refresh');
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

    const eod = await serviceRow('End-of-day data run');
    expect(within(eod).getByText('Tue 13 Oct 15:50')).toHaveAttribute('datetime', '2026-10-13T15:50:00+05:30');
    expect(
      within(await serviceRow('Instrument master refresh')).getByText('After End-of-day data run'),
    ).toBeInTheDocument();
    expect(within(await serviceRow('Quiet service')).getByText('Not scheduled')).toBeInTheDocument();
  });

  it('shows a service that never ran', async () => {
    renderServices({
      fake: createServicesFake({ services: [buildServiceView({ lastRun: null, status: 'healthy' })] }),
    });

    expect(within(await serviceRow('End-of-day data run')).getByText('Never run')).toBeInTheDocument();
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

    const row = await serviceRow('End-of-day data run');
    const list = within(row).getByRole('list', { name: 'Schedule checks' });
    expect(within(list).getByText('The end-of-day fetch before 15:35 may miss bars.')).toBeInTheDocument();
    expect(within(list).getByText('Warning:')).toBeInTheDocument();
    expect(
      within(await serviceRow('Instrument master refresh')).queryByRole('list', { name: 'Schedule checks' }),
    ).toBeNull();
  });

  it('puts the table in a card that takes the rest of the height, from md', async () => {
    renderServices();

    const card = await screen.findByRole('region', { name: 'Services' });
    expect(appliedCss(card).base).toContain('flex: 1 1 auto');
  });
});

describe('Services: an entry that is set elsewhere', () => {
  it('has a link instead of the actions', async () => {
    const { router } = renderServices();

    const row = await serviceRow('Account & portfolio sync');
    expect(within(row).queryByRole('button')).toBeNull();
    const link = within(row).getByRole('link', { name: 'Manage in Accounts: Account & portfolio sync' });
    expect(link).toHaveAttribute('href', '/accounts');
    await userEvent.click(link);
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

    const row = await serviceRow('Account & portfolio sync');
    expect(within(row).getByText('Tue 13 Oct 16:00')).toBeInTheDocument();
    expect(within(row).getByText('Mon 12 Oct 15:58')).toBeInTheDocument();
  });
});

describe('Services: loading, errors and nothing to show', () => {
  it('shows a loading state until the list arrives', async () => {
    renderServices({ handlers: [http.get('/api/services', () => new Promise<never>(() => undefined))] });

    expect(await screen.findByRole('progressbar', { name: 'Loading services' })).toBeInTheDocument();
    expect(screen.queryByRole('grid', { name: 'Services' })).not.toBeInTheDocument();
  });

  it('says the list could not be read, with Retry that loads it', async () => {
    const fake = createServicesFake();
    fake.failures.list = true;
    renderServices({ fake });

    expect(await screen.findByText('The services are not available.')).toBeInTheDocument();
    fake.failures.list = false;
    await userEvent.click(screen.getByRole('button', { name: 'Retry' }));

    expect(await screen.findByRole('grid', { name: 'Services' })).toBeInTheDocument();
    expect(screen.queryByText('The services are not available.')).not.toBeInTheDocument();
  });

  it('says there are no services', async () => {
    renderServices({ fake: createServicesFake({ services: [] }) });

    expect(await screen.findByText('No services')).toBeInTheDocument();
    expect(screen.queryByRole('grid', { name: 'Services' })).not.toBeInTheDocument();
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
  it('shows each service as a card with its facts and actions instead of a table', async () => {
    stubPhone();
    renderServices();

    const list = await screen.findByRole('list', { name: 'Services' });
    expect(screen.queryByRole('grid', { name: 'Services' })).not.toBeInTheDocument();
    const cards = within(list).getAllByRole('listitem');
    expect(cards).toHaveLength(3);
    const card = within(list).getByRole('listitem', { name: 'End-of-day data run' });
    expect(within(card).getByText('Healthy')).toBeInTheDocument();
    expect(within(card).getByText('Every trading day at 15:50 IST')).toBeInTheDocument();
    expect(within(card).getByText('Tue 13 Oct 15:50')).toBeInTheDocument();
    expect(within(card).getByText('Scheduled')).toBeInTheDocument();
    for (const name of ['Run now', 'Configure', 'Pause', 'History']) {
      expect(within(card).getByRole('button', { name: `${name}: End-of-day data run` })).toBeInTheDocument();
    }
    const sync = within(list).getByRole('listitem', { name: 'Account & portfolio sync' });
    expect(within(sync).getByRole('link', { name: /Manage in Accounts/ })).toBeInTheDocument();
  });

  it('opens Configure from a card', async () => {
    stubPhone();
    renderServices();

    await userEvent.click(await screen.findByRole('button', { name: 'Configure: End-of-day data run' }));

    expect(await screen.findByRole('dialog', { name: 'Configure — End-of-day data run' })).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.getByRole('region', { name: 'Preview' })).toBeInTheDocument();
    });
  });
});
