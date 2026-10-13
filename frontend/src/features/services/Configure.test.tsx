import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http } from 'msw';
import { problemResponse } from '@/test/server';
import { createServicesFake } from '@/test/servicesFake';
import { buildMasterService, buildServiceView, serviceIds } from '@/test/servicesFixtures';
import { openConfigure, renderServices, serviceRow } from './testing';

const preview = () => screen.getByRole('region', { name: 'Preview' });
const save = () => screen.getByRole('button', { name: 'Save schedule' });

/** Replaces the value of a field the way an owner does: select everything and type. */
async function enter(label: string, value: string) {
  const input = screen.getByLabelText(label);
  await userEvent.clear(input);
  await userEvent.type(input, value);
}

describe('Configure: what the dialog starts with', () => {
  it('shows the service, its type, time, days, execution settings and parameters', async () => {
    renderServices();

    const dialog = await openConfigure('End-of-day data run');

    expect(within(dialog).getByText(/Loads the day from Dhan/)).toBeInTheDocument();
    expect(within(dialog).getByText('service identity')).toBeInTheDocument();
    expect(within(dialog).getByLabelText('Schedule type')).toHaveValue('daily');
    expect(within(dialog).getByLabelText('Time')).toHaveValue('15:50');
    expect(within(dialog).getByRole('switch', { name: 'Enabled' })).toBeChecked();
    expect(within(dialog).getByRole('switch', { name: /Trading days only/ })).toBeChecked();
    expect(within(dialog).getByLabelText('Retries')).toHaveValue(2);
    expect(within(dialog).getByLabelText('Timeout (min)')).toHaveValue(60);
    expect(within(dialog).getByRole('switch', { name: 'Catch up missed runs' })).toBeChecked();
    expect(within(dialog).getByRole('switch', { name: 'Notify on failure' })).toBeChecked();
    expect(within(dialog).getByLabelText('Gap look-back')).toHaveValue(5);
    expect(within(dialog).getByText('Sessions looked at again for missing bars (1–30 days)')).toBeInTheDocument();
    expect(within(dialog).getByRole('switch', { name: 'Fetch gaps again' })).toBeChecked();
  });

  it('offers only the schedule types the service allows', async () => {
    renderServices();

    const dialog = await openConfigure('End-of-day data run');

    const options = within(within(dialog).getByLabelText('Schedule type')).getAllByRole('option');
    expect(options.map((o) => o.textContent)).toEqual(['Daily at a time', 'Weekly', 'After another service']);
  });

  it('shows the Monday-to-Friday chips pressed but off while trading days only is on', async () => {
    renderServices();

    const dialog = await openConfigure('End-of-day data run');

    const group = within(dialog).getByRole('group', { name: 'Days' });
    for (const day of ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday']) {
      expect(within(group).getByRole('button', { name: day })).toHaveAttribute('aria-pressed', 'true');
    }
    for (const day of ['Saturday', 'Sunday']) {
      expect(within(group).getByRole('button', { name: day })).toHaveAttribute('aria-pressed', 'false');
    }
    expect(within(group).getByRole('button', { name: 'Friday' })).toHaveAttribute('aria-disabled', 'true');
  });

  it('shows the words and the next three runs for what is stored, in India time', async () => {
    renderServices();

    await openConfigure('End-of-day data run');

    const panel = preview();
    expect(await within(panel).findByText('Every trading day at 15:50 IST')).toBeInTheDocument();
    const times = within(panel).getAllByText(/^(Tue|Wed|Thu) 1[345] Oct 15:50$/);
    expect(times.map((t) => t.textContent)).toEqual(['Tue 13 Oct 15:50', 'Wed 14 Oct 15:50', 'Thu 15 Oct 15:50']);
    expect(within(panel).getByText('No conflicts with other services.')).toBeInTheDocument();
  });

  it('has Cancel and the close button, which leave without saving', async () => {
    const { fake } = renderServices();
    await openConfigure('End-of-day data run');

    await userEvent.click(screen.getByRole('button', { name: 'Cancel' }));

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(fake.calls.saves).toHaveLength(0);
  });
});

