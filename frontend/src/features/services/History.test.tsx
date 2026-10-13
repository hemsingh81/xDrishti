import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { scrollToEnd } from '@/test/intersection';
import { createServicesFake } from '@/test/servicesFake';
import {
  buildFailedRun,
  buildMasterService,
  buildRunHistory,
  buildServiceRun,
  buildServiceView,
  serviceIds,
} from '@/test/servicesFixtures';
import { smallPage, threePages } from '@/test/smallPages';
import { renderServices } from './testing';

// Lists page by a few rows here instead of 100 (the default of 100 is asserted in useInfiniteRows.test.tsx).
vi.mock('@/shared/lib/useInfiniteRows', async (importOriginal) =>
  (await import('@/test/smallPages')).withSmallPages(await importOriginal()),
);

const shown = (count: number) => `Showing ${String(count)} of ${String(threePages)}`;
const openHistory = async (name: string) => {
  await userEvent.click(await screen.findByRole('button', { name: `History: ${name}` }));
  return screen.findByRole('dialog', { name: 'Run history' });
};

describe('Services: the History drawer', () => {
  it('lists the runs newest first with trigger, status, start, duration and message', async () => {
    renderServices({
      fake: createServicesFake({
        runs: {
          [serviceIds.eod]: [
            buildFailedRun({ id: '00000000-0000-4000-8000-000000000931', attempt: 2, trigger: 'catch_up' }),
            buildServiceRun({ trigger: 'manual', message: 'Fetched 41,200 bars.' }),
          ],
        },
      }),
    });

    const drawer = await openHistory('End-of-day data run');

    expect(within(drawer).getByRole('heading', { name: 'End-of-day data run — run history' })).toBeInTheDocument();
    const grid = await within(drawer).findByRole('grid', { name: 'Runs' });
    const rows = within(grid).getAllByRole('row').slice(1);
    expect(rows).toHaveLength(2);
    const [failed, fine] = rows as [HTMLElement, HTMLElement];
    expect(within(failed).getByText('Mon 12 Oct 15:50 · attempt 2')).toHaveAttribute(
      'datetime',
      '2026-10-12T15:50:00+05:30',
    );
    expect(within(failed).getByText('Catch-up')).toBeInTheDocument();
    expect(within(failed).getByText('Failed')).toBeInTheDocument();
    expect(within(failed).getByTestId('ErrorOutlineOutlinedIcon')).toBeInTheDocument();
    expect(within(failed).getByText('1m 0s')).toBeInTheDocument();
    expect(within(failed).getByText('Dhan refused the access token.')).toBeInTheDocument();
    expect(within(fine).getByText('Manual')).toBeInTheDocument();
    expect(within(fine).getByText('Succeeded')).toBeInTheDocument();
    expect(within(fine).getByText('8m 0s')).toBeInTheDocument();
    expect(within(fine).getByText('Fetched 41,200 bars.')).toBeInTheDocument();
  });

  it('shows a run that is still going without a duration', async () => {
    renderServices({
      fake: createServicesFake({
        services: [
          buildServiceView({ status: 'running', lastRun: buildServiceRun({ status: 'running', finishedAt: null }) }),
        ],
      }),
    });

    const drawer = await openHistory('End-of-day data run');

    const [row] = within(await within(drawer).findByRole('grid', { name: 'Runs' }))
      .getAllByRole('row')
      .slice(1);
    expect(within(row!).getByText('Running')).toBeInTheDocument();
    // The duration column (the fourth) is empty; the message is too.
    expect(within(row!).getAllByRole('gridcell')[3]).toHaveTextContent('—');
  });

  it('says there are no runs yet', async () => {
    renderServices({
      fake: createServicesFake({ services: [buildServiceView({ lastRun: null }), buildMasterService()] }),
    });

    const drawer = await openHistory('End-of-day data run');

    expect(await within(drawer).findByText('No runs yet')).toBeInTheDocument();
  });

  it('loads the next page when the end of the list is reached, a page at a time', async () => {
    const fake = createServicesFake({ runs: { [serviceIds.eod]: buildRunHistory(serviceIds.eod, threePages) } });
    renderServices({ fake });
    const drawer = await openHistory('End-of-day data run');

    await within(drawer).findByText(shown(smallPage));
    expect(within(drawer).getByRole('grid', { name: 'Runs' })).toHaveAttribute('aria-rowcount', String(threePages + 1));
    expect(within(drawer).getByText('Run 1')).toBeInTheDocument();
    expect(within(drawer).queryByText(`Run ${String(smallPage + 1)}`)).not.toBeInTheDocument();

    scrollToEnd();
    await within(drawer).findByText(shown(smallPage * 2));
    scrollToEnd();
    await within(drawer).findByText(shown(threePages));

    expect(within(drawer).getByText(`Run ${String(threePages)}`)).toBeInTheDocument();
    expect(fake.calls.runPages.map((c) => c.page)).toEqual([1, 2, 3]);
    expect(fake.calls.runPages.every((c) => c.id === serviceIds.eod && c.pageSize === smallPage)).toBe(true);
  });

  it('says the runs could not be loaded and loads them on Retry', async () => {
    const fake = createServicesFake();
    fake.failures.runs = true;
    renderServices({ fake });
    const drawer = await openHistory('End-of-day data run');

    expect(await within(drawer).findByText('The services are not available.')).toBeInTheDocument();
    fake.failures.runs = false;
    await userEvent.click(within(drawer).getByRole('button', { name: 'Retry' }));

    expect(await within(drawer).findByRole('grid', { name: 'Runs' })).toBeInTheDocument();
    expect(within(drawer).getByText('Succeeded')).toBeInTheDocument();
  });

  it('closes with its button, and shows the runs of another service when opened for it', async () => {
    const fake = createServicesFake({
      runs: { [serviceIds.master]: [buildServiceRun({ serviceId: serviceIds.master, message: 'Master refreshed.' })] },
    });
    renderServices({ fake });
    const drawer = await openHistory('End-of-day data run');
    await within(drawer).findByRole('grid', { name: 'Runs' });

    await userEvent.click(within(drawer).getByRole('button', { name: 'Close run history' }));
    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: 'Run history' })).not.toBeInTheDocument();
    });
    const other = await openHistory('Instrument master refresh');

    expect(await within(other).findByText('Master refreshed.')).toBeInTheDocument();
    expect(fake.calls.runPages.map((c) => c.id)).toEqual([serviceIds.eod, serviceIds.master]);
  });

  it('has no History button on the entry that is set elsewhere', async () => {
    renderServices();

    await screen.findByRole('grid', { name: 'Services' });
    expect(screen.queryByRole('button', { name: 'History: Account & portfolio sync' })).not.toBeInTheDocument();
  });
});
