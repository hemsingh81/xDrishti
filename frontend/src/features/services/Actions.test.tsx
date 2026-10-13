import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http } from 'msw';
import { problemResponse } from '@/test/server';
import { createServicesFake } from '@/test/servicesFake';
import { buildMasterService, buildServiceView, serviceIds } from '@/test/servicesFixtures';
import { chooseAction, openActions, renderServices, serviceCard } from './testing';

vi.mock('./lib/polling', () => ({ servicesRunningPollMs: 25, servicesIdlePollMs: 60_000 }));

const button = (row: HTMLElement, name: string) => within(row).getByRole('button', { name });

describe('Services: Run now', () => {
  it('starts the service, shows it running with the data job’s progress, and tells how it ended', async () => {
    const fake = createServicesFake({ autoAdvance: false });
    renderServices({ fake });
    const row = await serviceCard('End-of-day data run');

    await userEvent.click(button(row, 'Run now: End-of-day data run'));

    expect(fake.calls.runs).toEqual([serviceIds.eod]);
    expect(await within(await serviceCard('End-of-day data run')).findByText('Running')).toBeInTheDocument();
    expect(await screen.findByText('Fetching from Dhan: 120 of 500 instruments')).toBeInTheDocument();
    expect(screen.getByRole('progressbar', { name: 'Fetching from Dhan progress' })).toHaveAttribute(
      'aria-valuenow',
      '24',
    );
    expect(button(await serviceCard('End-of-day data run'), 'Run now: End-of-day data run')).toBeDisabled();
    expect(await screen.findByText('End-of-day data run started.')).toBeInTheDocument();

    fake.advance();

    expect(await screen.findByText('End-of-day data run finished.')).toBeInTheDocument();
    const done = await serviceCard('End-of-day data run');
    await waitFor(() => {
      expect(within(done).queryByText('Running')).not.toBeInTheDocument();
    });
    expect(within(done).getByText('Healthy')).toBeInTheDocument();
    expect(within(done).getByText('Manual')).toBeInTheDocument();
    expect(screen.queryByRole('progressbar', { name: /progress/ })).not.toBeInTheDocument();
  });

  it('tells a failed run with its reason, as an error', async () => {
    const fake = createServicesFake({ runOutcome: 'failed' });
    renderServices({ fake });

    await userEvent.click(await screen.findByRole('button', { name: 'Run now: Instrument master refresh' }));

    expect(
      await screen.findByText('Instrument master refresh failed: Dhan refused the access token.'),
    ).toBeInTheDocument();
    const row = await serviceCard('Instrument master refresh');
    expect(await within(row).findByText('Failed')).toBeInTheDocument();
    expect(within(row).getByText('Failed: Dhan refused the access token.')).toBeInTheDocument();
  });

  it('tells a skipped run with its reason', async () => {
    renderServices({ fake: createServicesFake({ runOutcome: 'skipped' }) });

    await userEvent.click(await screen.findByRole('button', { name: 'Run now: End-of-day data run' }));

    expect(
      await screen.findByText('End-of-day data run was skipped: The Data account is not connected.'),
    ).toBeInTheDocument();
  });

  it('works on a paused service too', async () => {
    const fake = createServicesFake({
      services: [buildServiceView({ enabled: false, status: 'paused', nextRun: null }), buildMasterService()],
    });
    renderServices({ fake });

    await userEvent.click(await screen.findByRole('button', { name: 'Run now: End-of-day data run' }));

    expect(fake.calls.runs).toEqual([serviceIds.eod]);
    expect(await screen.findByText('End-of-day data run finished.')).toBeInTheDocument();
    // Still paused afterwards.
    expect(within(await serviceCard('End-of-day data run')).getByText('Paused')).toBeInTheDocument();
  });

  it('shows a service that is running with Run now off', async () => {
    const fake = createServicesFake({ autoAdvance: false });
    fake.startScheduledRun(serviceIds.master);
    renderServices({ fake });

    const row = await serviceCard('Instrument master refresh');
    expect(within(row).getByText('Running')).toBeInTheDocument();
    expect(button(row, 'Run now: Instrument master refresh')).toBeDisabled();
  });

  it('says it when the start is refused because the service is already running', async () => {
    renderServices({
      handlers: [
        http.post('/api/services/:id/run', () =>
          problemResponse({ title: 'It runs.', code: 'services.already_running', status: 409 }, 409),
        ),
      ],
    });

    await userEvent.click(await screen.findByRole('button', { name: 'Run now: End-of-day data run' }));

    expect(
      await screen.findByText('End-of-day data run is already running. This page shows its progress.'),
    ).toBeInTheDocument();
  });

  it('says a start that failed for another reason', async () => {
    renderServices({
      handlers: [
        http.post('/api/services/:id/run', () => problemResponse({ title: 'The scheduler is down.', code: 'x' }, 500)),
      ],
    });

    await userEvent.click(await screen.findByRole('button', { name: 'Run now: End-of-day data run' }));

    expect(await screen.findByText('The scheduler is down.')).toBeInTheDocument();
  });
});

