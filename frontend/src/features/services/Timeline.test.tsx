import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { createServicesFake } from '@/test/servicesFake';
import {
  buildMasterService,
  buildScheduleSpec,
  buildServiceView,
  buildTimeline,
  buildTimelineEntry,
  serviceIds,
} from '@/test/servicesFixtures';
import { renderServices } from './testing';

const card = () => screen.findByRole('region', { name: 'Trading-day timeline' });

describe('Services: the trading-day timeline', () => {
  it('says the date, the market hours and the review cutoff in words', async () => {
    renderServices();

    const timeline = await card();
    expect(
      await within(timeline).findByText('Tue 13 Oct 2026 · market 09:15–15:30 · review cutoff 16:30'),
    ).toBeInTheDocument();
    expect(within(timeline).getByRole('heading', { level: 2, name: 'Trading-day timeline' })).toBeInTheDocument();
  });

  it('shows every start as a button whose name says what and when', async () => {
    renderServices();

    const timeline = await card();
    const eod = await within(timeline).findByRole('button', { name: 'End-of-day data run starts at 15:50' });
    expect(
      within(timeline).getByRole('button', { name: 'Instrument master refresh starts at 18:30' }),
    ).toBeInTheDocument();
    // The label printed next to the dot carries the time and the name.
    expect(eod).toHaveTextContent('15:50 End-of-day data run');
  });

  it('can be reached with the keyboard, and the tooltip says the same', async () => {
    renderServices();

    const timeline = await card();
    const eod = await within(timeline).findByRole('button', { name: 'End-of-day data run starts at 15:50' });
    await userEvent.tab();
    // Tab order: the first mark is the first stop inside the card.
    let guard = 0;
    while (document.activeElement !== eod && guard < 20) {
      await userEvent.tab();
      guard += 1;
    }
    expect(eod).toHaveFocus();
    expect(await screen.findByRole('tooltip')).toHaveTextContent('End-of-day data run starts at 15:50');
  });

  it('shows a window and an interval as bands that say how long they are up', async () => {
    renderServices({
      fake: createServicesFake({
        timeline: {
          entries: [
            buildTimelineEntry({
              serviceId: 'feed',
              name: 'Live feed',
              kind: 'band',
              marks: [],
              band: { from: '09:00', to: '15:45' },
            }),
            buildTimelineEntry({
              serviceId: 'alerts',
              name: 'Price alerts',
              kind: 'band',
              marks: [],
              band: { from: '09:15', to: '15:30' },
              everyMinutes: 5,
            }),
            buildTimelineEntry({
              serviceId: 'watcher',
              name: 'CSV watcher',
              kind: 'band',
              marks: [],
              band: { from: '00:00', to: '24:00' },
            }),
          ],
        },
      }),
    });

    const timeline = await card();
    expect(
      await within(timeline).findByRole('button', { name: 'Live feed is up from 09:00 to 15:45' }),
    ).toBeInTheDocument();
    expect(
      within(timeline).getByRole('button', { name: 'Price alerts runs every 5 minutes from 09:15 to 15:30' }),
    ).toBeInTheDocument();
    expect(within(timeline).getByRole('button', { name: 'CSV watcher is up all day' })).toBeInTheDocument();
  });

  it('marks a run-after service with the service it follows', async () => {
    renderServices({
      fake: createServicesFake({
        services: [
          buildServiceView(),
          buildMasterService({
            schedule: { type: 'after', after: serviceIds.eod, tradingDaysOnly: false },
            scheduleWords: 'After End-of-day data run finishes',
            nextRun: null,
          }),
        ],
      }),
    });

    const timeline = await card();
    const after = await within(timeline).findByRole('button', {
      name: 'Instrument master refresh starts after End-of-day data run finishes, about 15:50',
    });
    expect(after).toHaveTextContent('↳ 15:50 Instrument master refresh');
  });

  it('keeps labels of nearby starts apart by putting them in different rows', async () => {
    renderServices({
      fake: createServicesFake({
        services: [
          buildServiceView({ schedule: buildScheduleSpec({ time: '15:50' }) }),
          buildMasterService({ schedule: buildScheduleSpec({ time: '15:55' }) }),
        ],
      }),
    });

    const timeline = await card();
    const first = await within(timeline).findByRole('button', { name: 'End-of-day data run starts at 15:50' });
    const second = within(timeline).getByRole('button', { name: 'Instrument master refresh starts at 15:55' });
    expect(first.style.top || getComputedStyle(first).top).not.toBe(getComputedStyle(second).top);
  });

  it('says so when the date is not a trading day', async () => {
    renderServices({
      fake: createServicesFake({
        timeline: buildTimeline({
          date: '2026-10-17',
          tradingDay: false,
          marketOpen: null,
          marketClose: null,
          entries: [],
        }),
      }),
    });

    const timeline = await card();
    expect(
      await within(timeline).findByText(/Sat 17 Oct 2026 is not a trading day: the market is closed/),
    ).toBeInTheDocument();
    expect(within(timeline).getByText('Sat 17 Oct 2026 · no market session · review cutoff 16:30')).toBeInTheDocument();
  });

  it('shows a loading state, then an error with Retry that loads the timeline', async () => {
    const fake = createServicesFake();
    fake.failures.timeline = true;
    renderServices({ fake });

    const timeline = await card();
    expect(await within(timeline).findByText('The services are not available.')).toBeInTheDocument();
    // The services table is still there.
    expect(await screen.findByRole('grid', { name: 'Services' })).toBeInTheDocument();
    fake.failures.timeline = false;
    await userEvent.click(within(timeline).getByRole('button', { name: 'Retry' }));
    expect(
      await within(timeline).findByRole('button', { name: 'End-of-day data run starts at 15:50' }),
    ).toBeInTheDocument();
  });

  it('shows a loading indicator while the timeline is read', async () => {
    renderServices({ handlers: [http.get('/api/services/timeline', () => new Promise<never>(() => undefined))] });

    expect(await within(await card()).findByRole('progressbar', { name: 'Loading the timeline' })).toBeInTheDocument();
    expect(await screen.findByRole('grid', { name: 'Services' })).toBeInTheDocument();
  });

  it('shows nothing but the track when no service is enabled', async () => {
    renderServices({
      fake: createServicesFake({ timeline: { entries: [] } }),
      handlers: [http.get('/api/services/timeline', () => HttpResponse.json(buildTimeline({ entries: [] })))],
    });

    const timeline = await card();
    await within(timeline).findByText(/market 09:15–15:30/);
    expect(within(timeline).queryAllByRole('button')).toHaveLength(0);
  });
});
