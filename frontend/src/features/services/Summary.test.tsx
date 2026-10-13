import { screen, within } from '@testing-library/react';
import { appliedCss } from '@/test/css';
import { createServicesFake } from '@/test/servicesFake';
import { buildFailedRun, buildMasterService, buildServiceView } from '@/test/servicesFixtures';
import { renderServices, setNow } from './testing';

afterEach(() => {
  vi.restoreAllMocks();
});

const strip = () => screen.findByRole('region', { name: 'Services summary' });
const counts = (summary: HTMLElement) =>
  within(within(summary).getByRole('list', { name: 'Service counts' }))
    .getAllByRole('listitem')
    .map((item) => item.textContent);

describe('Services: the summary strip', () => {
  it('counts the services, with a word for each count', async () => {
    renderServices();

    expect(counts(await strip())).toEqual(['3Services', '3Healthy', '0Paused', '0Failed']);
  });

  it('counts healthy, running, paused and failed services', async () => {
    renderServices({
      fake: createServicesFake({
        services: [
          buildServiceView(),
          buildServiceView({ id: 'feed', name: 'Live feed', status: 'running' }),
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

    expect(counts(await strip())).toEqual(['4Services', '1Healthy', '1Running', '1Paused', '1Failed']);
  });

  it('names the next run of all services with a countdown, and the last run with how it ended', async () => {
    // 14:05 in India.
    setNow('2026-10-13T08:35:00Z');
    renderServices();

    const summary = await strip();
    // The earliest of the three: the end-of-day run at 15:50 (the account sync is 16:00).
    const next = within(summary).getByText('Next run').parentElement!;
    expect(within(next).getByText('Tue 13 Oct 15:50')).toHaveAttribute('datetime', '2026-10-13T15:50:00+05:30');
    expect(within(next).getByText('End-of-day data run')).toBeInTheDocument();
    expect(within(next).getByText('in 1 h 45 min')).toBeInTheDocument();
    // The latest run to end: the instrument refresh on 12 Oct at 18:31.
    const last = within(summary).getByText('Last run').parentElement!;
    expect(within(last).getByText('Mon 12 Oct 18:31')).toBeInTheDocument();
    expect(within(last).getByText('Instrument master refresh')).toBeInTheDocument();
    expect(within(last).getByText('Succeeded')).toBeInTheDocument();
  });

  it('says a failed last run as a word', async () => {
    renderServices({
      fake: createServicesFake({
        services: [buildServiceView({ status: 'failed', lastRun: buildFailedRun() })],
      }),
    });

    const last = within(await strip()).getByText('Last run').parentElement!;
    expect(within(last).getByText('Failed')).toBeInTheDocument();
  });

  it('says so when nothing is scheduled and nothing has run', async () => {
    renderServices({
      fake: createServicesFake({
        services: [buildServiceView({ enabled: false, status: 'paused', nextRun: null, lastRun: null })],
      }),
    });

    const summary = await strip();
    expect(within(summary).getByText('Nothing is scheduled')).toBeInTheDocument();
    expect(within(summary).getByText('Nothing has run yet')).toBeInTheDocument();
  });

  it('shows zero counts and the empty lines when there are no services', async () => {
    renderServices({ fake: createServicesFake({ services: [] }) });

    await screen.findByText('No services');
    const summary = await strip();
    expect(counts(summary)).toEqual(['0Services', '0Healthy', '0Paused', '0Failed']);
    expect(within(summary).getByText('Nothing is scheduled')).toBeInTheDocument();
  });

  it('puts the counts, the next run and the last run on one line from the theme’s lg breakpoint', async () => {
    renderServices();

    const { responsive } = appliedCss(await strip());

    expect(
      responsive.some(
        (r) => r.query.replace(/\s/g, '').includes('min-width:1200px') && r.css.includes('grid-template-columns'),
      ),
    ).toBe(true);
  });
});