describe('Services: Pause and Resume', () => {
  it('pauses a service: the status says Paused, there is no next run and the menu offers Resume', async () => {
    const fake = createServicesFake();
    renderServices({ fake });

    await chooseAction('End-of-day data run', 'Pause');

    expect(fake.calls.pauses).toEqual([serviceIds.eod]);
    expect(
      await screen.findByText('End-of-day data run paused. It does not run by itself until you resume it.'),
    ).toBeInTheDocument();
    const row = await serviceCard('End-of-day data run');
    expect(await within(row).findByText('Paused')).toBeInTheDocument();
    expect(within(row).getByText('Not scheduled')).toBeInTheDocument();
    const menu = await openActions('End-of-day data run');
    expect(within(menu).getByRole('menuitem', { name: 'Resume' })).toBeInTheDocument();
    expect(within(menu).queryByRole('menuitem', { name: 'Pause' })).not.toBeInTheDocument();
  });

  it('resumes it: Healthy again with the next run', async () => {
    const fake = createServicesFake({
      services: [buildServiceView({ enabled: false, status: 'paused', nextRun: null }), buildMasterService()],
    });
    renderServices({ fake });

    await chooseAction('End-of-day data run', 'Resume');

    expect(fake.calls.resumes).toEqual([serviceIds.eod]);
    expect(await screen.findByText('End-of-day data run resumed.')).toBeInTheDocument();
    const row = await serviceCard('End-of-day data run');
    expect(await within(row).findByText('Healthy')).toBeInTheDocument();
    expect(within(row).getByText('Tue 13 Oct 15:50')).toBeInTheDocument();
    expect(
      within(await openActions('End-of-day data run')).getByRole('menuitem', { name: 'Pause' }),
    ).toBeInTheDocument();
  });

  it('says a pause that failed', async () => {
    renderServices({
      handlers: [http.post('/api/services/:id/pause', () => problemResponse({ title: 'Nope.', code: 'x' }, 500))],
    });

    await chooseAction('End-of-day data run', 'Pause');

    expect(await screen.findByText('Nope.')).toBeInTheDocument();
    expect(within(await serviceCard('End-of-day data run')).getByText('Healthy')).toBeInTheDocument();
  });
});

describe('Services: a run that starts by itself', () => {
  it('is noticed while the page is open, and its end is told like any other', async () => {
    const fake = createServicesFake({ autoAdvance: false });
    fake.startScheduledRun(serviceIds.eod);
    renderServices({ fake });

    // Open while it runs: the page follows it and reads again every moment.
    const row = await serviceCard('End-of-day data run');
    expect(within(row).getByText('Running')).toBeInTheDocument();
    expect(await screen.findByText('Fetching from Dhan: 120 of 500 instruments')).toBeInTheDocument();

    fake.advance();

    expect(await screen.findByText('End-of-day data run finished.')).toBeInTheDocument();
    await waitFor(() => {
      expect(within(row).queryByText('Running')).not.toBeInTheDocument();
    });
  });
});
