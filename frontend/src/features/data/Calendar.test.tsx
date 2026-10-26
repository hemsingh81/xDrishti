import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { buildCalendarDay } from '@/test/fixtures';
import { createDataFake, type DataFake } from '@/test/dataFake';
import { problemResponse, server } from '@/test/server';
import { calendarColumns } from './components/calendarColumns';
import { renderData as renderAt } from './testing';

/** Every test here looks at the Calendar tab. */
const renderData = (options: Parameters<typeof renderAt>[0] = {}) => renderAt({ search: '?tab=calendar', ...options });

const card = () => screen.findByRole('tabpanel', { name: /^Calendar/ });
const upcoming = async () =>
  within(await card()).findByRole('grid', { name: 'Upcoming holidays and special sessions' });
const proposals = async () => within(await card()).findByRole('grid', { name: 'Proposed calendar days' });
const rowOf = (grid: HTMLElement, name: RegExp) => within(grid).getByRole('row', { name });
const strip = () => screen.findByRole('region', { name: 'Data status' });

async function open(fake: DataFake = createDataFake()) {
  const result = renderData({ fake });
  await card();
  return result;
}

describe('Trading calendar: the card', () => {
  it('shows a loading state, then the upcoming holidays and special sessions', async () => {
    renderData();

    expect(screen.getByText('Loading data overview')).toBeInTheDocument();
    expect(await screen.findByText('Loading the trading calendar')).toBeInTheDocument();
    const grid = await upcoming();
    expect(within(grid).getAllByRole('row')).toHaveLength(1 + 3);
  });

  it('shows date with weekday, kind in words, session window, source and note', async () => {
    await open();

    const grid = await upcoming();
    const diwali = rowOf(grid, /Diwali Laxmi Pujan/);
    expect(within(diwali).getByText('Tue 20 Oct 2026')).toHaveAttribute('datetime', '2026-10-20');
    expect(within(diwali).getByText('Holiday')).toBeInTheDocument();
    expect(within(diwali).getByText('NSE')).toBeInTheDocument();
    const muhurat = rowOf(grid, /Muhurat trading/);
    expect(within(muhurat).getByText('Sun 8 Nov 2026')).toBeInTheDocument();
    expect(within(muhurat).getByText('Special session')).toBeInTheDocument();
    expect(within(muhurat).getByText('18:00–19:00')).toBeInTheDocument();
    expect(within(muhurat).getByText('You')).toBeInTheDocument();
    expect(within(grid).getByRole('columnheader', { name: 'Session (IST)' })).toBeInTheDocument();
  });

  it('lists the days by date', async () => {
    await open();

    const rows = within(await upcoming())
      .getAllByRole('row')
      .slice(1);
    expect(rows.map((row) => /\d+ \w{3} 2026/.exec(row.textContent)?.[0])).toEqual([
      '20 Oct 2026',
      '8 Nov 2026',
      '25 Dec 2026',
    ]);
  });

  it('says so when no day is on the calendar', async () => {
    await open(createDataFake({ calendar: { days: [], proposals: [] } }));

    expect(await screen.findByText(/No upcoming holidays or special sessions yet/)).toBeInTheDocument();
    expect(screen.queryByText(/^Proposed/)).not.toBeInTheDocument();
  });

  it('says in words when the calendar cannot be loaded, and loads it on Retry', async () => {
    const fake = createDataFake();
    fake.failures.calendar = true;
    renderData({ fake });

    const region = await card();
    expect(await within(region).findByText('The data service is not available.')).toBeInTheDocument();
    expect(within(region).getByRole('button', { name: 'Add holiday / special session' })).toBeEnabled();
    expect(screen.getByRole('region', { name: 'Data status' })).toBeInTheDocument();

    fake.failures.calendar = false;
    await userEvent.click(within(region).getByRole('button', { name: 'Retry' }));
    expect(await upcoming()).toBeInTheDocument();
  });

  it('hides the window, source and note on narrow screens and keeps date, kind and the actions', () => {
    const columns = calendarColumns(() => null);

    const hidden = Object.fromEntries(columns.map((column) => [column.id, column.hideBelow]));
    expect(hidden).toEqual({
      date: undefined,
      kind: undefined,
      session: 'sm',
      source: 'md',
      note: 'md',
      actions: undefined,
    });
  });
});

