import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createServicesFake } from '@/test/servicesFake';
import { buildMasterService, buildScheduleSpec, buildServiceView, serviceIds } from '@/test/servicesFixtures';
import { openConfigure, renderServices } from './testing';

const preview = () => screen.getByRole('region', { name: 'Preview' });
const save = () => screen.getByRole('button', { name: 'Save schedule' });
const days = () => within(screen.getByRole('group', { name: 'Days' }));

async function enter(label: string, value: string) {
  const input = screen.getByLabelText(label);
  await userEvent.clear(input);
  await userEvent.type(input, value);
}

/** Opens Configure for the instrument refresh, which allows every schedule type. */
async function openMaster(services = [buildServiceView(), buildMasterService()]) {
  const result = renderServices({ fake: createServicesFake({ services }) });
  const dialog = await openConfigure('Instrument master refresh');
  await within(preview()).findByText('Daily at 18:30 IST');
  return { ...result, dialog };
}

describe('Configure: the fields of each schedule type', () => {
  it('daily: a time, the days as chips and the trading-days switch', async () => {
    const { dialog } = await openMaster();

    expect(within(dialog).getByLabelText('Time')).toHaveValue('18:30');
    expect(within(dialog).getByRole('switch', { name: /Trading days only/ })).not.toBeChecked();
    for (const name of ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday']) {
      expect(days().getByRole('button', { name })).toHaveAttribute('aria-pressed', 'true');
    }
  });

  it('weekly: a weekday and a time', async () => {
    const { fake } = await openMaster();

    await userEvent.selectOptions(screen.getByLabelText('Schedule type'), 'weekly');
    expect(screen.getByLabelText('Day')).toHaveValue('mon');
    expect(screen.queryByRole('group', { name: 'Days' })).not.toBeInTheDocument();
    await userEvent.selectOptions(screen.getByLabelText('Day'), 'sat');
    await enter('Time', '10:00');

    expect(await within(preview()).findByText('Every Saturday at 10:00 IST')).toBeInTheDocument();
    expect(within(preview()).getByText('Sat 17 Oct 10:00')).toBeInTheDocument();
    expect(fake.calls.previews.at(-1)!.body.schedule).toEqual({
      type: 'weekly',
      time: '10:00',
      weekday: 'sat',
      tradingDaysOnly: false,
    });
  });

  it('after: a select of the other services that can finish, and a sentence instead of times', async () => {
    const { fake } = await openMaster();

    await userEvent.selectOptions(screen.getByLabelText('Schedule type'), 'after');

    const select = screen.getByLabelText('Run after');
    // Not itself, and not the entry that is set elsewhere.
    expect(
      within(select)
        .getAllByRole('option')
        .map((o) => o.textContent),
    ).toEqual(['End-of-day data run']);
    expect(select).toHaveValue(serviceIds.eod);
    expect(screen.getByText('Runs automatically each time that service finishes successfully.')).toBeInTheDocument();
    expect(await within(preview()).findByText('After End-of-day data run finishes')).toBeInTheDocument();
    expect(
      within(preview()).getByText('Runs each time End-of-day data run finishes successfully.'),
    ).toBeInTheDocument();
    expect(fake.calls.previews.at(-1)!.body.schedule).toEqual({
      type: 'after',
      after: serviceIds.eod,
      tradingDaysOnly: false,
    });
  });

  it('window: from and to, the days and no interval', async () => {
    const { fake } = await openMaster();

    await userEvent.selectOptions(screen.getByLabelText('Schedule type'), 'window');

    expect(screen.getByLabelText('From')).toHaveValue('09:15');
    expect(screen.getByLabelText('To')).toHaveValue('15:30');
    expect(screen.queryByLabelText('Every (minutes)')).not.toBeInTheDocument();
    expect(days().getByRole('button', { name: 'Monday' })).toBeInTheDocument();
    expect(await within(preview()).findByText('Up from 09:15 to 15:30 IST')).toBeInTheDocument();
    expect(fake.calls.previews.at(-1)!.body.schedule).toMatchObject({ type: 'window', from: '09:15', to: '15:30' });
  });

  it('interval: from, to and every N minutes, with the next runs', async () => {
    const { fake } = await openMaster();

    await userEvent.selectOptions(screen.getByLabelText('Schedule type'), 'interval');
    expect(screen.getByLabelText('Every (minutes)')).toHaveValue(5);
    await enter('Every (minutes)', '15');

    expect(await within(preview()).findByText('Every 15 minutes from 09:15 to 15:30 IST')).toBeInTheDocument();
    expect(within(preview()).getByText('Tue 13 Oct 09:15')).toBeInTheDocument();
    expect(within(preview()).getByText('Tue 13 Oct 09:30')).toBeInTheDocument();
    expect(fake.calls.previews.at(-1)!.body.schedule).toMatchObject({ type: 'interval', everyMinutes: 15 });
  });

  it('continuous: no fields, and the service is always up', async () => {
    await openMaster();

    await userEvent.selectOptions(screen.getByLabelText('Schedule type'), 'continuous');

    expect(screen.getByText('Runs continuously (it is always up).')).toBeInTheDocument();
    expect(screen.queryByLabelText('Time')).not.toBeInTheDocument();
    expect(await within(preview()).findByText('Always on')).toBeInTheDocument();
    expect(within(preview()).getByText('Always up.')).toBeInTheDocument();
  });

  it('puts the saved type and its fields in the request and in the table', async () => {
    const { fake } = await openMaster();
    await userEvent.selectOptions(screen.getByLabelText('Schedule type'), 'weekly');
    await userEvent.selectOptions(screen.getByLabelText('Day'), 'sun');
    await enter('Time', '09:30');
    await within(preview()).findByText('Every Sunday at 09:30 IST');

    await userEvent.click(save());

    await waitFor(() => {
      expect(fake.calls.saves).toHaveLength(1);
    });
    expect(fake.calls.saves[0]!.body.schedule).toEqual({
      type: 'weekly',
      time: '09:30',
      weekday: 'sun',
      tradingDaysOnly: false,
    });
    expect(await screen.findByText('Every Sunday at 09:30 IST')).toBeInTheDocument();
  });
});