describe('Configure: saving', () => {
  it('asks for the preview as the owner types (debounced) and shows a warning without blocking the save', async () => {
    const { fake } = renderServices();
    await openConfigure('End-of-day data run');
    await within(preview()).findByText('No conflicts with other services.');
    const before = fake.calls.previews.length;

    await userEvent.clear(screen.getByLabelText('Time'));
    await userEvent.type(screen.getByLabelText('Time'), '15:20');

    // One call for the settled draft, not one per key.
    await within(preview()).findByText('Every trading day at 15:20 IST');
    expect(fake.calls.previews.length - before).toBeLessThanOrEqual(2);
    const last = fake.calls.previews.at(-1)!.body.schedule;
    expect(last).toMatchObject({ type: 'daily', time: '15:20', tradingDaysOnly: true });
    const list = within(preview()).getByRole('list', { name: 'Schedule checks' });
    expect(list).toHaveTextContent(
      'End-of-day data run · Time: The end-of-day fetch before 15:35 may miss the last bars of the session.',
    );
    expect(within(list).getByText('Warning:')).toBeInTheDocument();
    expect(save()).toBeEnabled();
  });

  it('saves the draft with the revision it was read at, then closes with a toast and shows the new schedule', async () => {
    const { fake } = renderServices();
    await openConfigure('End-of-day data run');
    await within(preview()).findByText('Every trading day at 15:50 IST');

    await enter('Time', '16:05');
    await enter('Retries', '3');
    await enter('Timeout (min)', '45');
    await userEvent.click(screen.getByRole('switch', { name: 'Catch up missed runs' }));
    await userEvent.click(screen.getByRole('switch', { name: 'Notify on failure' }));
    await enter('Gap look-back', '10');
    await userEvent.click(screen.getByRole('switch', { name: 'Fetch gaps again' }));
    await within(preview()).findByText('Every trading day at 16:05 IST');
    await userEvent.click(save());

    expect(await screen.findByText('Schedule saved — the service uses it from now on.')).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: /Configure/ })).not.toBeInTheDocument();
    });
    expect(fake.calls.saves).toHaveLength(1);
    expect(fake.calls.saves[0]).toEqual({
      id: serviceIds.eod,
      body: {
        enabled: true,
        schedule: {
          type: 'daily',
          time: '16:05',
          days: ['mon', 'tue', 'wed', 'thu', 'fri'],
          tradingDaysOnly: true,
        },
        retries: 3,
        timeoutMinutes: 45,
        catchUp: false,
        notifyOnFailure: false,
        parameters: { lookbackDays: 10, fetchGaps: false },
        revision: 3,
      },
    });
    const row = await serviceRow('End-of-day data run');
    expect(await within(row).findByText('Every trading day at 16:05 IST')).toBeInTheDocument();
    expect(within(row).getByText('retries 3 · timeout 45 min')).toBeInTheDocument();
  });

  it('saves a paused service: enabled off', async () => {
    const { fake } = renderServices();
    await openConfigure('End-of-day data run');

    await userEvent.click(screen.getByRole('switch', { name: 'Enabled' }));
    await userEvent.click(save());

    await waitFor(() => {
      expect(fake.calls.saves).toHaveLength(1);
    });
    expect(fake.calls.saves[0]!.body.enabled).toBe(false);
    expect(within(await serviceRow('End-of-day data run')).getByText('Paused')).toBeInTheDocument();
  });

  it('keeps the warnings on the list after saving: the banner names them', async () => {
    renderServices();
    await openConfigure('End-of-day data run');
    await enter('Time', '15:20');
    await within(preview()).findByText('Every trading day at 15:20 IST');

    await userEvent.click(save());

    expect(await screen.findByText('Schedule checks: 1 warning')).toBeInTheDocument();
    const row = await serviceRow('End-of-day data run');
    expect(within(row).getByRole('list', { name: 'Schedule checks' })).toHaveTextContent('before 15:35');
  });

  it('shows what the server refuses on the field, and keeps the dialog open', async () => {
    let saves = 0;
    renderServices({
      handlers: [
        http.put('/api/services/:id/schedule', () => {
          saves += 1;
          return problemResponse(
            {
              title: 'The schedule is not valid.',
              code: 'services.invalid',
              status: 400,
              issues: [
                {
                  field: 'retries',
                  severity: 'error',
                  code: 'services.retries',
                  message: 'Retries are limited by the policy.',
                },
                {
                  serviceId: serviceIds.master,
                  field: 'schedule.after',
                  severity: 'error',
                  code: 'services.circular',
                  message: 'That would make a circle.',
                },
              ],
            },
            400,
          );
        }),
      ],
    });
    await openConfigure('End-of-day data run');
    await userEvent.click(save());

    expect(await screen.findByText('Retries are limited by the policy.')).toBeInTheDocument();
    expect(screen.getByLabelText('Retries')).toBeInvalid();
    // The finding about another service is listed under the preview, named.
    expect(within(preview()).getByRole('list', { name: 'Schedule checks' })).toHaveTextContent(
      'Instrument master refresh · Run after: That would make a circle.',
    );
    expect(save()).toBeDisabled();
    expect(screen.getByRole('dialog', { name: /Configure/ })).toBeInTheDocument();
    expect(saves).toBe(1);

    // Changing the draft lets the owner try again.
    await enter('Retries', '1');
    await waitFor(() => {
      expect(save()).toBeEnabled();
    });
  });

  it('says a service changed elsewhere, and Reload starts over from what is stored now', async () => {
    const { fake } = renderServices();
    await openConfigure('End-of-day data run');
    fake.changeElsewhere(serviceIds.eod, { time: '15:55' });

    await userEvent.click(save());

    expect(await screen.findByText(/changed elsewhere — reload/)).toBeInTheDocument();
    expect(screen.getByRole('dialog', { name: /Configure/ })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Reload' }));

    await waitFor(() => {
      expect(screen.getByLabelText('Time')).toHaveValue('15:55');
    });
    expect(screen.queryByText(/changed elsewhere — reload/)).not.toBeInTheDocument();
    await userEvent.click(save());
    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: /Configure/ })).not.toBeInTheDocument();
    });
    expect(fake.calls.saves.at(-1)!.body.revision).toBe(4);
  });

  it('says a save that failed for another reason', async () => {
    renderServices({
      handlers: [
        http.put('/api/services/:id/schedule', () =>
          problemResponse({ title: 'The database is busy.', code: 'x' }, 500),
        ),
      ],
    });
    await openConfigure('End-of-day data run');

    await userEvent.click(save());

    expect(await screen.findByText('The database is busy.')).toBeInTheDocument();
    expect(save()).toBeEnabled();
  });
});