describe('Trading calendar: proposals', () => {
  it('lists the proposals apart, with the reason and Confirm / Dismiss on every row', async () => {
    await open();

    const region = await card();
    expect(within(region).getByRole('heading', { level: 2, name: 'Proposed (2)' })).toBeInTheDocument();
    expect(
      within(region).getByText(/Found in your stored data — confirm to treat it as a non-trading day/),
    ).toBeInTheDocument();
    const grid = await proposals();
    const holiday = rowOf(grid, /Fri 2 Oct 2026/);
    expect(within(holiday).getByText('Stored data')).toBeInTheDocument();
    expect(within(holiday).getByRole('button', { name: 'Confirm Fri 2 Oct 2026' })).toBeEnabled();
    expect(within(holiday).getByRole('button', { name: 'Dismiss Fri 2 Oct 2026' })).toBeEnabled();
    const saturday = rowOf(grid, /Sat 12 Sept 2026/);
    expect(within(saturday).getByText('Special session')).toBeInTheDocument();
    expect(within(saturday).getByText('09:15–15:30')).toBeInTheDocument();
    // They are not holidays yet.
    expect(within(await upcoming()).queryByText('Fri 2 Oct 2026')).not.toBeInTheDocument();
  });

  it('Confirm moves the proposal into the upcoming list and tells the owner', async () => {
    const { fake } = await open();
    const grid = await proposals();

    await userEvent.click(within(grid).getByRole('button', { name: 'Confirm Fri 2 Oct 2026' }));

    expect(
      await screen.findByText('Confirmed Fri 2 Oct 2026: it now counts as a non-trading day.'),
    ).toBeInTheDocument();
    expect(fake.calls.calendarConfirm).toEqual(['2026-10-02']);
    await waitFor(() => {
      expect(
        within(screen.getByRole('grid', { name: 'Upcoming holidays and special sessions' })).getByText(
          'Fri 2 Oct 2026',
        ),
      ).toBeInTheDocument();
    });
    expect(screen.getByRole('heading', { level: 2, name: 'Proposed (1)' })).toBeInTheDocument();
  });

  it('Dismiss removes the proposal for good, without a confirmation step', async () => {
    const { fake } = await open();

    await userEvent.click(within(await proposals()).getByRole('button', { name: 'Dismiss Sat 12 Sept 2026' }));

    expect(await screen.findByText('Dismissed Sat 12 Sept 2026; it will not be proposed again.')).toBeInTheDocument();
    expect(fake.calls.calendarRemove).toEqual(['2026-09-12']);
    await waitFor(() => {
      expect(screen.queryByRole('button', { name: 'Dismiss Sat 12 Sept 2026' })).not.toBeInTheDocument();
    });
    expect(screen.getByRole('heading', { level: 2, name: 'Proposed (1)' })).toBeInTheDocument();
    expect(within(await upcoming()).queryByText('Sat 12 Sept 2026')).not.toBeInTheDocument();
  });

  it('removes the whole Proposed section when the last proposal is dealt with', async () => {
    await open(
      createDataFake({
        calendar: { proposals: [buildCalendarDay({ date: '2026-10-02', status: 'proposed', source: 'observed' })] },
      }),
    );

    await userEvent.click(await screen.findByRole('button', { name: 'Confirm Fri 2 Oct 2026' }));

    await waitFor(() => {
      expect(screen.queryByRole('heading', { name: /^Proposed/ })).not.toBeInTheDocument();
    });
  });

  it('says so when the proposal is gone already (404)', async () => {
    await open();
    const grid = await proposals();
    server.use(
      http.put('/api/data/calendar/:date/confirm', () =>
        problemResponse({ title: 'Not found', code: 'data.calendar_day_not_found' }, 404),
      ),
    );

    await userEvent.click(within(grid).getByRole('button', { name: 'Confirm Sat 12 Sept 2026' }));

    expect(
      await screen.findByText('That day is no longer in the calendar. The list was reloaded.'),
    ).toBeInTheDocument();
  });
});