describe('Configure: days', () => {
  it('toggles a day with its chip', async () => {
    const { fake } = await openMaster();

    await userEvent.click(days().getByRole('button', { name: 'Saturday' }));
    await userEvent.click(days().getByRole('button', { name: 'Sunday' }));

    expect(days().getByRole('button', { name: 'Saturday' })).toHaveAttribute('aria-pressed', 'false');
    await waitFor(() => {
      expect(fake.calls.previews.at(-1)!.body.schedule.days).toEqual(['mon', 'tue', 'wed', 'thu', 'fri']);
    });
  });

  it('"Trading days" chooses Monday to Friday and switches trading days only on; "Every day" undoes it', async () => {
    await openMaster();

    await userEvent.click(screen.getByRole('button', { name: 'Trading days' }));

    expect(screen.getByRole('switch', { name: /Trading days only/ })).toBeChecked();
    // While on, the days are Monday to Friday and cannot be picked one by one.
    expect(days().getByRole('button', { name: 'Monday' })).toHaveAttribute('aria-pressed', 'true');
    expect(days().getByRole('button', { name: 'Saturday' })).toHaveAttribute('aria-pressed', 'false');
    expect(days().getByRole('button', { name: 'Saturday' })).toHaveAttribute('aria-disabled', 'true');
    expect(await within(preview()).findByText('Every trading day at 18:30 IST')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Every day' }));

    expect(screen.getByRole('switch', { name: /Trading days only/ })).not.toBeChecked();
    expect(days().getByRole('button', { name: 'Sunday' })).toHaveAttribute('aria-pressed', 'true');
  });

  it('switching trading days only on and off again keeps the owner’s chips', async () => {
    await openMaster();
    await userEvent.click(days().getByRole('button', { name: 'Saturday' }));
    await userEvent.click(days().getByRole('button', { name: 'Sunday' }));

    await userEvent.click(screen.getByRole('switch', { name: /Trading days only/ }));
    await userEvent.click(screen.getByRole('switch', { name: /Trading days only/ }));

    expect(days().getByRole('button', { name: 'Saturday' })).toHaveAttribute('aria-pressed', 'false');
    expect(days().getByRole('button', { name: 'Friday' })).toHaveAttribute('aria-pressed', 'true');
  });

  it('refuses a schedule with no day: the message is on the days, nothing is asked, Save is off', async () => {
    const { fake } = await openMaster();
    const before = fake.calls.previews.length;

    for (const name of ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday']) {
      await userEvent.click(days().getByRole('button', { name }));
    }

    expect(await screen.findByText('Choose at least one day.')).toBeInTheDocument();
    expect(save()).toBeDisabled();
    expect(fake.calls.previews.length).toBe(before);
  });
});

describe('Configure: errors stand in the way of saving', () => {
  it('a window that ends before it starts: the server’s message is on the field and Save is off until it is fixed', async () => {
    const { fake } = await openMaster();
    await userEvent.selectOptions(screen.getByLabelText('Schedule type'), 'window');
    await within(preview()).findByText('Up from 09:15 to 15:30 IST');

    await enter('To', '09:00');

    expect(await screen.findByText('The window must start before it ends.')).toBeInTheDocument();
    expect(screen.getByLabelText('From')).toBeInvalid();
    expect(save()).toBeDisabled();
    expect(within(preview()).queryByText('No conflicts with other services.')).not.toBeInTheDocument();

    await enter('To', '16:00');

    await waitFor(() => {
      expect(save()).toBeEnabled();
    });
    expect(screen.queryByText('The window must start before it ends.')).not.toBeInTheDocument();
    expect(fake.calls.saves).toHaveLength(0);
  });

  it('a run-after chain that would be circular is named on the field', async () => {
    const services = [
      buildServiceView({
        schedule: { type: 'after', after: serviceIds.master, tradingDaysOnly: false },
        scheduleWords: 'After Instrument master refresh finishes',
        nextRun: null,
      }),
      buildMasterService(),
    ];
    await openMaster(services);
    await userEvent.selectOptions(screen.getByLabelText('Schedule type'), 'after');

    expect(await screen.findByText('The run-after chain would be circular.')).toBeInTheDocument();
    expect(screen.getByLabelText('Run after')).toBeInvalid();
    expect(save()).toBeDisabled();
  });

  it('an interval longer than its window', async () => {
    await openMaster();
    await userEvent.selectOptions(screen.getByLabelText('Schedule type'), 'interval');
    await within(preview()).findByText('Every 5 minutes from 09:15 to 15:30 IST');
    await enter('From', '09:15');
    await enter('To', '09:30');
    await enter('Every (minutes)', '20');

    expect(await screen.findByText('The interval is longer than its window.')).toBeInTheDocument();
    expect(save()).toBeDisabled();
  });

  it('numbers outside their bounds are marked before anything is asked', async () => {
    const { fake } = await openMaster();
    await userEvent.selectOptions(screen.getByLabelText('Schedule type'), 'interval');
    await within(preview()).findByText('Every 5 minutes from 09:15 to 15:30 IST');
    const before = fake.calls.previews.length;

    await enter('Every (minutes)', '200');
    expect(await screen.findByText('Between 1 and 120 minutes.')).toBeInTheDocument();
    await enter('Timeout (min)', '241');
    expect(await screen.findByText('Between 1 and 240 minutes.')).toBeInTheDocument();
    await enter('Retries', '6');
    expect(await screen.findByText('Between 0 and 5.')).toBeInTheDocument();

    expect(save()).toBeDisabled();
    expect(fake.calls.previews.length).toBe(before);
  });

  it('a parameter outside its bounds, in the words of its schema', async () => {
    await openMaster();

    await enter('Minimum turnover', '250');

    expect(await screen.findByText('Between 0 and 100.')).toBeInTheDocument();
    expect(screen.getByLabelText('Minimum turnover')).toBeInvalid();
    expect(save()).toBeDisabled();
    await enter('Minimum turnover', '12.5');
    await waitFor(() => {
      expect(save()).toBeEnabled();
    });
  });

  it('a whole-number parameter refuses a fraction', async () => {
    renderServices();
    await openConfigure('End-of-day data run');

    await enter('Gap look-back', '2.5');

    expect(await screen.findByText('Enter a whole number.')).toBeInTheDocument();
    expect(save()).toBeDisabled();
  });

  it('an empty time', async () => {
    renderServices();
    await openConfigure('End-of-day data run');

    await userEvent.clear(screen.getByLabelText('Time'));

    expect(await screen.findByText('Enter a time.')).toBeInTheDocument();
    expect(save()).toBeDisabled();
  });

  it('a weekly schedule of the end-of-day service uses the same checks', async () => {
    renderServices({
      fake: createServicesFake({
        services: [buildServiceView({ schedule: buildScheduleSpec({ type: 'weekly', weekday: 'fri' }) })],
      }),
    });
    await openConfigure('End-of-day data run');

    expect(screen.getByLabelText('Day')).toHaveValue('fri');
    expect(screen.getByLabelText('Time')).toHaveValue('15:50');
  });
});