describe('Configure: restore default', () => {
  it('is off while the service has its defaults', async () => {
    renderServices();
    await openConfigure('End-of-day data run');

    expect(screen.getByRole('button', { name: 'Restore default' })).toBeDisabled();
  });

  it('writes the defaults, closes and says so', async () => {
    const fake = createServicesFake({
      services: [
        buildServiceView({
          isDefault: false,
          schedule: { type: 'daily', time: '17:00', days: ['mon'], tradingDaysOnly: false },
          scheduleWords: 'Daily at 17:00 IST',
        }),
        buildMasterService(),
      ],
    });
    renderServices({ fake });
    await openConfigure('End-of-day data run');

    await userEvent.click(screen.getByRole('button', { name: 'Restore default' }));

    expect(await screen.findByText('Default schedule restored.')).toBeInTheDocument();
    expect(fake.calls.resets).toEqual([serviceIds.eod]);
    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: /Configure/ })).not.toBeInTheDocument();
    });
  });

  it('shows why the defaults cannot be written', async () => {
    renderServices({
      fake: createServicesFake({ services: [buildServiceView({ isDefault: false })] }),
      handlers: [
        http.post('/api/services/:id/schedule/reset', () =>
          problemResponse(
            {
              title: 'The defaults break a rule.',
              code: 'services.invalid',
              status: 400,
              issues: [
                {
                  serviceId: serviceIds.eod,
                  field: 'schedule.after',
                  severity: 'error',
                  code: 'c',
                  message: 'Circular.',
                },
              ],
            },
            400,
          ),
        ),
      ],
    });
    await openConfigure('End-of-day data run');

    await userEvent.click(screen.getByRole('button', { name: 'Restore default' }));

    expect(await screen.findByText(/Run after: Circular\./)).toBeInTheDocument();
    expect(screen.getByRole('dialog', { name: /Configure/ })).toBeInTheDocument();
  });
});

describe('Configure: the preview', () => {
  it('says the checks could not be run when the server does not answer, and still allows saving', async () => {
    const fake = createServicesFake();
    fake.failures.preview = true;
    renderServices({ fake });
    await openConfigure('End-of-day data run');

    expect(
      await screen.findByText('The checks could not be run. The server checks again when you save.'),
    ).toBeInTheDocument();
    expect(save()).toBeEnabled();
  });

  it('does not ask while a field is incomplete, and says what is missing', async () => {
    const { fake } = renderServices();
    await openConfigure('End-of-day data run');
    await within(preview()).findByText('Every trading day at 15:50 IST');
    const before = fake.calls.previews.length;

    await userEvent.clear(screen.getByLabelText('Retries'));

    expect(await screen.findByText('Between 0 and 5.')).toBeInTheDocument();
    expect(screen.getByLabelText('Retries')).toBeInvalid();
    expect(screen.getByText('Complete the highlighted fields to see the next runs.')).toBeInTheDocument();
    expect(save()).toBeDisabled();
    await new Promise((resolve) => setTimeout(resolve, 600));
    expect(fake.calls.previews).toHaveLength(before);
  });
});