describe('Trading calendar: remove and dismiss', () => {
  it('asks before removing the owner’s own day, and Cancel changes nothing', async () => {
    const { fake } = await open();
    const grid = await upcoming();

    await userEvent.click(within(grid).getByRole('button', { name: 'Remove Fri 25 Dec 2026' }));
    const dialog = await screen.findByRole('dialog', { name: 'Remove Fri 25 Dec 2026?' });
    expect(
      within(dialog).getByText(/leaves your calendar and the date goes back to being an ordinary day/),
    ).toBeInTheDocument();
    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(fake.calls.calendarRemove).toEqual([]);
    expect(within(grid).getByText('Fri 25 Dec 2026')).toBeInTheDocument();
  });

  it('removes the owner’s own day after the confirmation', async () => {
    const { fake } = await open();

    await userEvent.click(within(await upcoming()).getByRole('button', { name: 'Remove Fri 25 Dec 2026' }));
    const dialog = await screen.findByRole('dialog');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Remove' }));

    expect(await screen.findByText('Removed Fri 25 Dec 2026.')).toBeInTheDocument();
    expect(fake.calls.calendarRemove).toEqual(['2026-12-25']);
    await waitFor(() => {
      expect(
        within(screen.getByRole('grid', { name: 'Upcoming holidays and special sessions' })).queryByText(
          'Fri 25 Dec 2026',
        ),
      ).not.toBeInTheDocument();
    });
  });

  it('calls it Dismiss for a day from NSE’s list and explains that it is not proposed again', async () => {
    const { fake } = await open();

    await userEvent.click(within(await upcoming()).getByRole('button', { name: 'Dismiss Tue 20 Oct 2026' }));
    const dialog = await screen.findByRole('dialog', { name: 'Dismiss Tue 20 Oct 2026?' });
    expect(
      within(dialog).getByText(/is not proposed again, not even by the stored data or NSE's list/),
    ).toBeInTheDocument();
    await userEvent.click(within(dialog).getByRole('button', { name: 'Dismiss' }));

    expect(await screen.findByText('Dismissed Tue 20 Oct 2026.')).toBeInTheDocument();
    expect(fake.calls.calendarRemove).toEqual(['2026-10-20']);
  });

  it('keeps the dialog open with the reason when the day is gone already', async () => {
    await open(createDataFake());
    const grid = await upcoming();
    server.use(
      http.delete('/api/data/calendar/:date', () =>
        problemResponse({ title: 'Not found', code: 'data.calendar_day_not_found' }, 404),
      ),
    );

    await userEvent.click(within(grid).getByRole('button', { name: 'Remove Fri 25 Dec 2026' }));
    await userEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Remove' }));

    expect(
      await screen.findByText('That day is no longer in the calendar. The list was reloaded.'),
    ).toBeInTheDocument();
    expect(screen.getByRole('dialog')).toBeInTheDocument();
  });
});

describe('Trading calendar: add a holiday or special session', () => {
  const openDialog = async () => {
    await userEvent.click(within(await card()).getByRole('button', { name: 'Add holiday / special session' }));
    return screen.findByRole('dialog', { name: 'Add holiday / special session' });
  };

  it('adds a holiday with a note; the session window is only for a special session', async () => {
    const { fake } = await open();
    const dialog = await openDialog();

    expect(within(dialog).queryByLabelText('Session from')).not.toBeInTheDocument();
    await userEvent.type(within(dialog).getByLabelText(/^Date/), '2026-12-31');
    await userEvent.type(within(dialog).getByLabelText('Note'), '  Year end ');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Add' }));

    expect(await screen.findByText('Added Thu 31 Dec 2026.')).toBeInTheDocument();
    expect(fake.calls.calendarAdd).toEqual([{ date: '2026-12-31', kind: 'holiday', note: 'Year end' }]);
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    const row = rowOf(await upcoming(), /Thu 31 Dec 2026/);
    expect(within(row).getByText('You')).toBeInTheDocument();
    expect(within(row).getByText('Year end')).toBeInTheDocument();
  });

  it('adds a special session with the regular window by default and the typed window when changed', async () => {
    const { fake } = await open();
    const dialog = await openDialog();

    await userEvent.type(within(dialog).getByLabelText(/^Date/), '2026-11-14');
    await userEvent.selectOptions(within(dialog).getByRole('combobox', { name: 'Kind' }), 'special');
    expect(within(dialog).getByLabelText('Session from')).toHaveValue('09:15');
    expect(within(dialog).getByLabelText('Session to')).toHaveValue('15:30');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Add' }));

    expect(await screen.findByText('Added Sat 14 Nov 2026.')).toBeInTheDocument();
    expect(fake.calls.calendarAdd[0]).toEqual({
      date: '2026-11-14',
      kind: 'special',
      sessionFrom: '09:15',
      sessionTo: '15:30',
    });
    const row = rowOf(await upcoming(), /Sat 14 Nov 2026/);
    expect(within(row).getByText('09:15–15:30')).toBeInTheDocument();
  });

  it('sends a changed window', async () => {
    const { fake } = await open();
    const dialog = await openDialog();

    await userEvent.type(within(dialog).getByLabelText(/^Date/), '2026-11-09');
    await userEvent.selectOptions(within(dialog).getByRole('combobox', { name: 'Kind' }), 'special');
    const from = within(dialog).getByLabelText('Session from');
    const to = within(dialog).getByLabelText('Session to');
    await userEvent.clear(from);
    await userEvent.type(from, '18:15');
    await userEvent.clear(to);
    await userEvent.type(to, '19:15');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Add' }));

    await screen.findByText('Added Mon 9 Nov 2026.');
    expect(fake.calls.calendarAdd[0]).toMatchObject({ kind: 'special', sessionFrom: '18:15', sessionTo: '19:15' });
    expect(within(rowOf(await upcoming(), /Mon 9 Nov 2026/)).getByText('18:15–19:15')).toBeInTheDocument();
  });

  it('says "Replaced" when the date had an entry already', async () => {
    await open();
    const dialog = await openDialog();

    await userEvent.type(within(dialog).getByLabelText(/^Date/), '2026-10-20');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Add' }));

    expect(await screen.findByText('Replaced the entry for Tue 20 Oct 2026.')).toBeInTheDocument();
    await waitFor(() => {
      expect(
        within(
          rowOf(screen.getByRole('grid', { name: 'Upcoming holidays and special sessions' }), /Tue 20 Oct 2026/),
        ).getByText('You'),
      ).toBeInTheDocument();
    });
  });

  it('asks for a date before sending anything', async () => {
    const { fake } = await open();
    const dialog = await openDialog();

    await userEvent.click(within(dialog).getByRole('button', { name: 'Add' }));

    expect(within(dialog).getByText('Choose a date.')).toBeInTheDocument();
    expect(fake.calls.calendarAdd).toEqual([]);
  });

  it('asks for a window that ends after it starts', async () => {
    const { fake } = await open();
    const dialog = await openDialog();

    await userEvent.type(within(dialog).getByLabelText(/^Date/), '2026-11-14');
    await userEvent.selectOptions(within(dialog).getByRole('combobox', { name: 'Kind' }), 'special');
    const to = within(dialog).getByLabelText('Session to');
    await userEvent.clear(to);
    await userEvent.type(to, '09:00');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Add' }));

    expect(within(dialog).getByText('The session must end after it starts.')).toBeInTheDocument();
    expect(fake.calls.calendarAdd).toEqual([]);
  });

  it('shows the server’s message on the date field, e.g. a holiday on a weekend, and lets it be fixed', async () => {
    const { fake } = await open();
    const dialog = await openDialog();

    await userEvent.type(within(dialog).getByLabelText(/^Date/), '2026-11-14');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Add' }));

    expect(
      await within(dialog).findByText('A weekend is not a trading day, so it cannot be a holiday.'),
    ).toBeInTheDocument();
    expect(within(dialog).getByLabelText(/^Date/)).toBeInvalid();
    expect(screen.getByRole('dialog')).toBeInTheDocument();

    await userEvent.selectOptions(within(dialog).getByRole('combobox', { name: 'Kind' }), 'special');
    expect(within(dialog).queryByText(/cannot be a holiday/)).not.toBeInTheDocument();
    await userEvent.click(within(dialog).getByRole('button', { name: 'Add' }));
    expect(await screen.findByText('Added Sat 14 Nov 2026.')).toBeInTheDocument();
    expect(fake.calls.calendarAdd).toHaveLength(2);
  });

  it('shows the server’s message under the field it names (the end of the window)', async () => {
    await open();
    const dialog = await openDialog();
    // Same-minute window passes the client check only if from < to; let the server refuse a different rule.
    server.use(
      http.post('/api/data/calendar', () =>
        problemResponse(
          {
            title: 'Not valid',
            code: 'data.invalid',
            errors: { sessionTo: ['The session cannot end after midnight.'] },
          },
          400,
        ),
      ),
    );

    await userEvent.type(within(dialog).getByLabelText(/^Date/), '2026-11-14');
    await userEvent.selectOptions(within(dialog).getByRole('combobox', { name: 'Kind' }), 'special');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Add' }));

    expect(await within(dialog).findByText('The session cannot end after midnight.')).toBeInTheDocument();
    expect(within(dialog).getByLabelText('Session to')).toBeInvalid();
  });

  it('says in words when the request fails, and closes with Cancel', async () => {
    await open();
    const dialog = await openDialog();
    server.use(http.post('/api/data/calendar', () => HttpResponse.error()));

    await userEvent.type(within(dialog).getByLabelText(/^Date/), '2026-12-31');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Add' }));

    expect(await within(dialog).findByRole('alert')).toBeInTheDocument();
    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
  });
});

describe('Trading calendar: refresh from NSE', () => {
  it('shows what NSE brought as a toast and adds the new days to the list', async () => {
    const { fake } = await open();

    await userEvent.click(within(await card()).getByRole('button', { name: 'Refresh from NSE' }));

    expect(await screen.findByText('NSE holidays refreshed: 1 added, 0 updated, 2 unchanged.')).toBeInTheDocument();
    expect(fake.calls.calendarRefresh).toBe(1);
    expect(await within(await upcoming()).findByText('Tue 24 Nov 2026')).toBeInTheDocument();
  });

  it('is busy while the request runs', async () => {
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    renderData({
      handlers: [
        http.post('/api/data/calendar/refresh', async () => {
          await gate;
          return HttpResponse.json({
            succeeded: true,
            message: 'Done.',
            added: 0,
            updated: 0,
            unchanged: 0,
            skipped: 0,
            at: '2026-10-12T16:20:00+05:30',
          });
        }),
      ],
    });
    const button = await within(await card()).findByRole('button', { name: 'Refresh from NSE' });

    await userEvent.click(button);
    await waitFor(() => {
      expect(button).toBeDisabled();
    });
    release();

    expect(await screen.findByText('Done.')).toBeInTheDocument();
    await waitFor(() => {
      expect(button).toBeEnabled();
    });
  });

  it('warns, with the hint to add days by hand, when NSE refused', async () => {
    await open(createDataFake({ nseRefresh: 'refused' }));

    await userEvent.click(within(await card()).getByRole('button', { name: 'Refresh from NSE' }));

    expect(
      await screen.findByText('NSE did not answer (HTTP 403); nothing changed. You can add the holidays by hand.'),
    ).toBeInTheDocument();
    expect(within(screen.getByRole('alert')).getByTestId('WarningAmberOutlinedIcon')).toBeInTheDocument();
  });

  it('says so when the request itself fails', async () => {
    renderData({ handlers: [http.post('/api/data/calendar/refresh', () => HttpResponse.error())] });

    await userEvent.click(await within(await card()).findByRole('button', { name: 'Refresh from NSE' }));

    expect(
      await screen.findByText(/Could not reach the server\. .* You can add the holidays by hand\./),
    ).toBeInTheDocument();
  });
});

describe('Trading calendar: the ribbon and the attention line', () => {
  const holidayButton = async (name: string) =>
    within(await screen.findByRole('region', { name: 'Last tick' })).findByRole('button', { name });

  it('shows the next holiday as a tooltip on the last tick', async () => {
    await open();

    expect(await holidayButton('Next holiday: Tue 20 Oct 2026 — Diwali Laxmi Pujan')).toBeInTheDocument();
  });

  it('leaves the holiday out when none is known, and shows a holiday without a note by its date', async () => {
    const none = await open(createDataFake({ calendar: { days: [], proposals: [] } }));
    await strip();
    expect(screen.queryByRole('button', { name: /^Next holiday/ })).not.toBeInTheDocument();
    none.unmount();

    await open(createDataFake({ calendar: { days: [buildCalendarDay({ note: null })], proposals: [] } }));
    expect(await holidayButton('Next holiday: Tue 20 Oct 2026')).toBeInTheDocument();
  });

  it('moves to the following holiday when the next one is dismissed (the overview is read again)', async () => {
    await open();
    expect(await holidayButton('Next holiday: Tue 20 Oct 2026 — Diwali Laxmi Pujan')).toBeInTheDocument();

    await userEvent.click(within(await upcoming()).getByRole('button', { name: 'Dismiss Tue 20 Oct 2026' }));
    await userEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Dismiss' }));

    expect(await holidayButton('Next holiday: Fri 25 Dec 2026 — Christmas')).toBeInTheDocument();
  });

  it('puts the number of proposals on the Calendar tab and in the attention line, which leads to the tab', async () => {
    const { router } = renderAt({ fake: createDataFake() });

    const line = await screen.findByRole('region', { name: 'Needs attention' });
    expect(await screen.findByRole('tab', { name: 'Calendar, 2 to review' })).toHaveAttribute('aria-selected', 'false');
    await userEvent.click(within(line).getByRole('link', { name: '2 calendar proposals' }));

    expect(await screen.findByRole('tab', { name: 'Calendar, 2 to review' })).toHaveAttribute('aria-selected', 'true');
    expect(router.state.location.search).toBe('?tab=calendar');
    expect(await screen.findByRole('heading', { level: 2, name: 'Proposed (2)' })).toBeInTheDocument();
  });

  it('says "1 calendar proposal" in the singular, and shows no line and no badge without proposals', async () => {
    const one = await open(
      createDataFake({
        calendar: { proposals: [buildCalendarDay({ date: '2026-10-02', status: 'proposed', source: 'observed' })] },
      }),
    );
    expect(await screen.findByRole('link', { name: '1 calendar proposal' })).toBeInTheDocument();
    one.unmount();

    await open(createDataFake({ calendar: { proposals: [] } }));
    await strip();
    expect(screen.queryByRole('region', { name: 'Needs attention' })).not.toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Calendar' })).toBeInTheDocument();
  });

  it('counts down as proposals are confirmed or dismissed', async () => {
    await open();
    await screen.findByRole('tab', { name: 'Calendar, 2 to review' });

    await userEvent.click(within(await proposals()).getByRole('button', { name: 'Confirm Fri 2 Oct 2026' }));
    expect(await screen.findByRole('tab', { name: 'Calendar, 1 to review' })).toBeInTheDocument();

    await userEvent.click(within(await proposals()).getByRole('button', { name: 'Dismiss Sat 12 Sept 2026' }));
    await waitFor(() => {
      expect(screen.queryByRole('region', { name: 'Needs attention' })).not.toBeInTheDocument();
    });
    expect(screen.getByRole('tab', { name: 'Calendar' })).toBeInTheDocument();
  });
});

describe('Trading calendar: what a change reloads', () => {
  it('reads the overview again after a change, and marks the coverage as out of date', async () => {
    const { fake, client } = await open();
    await strip();
    client.setQueryData(['data', 'coverage', 'list', {}], {});
    const before = fake.calls.overview;

    await userEvent.click(within(await proposals()).getByRole('button', { name: 'Confirm Fri 2 Oct 2026' }));

    await waitFor(() => {
      expect(fake.calls.overview).toBeGreaterThan(before);
      expect(client.getQueryState(['data', 'coverage', 'list', {}])?.isInvalidated).toBe(true);
    });
  });
});

describe('Trading calendar: a proposal next to a confirmed day', () => {
  const seen = buildCalendarDay({
    date: '2026-10-20',
    source: 'observed',
    status: 'proposed',
    kind: 'special',
    sessionFrom: '09:15',
    sessionTo: '15:30',
    minutes: 375,
    note: null,
    replaces: 'holiday',
  });
  const holiday = buildCalendarDay({ date: '2026-10-20' });
  const withSession = () => createDataFake({ calendar: { days: [holiday], proposals: [seen] } });

  it('says a session was seen on a day that is listed as a holiday, and the holiday stays in Upcoming meanwhile', async () => {
    await open(withSession());

    const grid = await proposals();
    const row = rowOf(grid, /Tue 20 Oct 2026/);
    expect(
      within(row).getByText(
        'A session was seen on Tue 20 Oct 2026, which is listed as a holiday. Confirm to treat it as a special session.',
      ),
    ).toBeInTheDocument();
    expect(within(row).getByText('Special session')).toBeInTheDocument();
    expect(within(row).getByRole('button', { name: 'Confirm Tue 20 Oct 2026' })).toBeEnabled();
    expect(within(row).getByRole('button', { name: 'Dismiss Tue 20 Oct 2026' })).toBeEnabled();
    const listed = rowOf(await upcoming(), /Tue 20 Oct 2026/);
    expect(within(listed).getByText('Holiday')).toBeInTheDocument();
    expect(within(listed).queryByText(/A session was seen/)).not.toBeInTheDocument();
  });

  it('Confirm replaces the holiday with the special session', async () => {
    const { fake } = await open(withSession());

    await userEvent.click(within(await proposals()).getByRole('button', { name: 'Confirm Tue 20 Oct 2026' }));

    expect(
      await screen.findByText(/^Confirmed Tue 20 Oct 2026: it now counts as a special session/),
    ).toBeInTheDocument();
    expect(fake.calls.calendarConfirm).toEqual(['2026-10-20']);
    await waitFor(() => {
      expect(screen.queryByRole('heading', { name: /^Proposed/ })).not.toBeInTheDocument();
    });
    const listed = within(await upcoming()).getAllByRole('row', { name: /Tue 20 Oct 2026/ });
    expect(listed).toHaveLength(1);
    expect(within(listed[0]!).getByText('Special session')).toBeInTheDocument();
  });

  it('Dismiss keeps the holiday and drops the proposal', async () => {
    const { fake } = await open(withSession());

    await userEvent.click(within(await proposals()).getByRole('button', { name: 'Dismiss Tue 20 Oct 2026' }));

    await waitFor(() => {
      expect(screen.queryByRole('heading', { name: /^Proposed/ })).not.toBeInTheDocument();
    });
    expect(fake.calls.calendarRemove).toEqual(['2026-10-20']);
  });

  it('says a day that has no session is listed as a special session, for the opposite case', async () => {
    const none = buildCalendarDay({
      date: '2026-11-08',
      source: 'observed',
      status: 'proposed',
      note: null,
      replaces: 'special',
    });
    await open(createDataFake({ calendar: { proposals: [none] } }));

    expect(
      within(await proposals()).getByText(
        'No session was seen on Sun 8 Nov 2026, which is listed as a special session. Confirm to treat it as a holiday.',
      ),
    ).toBeInTheDocument();
  });
});

describe('Trading calendar: dismissed days and what a change reloads', () => {
  it('tells that dismissed entries are not shown and how to bring one back', async () => {
    await open();

    expect(
      await within(await card()).findByText('Dismissed entries are not shown; to bring one back, add it again.'),
    ).toBeInTheDocument();
  });

  it('shows the hint also when nothing is proposed or upcoming', async () => {
    await open(createDataFake({ calendar: { days: [], proposals: [] } }));

    expect(
      await within(await card()).findByText('Dismissed entries are not shown; to bring one back, add it again.'),
    ).toBeInTheDocument();
  });

  it.each([
    ['confirming a proposal', () => screen.findByRole('button', { name: 'Confirm Fri 2 Oct 2026' })],
    ['dismissing a proposal', () => screen.findByRole('button', { name: 'Dismiss Fri 2 Oct 2026' })],
  ])('%s marks the gaps, the notices and the quality checks as out of date', async (_name, button) => {
    const { client } = await open();
    const keys = [
      ['data', 'gaps', 'open', 1, 10],
      ['data', 'notices'],
      ['quality', 'overview'],
    ] as const;
    for (const key of keys) client.setQueryData(key, {});

    await userEvent.click(await button());

    await waitFor(() => {
      for (const key of keys) expect(client.getQueryState(key)?.isInvalidated).toBe(true);
    });
  });
});
