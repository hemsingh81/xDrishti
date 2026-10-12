import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { delay, http, type RequestHandler } from 'msw';
import { buildCorporateAction, coverageWithCsvEnd, sampleCorporateActions } from '@/test/actionFixtures';
import { scrollToEnd } from '@/test/intersection';
import { createDataFake, type DataFake } from '@/test/dataFake';
import { problemResponse, server } from '@/test/server';
import { smallPage, threePages } from '@/test/smallPages';
import { actionColumns } from './components/actionColumns';
import { renderData as renderAt } from './testing';

// Lists page by a few rows here instead of 100 (the default of 100 is asserted in useInfiniteRows.test.tsx).
vi.mock('@/shared/lib/useInfiniteRows', async (importOriginal) =>
  (await import('@/test/smallPages')).withSmallPages(await importOriginal()),
);

/** Every test here looks at the Corporate actions tab. */
const renderData = (options: Parameters<typeof renderAt>[0] = {}) => renderAt({ search: '?tab=actions', ...options });

const region = () => screen.findByRole('tabpanel', { name: /^Corporate actions/ });
const list = async () => within(await region()).findByRole('grid', { name: 'Corporate actions list' });
const rowsIn = (grid: HTMLElement) => within(grid).getAllByRole('row').slice(1);
const rowOf = (grid: HTMLElement, name: RegExp) => within(grid).getByRole('row', { name });
/** A status chip of the list: its name starts with the label, the count (when it has one) follows. */
const tab = async (name: string) =>
  within(await region()).findByRole('button', { name: new RegExp(`^${name}( \\d+)?$`) });
const attention = () => screen.findByRole('region', { name: 'Needs attention' });

/** The owner's real setup: the CSV-only instruments end on 30 Apr 2026. */
const fakeWith = (options: Parameters<typeof createDataFake>[0] = {}) =>
  createDataFake({ coverage: coverageWithCsvEnd(), corporateActions: sampleCorporateActions, ...options });

async function open({
  search = '?tab=actions',
  fake = fakeWith(),
  handlers,
}: { search?: string; fake?: DataFake; handlers?: RequestHandler[] } = {}) {
  const result = renderData({ fake, search, handlers });
  await list();
  await waitFor(() => {
    expect(
      rowsIn(within(screen.getByRole('tabpanel', { name: /^Corporate actions/ })).getByRole('grid')),
    ).not.toHaveLength(0);
  });
  return result;
}

describe('Corporate actions: the tab', () => {
  it('has the add, import and detect buttons, a one-line note and the whole explanation behind an info button', async () => {
    renderData({ fake: fakeWith() });

    const card = await region();
    expect(card).toHaveAttribute('id', 'corporate-actions');
    expect(within(card).getByRole('button', { name: 'Add action' })).toBeEnabled();
    expect(within(card).getByRole('button', { name: 'Import CSV' })).toBeEnabled();
    expect(within(card).getByRole('button', { name: 'Detect now' })).toBeEnabled();
    const line = within(card).getByText(/^History from your CSV files is already adjusted at the source;/);
    expect(line).toHaveAttribute('role', 'note');
    expect(within(card).queryByText(/Split 1:5 is one share into five/)).not.toBeInTheDocument();

    await userEvent.click(within(card).getByRole('button', { name: 'About adjusted history and ratios' }));

    const help = await screen.findByRole('dialog', { name: 'About adjusted history and ratios' });
    expect(within(help).getByText(/Split 1:5 is one share into five/)).toBeInTheDocument();
    expect(within(help).getByText(/bonus 1:1 is one extra share for each held/)).toBeInTheDocument();
    expect(
      within(help).getByText(
        'History loaded from your CSV files is already adjusted at the source. The check is automatic: an action whose ex-date is on or before the day an instrument’s CSV files end asks you to confirm before it adjusts those prices a second time.',
      ),
    ).toBeInTheDocument();
  });

  it('is the tab of the old anchor link #corporate-actions', async () => {
    const { router } = renderAt({ fake: fakeWith() });
    await userEvent.click(await screen.findByRole('tab', { name: /^Corporate actions/ }));
    expect(await region()).toHaveAttribute('id', 'corporate-actions');
    expect(router.state.location.search).toBe('?tab=actions');
  });

  it('does not read the coverage to find the end of the CSV data: the API says it per instrument', async () => {
    const fake = fakeWith();
    renderData({ fake });

    await within(await region()).findByText('NSE:TCS');
    expect(fake.calls.lookups).toEqual([]);
  });

  it('shows a loading state while the actions are read', async () => {
    renderData({
      fake: fakeWith(),
      handlers: [http.get('/api/data/corporate-actions', () => delay('infinite'))],
    });

    expect(await list()).toHaveAttribute('aria-busy', 'true');
  });

  it('shows the actions with instrument, ex-date, kind, ratio, factor, source, status and note', async () => {
    renderData({ fake: fakeWith() });

    const grid = await list();
    const infy = rowOf(grid, /NSE:INFY.*Suggested/);
    expect(within(infy).getByText('INFOSYS LTD')).toBeInTheDocument();
    expect(within(infy).getByText('1 Sept 2026')).toHaveAttribute('datetime', '2026-09-01');
    expect(within(infy).getByText('Split')).toBeInTheDocument();
    expect(within(infy).getByText('1:5')).toBeInTheDocument();
    expect(within(infy).getByText('×0.2')).toBeInTheDocument();
    expect(within(infy).getByText('Detected')).toBeInTheDocument();
    expect(within(infy).getByText('Suggested')).toBeInTheDocument();
    expect(within(infy).getByText('Open fell 80% on 4.2 times the usual volume.')).toBeInTheDocument();
    const hdfc = rowOf(grid, /NSE:HDFCBANK/);
    expect(within(hdfc).getByText('Bonus')).toBeInTheDocument();
    expect(within(hdfc).getByText('×0.5')).toBeInTheDocument();
    expect(within(hdfc).getByText('CSV import')).toBeInTheDocument();
    expect(within(hdfc).getByText('Applied')).toBeInTheDocument();
  });

  it('lists the newest ex-date first', async () => {
    await open();

    const dates = rowsIn(await list()).map((row) =>
      within(row)
        .getByText(/^\d+ \w+ \d{4}$/)
        .getAttribute('datetime'),
    );
    expect(dates).toEqual(['2026-10-11', '2026-09-01', '2026-08-10', '2026-03-02', '2025-08-26']);
  });

  it('says in words when the actions cannot be loaded, keeps the rest of the page, and loads them on Retry', async () => {
    const fake = fakeWith();
    fake.actions.failures.list = true;
    renderData({ fake });

    const card = await region();
    expect(await within(card).findByText('The data service is not available.')).toBeInTheDocument();
    expect(within(card).getByRole('button', { name: 'Add action' })).toBeEnabled();
    expect(screen.getByRole('region', { name: 'Data status' })).toBeInTheDocument();

    fake.actions.failures.list = false;
    await userEvent.click(within(card).getByRole('button', { name: 'Retry' }));
    expect(await within(card).findByText('NSE:TCS')).toBeInTheDocument();
  });

  it('hides the factor, source and note on narrow screens and keeps instrument, ex-date, kind, ratio, status and the buttons', () => {
    const hidden = Object.fromEntries(actionColumns(() => null).map((column) => [column.id, column.hideBelow]));

    expect(hidden).toEqual({
      instrument: undefined,
      exDate: undefined,
      kind: undefined,
      ratio: undefined,
      factor: 'sm',
      source: 'md',
      status: undefined,
      note: 'xl',
      actions: undefined,
    });
  });
});

describe('Corporate actions: the status chips', () => {
  it('counts the suggestions on the Suggested chip and the list on the chip that is on, and shows all actions to start with', async () => {
    await open();

    expect(await tab('Suggested')).toHaveAccessibleName('Suggested 2');
    expect(await tab('All')).toHaveAccessibleName('All 5');
    expect(await tab('All')).toHaveAttribute('aria-pressed', 'true');
    expect(rowsIn(await list())).toHaveLength(5);
    expect(await tab('Applied')).toHaveAccessibleName('Applied');
  });

  it('lists them in the order the owner works through them: Suggested, Applied, Ignored, All', async () => {
    await open();

    const chips = within(await region()).getByRole('group', { name: 'Corporate action status' });
    expect(
      within(chips)
        .getAllByRole('button')
        .map((chip) => chip.textContent),
    ).toEqual(['Suggested 2', 'Applied', 'Ignored', 'All 5']);
  });

  it.each([
    ['Suggested', 'suggested', 2, '?tab=actions&actions=suggested'],
    ['Applied', 'applied', 2, '?tab=actions&actions=applied'],
    ['Ignored', 'ignored', 1, '?tab=actions&actions=ignored'],
  ])('%s asks for that status, shows its rows and puts it in the URL', async (name, status, count, search) => {
    const fake = fakeWith();
    const { router } = await open({ fake });

    await userEvent.click(await tab(name));

    await waitFor(() => {
      expect(rowsIn(screen.getByRole('grid', { name: 'Corporate actions list' }))).toHaveLength(count);
    });
    expect(fake.actions.calls.list.at(-1)).toContain(`status=${status}`);
    expect(router.state.location.search).toBe(search);
    expect(await tab(name)).toHaveAccessibleName(`${name} ${String(count)}`);
    await userEvent.click(await tab('All'));
    await waitFor(() => {
      expect(rowsIn(screen.getByRole('grid', { name: 'Corporate actions list' }))).toHaveLength(5);
    });
    expect(router.state.location.search).toBe('?tab=actions');
  });

  it('opens on the filter named in the URL', async () => {
    await open({ search: '?actions=suggested' });

    expect(await tab('Suggested')).toHaveAttribute('aria-pressed', 'true');
    await waitFor(() => {
      expect(rowsIn(screen.getByRole('grid', { name: 'Corporate actions list' }))).toHaveLength(2);
    });
  });

  it('says in words that nothing waits, is applied or is ignored, and when there is nothing at all', async () => {
    const fake = fakeWith({ corporateActions: [] });
    renderData({ fake, search: '?actions=suggested' });
    expect(await within(await region()).findByText('No suggestions waiting for a decision.')).toBeInTheDocument();

    await userEvent.click(await tab('Applied'));
    expect(await screen.findByText(/No action is applied/)).toBeInTheDocument();
    await userEvent.click(await tab('Ignored'));
    expect(await screen.findByText('Nothing is ignored.')).toBeInTheDocument();
    await userEvent.click(await tab('All'));
    expect(await screen.findByText(/No corporate actions yet/)).toBeInTheDocument();
    expect(screen.getByText(/Detect now/, { selector: 'p' })).toBeInTheDocument();
  });
});

describe('Corporate actions: infinite scroll', () => {
  const shown = (count: number) => `Showing ${String(count)} of ${String(threePages)}`;
  const many = (count: number) =>
    Array.from({ length: count }, (_, n) =>
      buildCorporateAction({
        id: `00000000-0000-4000-8000-00000000${String(5000 + n)}`,
        exDate: `2026-${String((n % 9) + 1).padStart(2, '0')}-${String((n % 27) + 1).padStart(2, '0')}`,
        status: n % 2 === 0 ? 'applied' : 'ignored',
      }),
    );

  it('shows "Showing N of total", loads the next page at the end of the list, and keeps page and size out of the URL', async () => {
    const fake = fakeWith({ corporateActions: many(threePages) });
    const { router } = renderData({ fake });
    await screen.findByText(shown(smallPage));
    expect(screen.queryByRole('combobox', { name: 'Rows per page' })).not.toBeInTheDocument();

    scrollToEnd();
    await screen.findByText(shown(smallPage * 2));
    scrollToEnd();
    await screen.findByText(shown(threePages));

    expect(fake.actions.calls.list.map((call) => new URLSearchParams(call).get('page'))).toEqual(['1', '2', '3']);
    expect(fake.actions.calls.list.every((call) => call.includes(`pageSize=${String(smallPage)}`))).toBe(true);
    expect(router.state.location.search).toBe('?tab=actions');
  });

  it('starts the new list from its first page when the status changes', async () => {
    const fake = fakeWith({ corporateActions: many(threePages) });
    renderData({ fake });
    await screen.findByText(shown(smallPage));
    scrollToEnd();
    await screen.findByText(shown(smallPage * 2));

    await userEvent.click(await tab('Applied'));

    await screen.findByText(`Showing ${String(smallPage)} of ${String(Math.ceil(threePages / 2))}`);
    expect(fake.actions.calls.list.at(-1)).toContain('page=1');
    expect(fake.actions.calls.list.at(-1)).toContain('status=applied');
  });

  it('keeps the loaded rows and offers a retry when a further page cannot be loaded', async () => {
    const fake = fakeWith({ corporateActions: many(threePages) });
    renderData({ fake });
    await screen.findByText(shown(smallPage));

    fake.actions.failures.list = true;
    scrollToEnd();
    expect(await screen.findByText('Could not load more rows')).toBeInTheDocument();
    fake.actions.failures.list = false;
    await userEvent.click(screen.getByRole('button', { name: 'Retry' }));

    await screen.findByText(shown(smallPage * 2));
  });
});

describe('Corporate actions: the buttons of a row', () => {
  it('offer Confirm, Edit and Ignore on a suggestion, Edit and Ignore (take back) on an applied action, Apply and Edit on an ignored one', async () => {
    await open();
    const grid = await list();

    const suggested = rowOf(grid, /NSE:TCS.*Suggested/);
    expect(within(suggested).getByRole('button', { name: /^Confirm NSE:TCS bonus 1:1 on 10 Aug 2026$/ })).toBeEnabled();
    expect(within(suggested).getByRole('button', { name: /^Edit NSE:TCS/ })).toBeEnabled();
    expect(within(suggested).getByRole('button', { name: /^Ignore NSE:TCS/ })).toBeEnabled();
    const applied = rowOf(grid, /NSE:HDFCBANK/);
    expect(within(applied).queryByRole('button', { name: /^Confirm/ })).not.toBeInTheDocument();
    expect(within(applied).getByRole('button', { name: /^Edit NSE:HDFCBANK/ })).toBeEnabled();
    expect(within(applied).getByRole('button', { name: /^Ignore \(take back\) NSE:HDFCBANK/ })).toBeEnabled();
    const ignored = rowOf(grid, /NSE:INFY.*Ignored/);
    expect(within(ignored).getByRole('button', { name: /^Apply NSE:INFY bonus 1:2/ })).toBeEnabled();
    expect(within(ignored).getByRole('button', { name: /^Edit NSE:INFY/ })).toBeEnabled();
    expect(within(ignored).queryByRole('button', { name: /^Ignore/ })).not.toBeInTheDocument();
  });

  it('Confirm applies a suggestion as it is, tells the owner, and moves it from the suggestions', async () => {
    const fake = fakeWith();
    const { client } = await open({ fake, search: '?actions=suggested' });
    // The coverage is not on this tab: it is marked out of date, and read again when its tab is opened.
    client.setQueryData(['data', 'coverage', 'list', {}], {});
    const before = { overview: fake.calls.overview };
    expect(within(await attention()).getByRole('link', { name: '2 suggested corporate actions' })).toBeInTheDocument();

    await userEvent.click(await screen.findByRole('button', { name: /^Confirm NSE:INFY split 1:5 on 1 Sept 2026$/ }));

    expect(
      await screen.findByText('Confirmed NSE:INFY split 1:5 on 1 Sept 2026; adjusted prices use it now.'),
    ).toBeInTheDocument();
    expect(fake.actions.calls.update).toEqual([
      {
        id: sampleCorporateActions[0]?.id,
        body: { exDate: null, kind: null, numerator: null, denominator: null, note: null },
      },
    ]);
    expect(fake.actions.items.find((a) => a.id === sampleCorporateActions[0]?.id)?.status).toBe('applied');
    await waitFor(() => {
      expect(rowsIn(screen.getByRole('grid', { name: 'Corporate actions list' }))).toHaveLength(1);
    });
    expect(await tab('Suggested')).toHaveAccessibleName('Suggested 1');
    expect(within(await attention()).getByRole('link', { name: '1 suggested corporate action' })).toBeInTheDocument();
    expect(fake.calls.overview).toBeGreaterThan(before.overview);
    expect(client.getQueryState(['data', 'coverage', 'list', {}])?.isInvalidated).toBe(true);
  });

  it('Confirm of an ex-date inside adjusted CSV history is sent plain first, then asks and repeats with consent', async () => {
    const early = buildCorporateAction({ exDate: '2026-04-20' });
    const fake = fakeWith({ corporateActions: [early] });
    await open({ fake });

    await userEvent.click(await screen.findByRole('button', { name: /^Confirm NSE:INFY split 1:5 on 20 Apr 2026$/ }));

    const dialog = await screen.findByRole('dialog', { name: 'Adjust history that is already adjusted?' });
    expect(dialog).toHaveTextContent(
      'Bars of NSE:INFY up to 30 Apr 2026 came from CSV files that are already adjusted for corporate actions.',
    );
    expect(dialog).toHaveTextContent('adjusted twice');
    // The first request carried no consent and nothing was applied.
    expect(fake.actions.calls.update).toEqual([
      { id: early.id, body: { exDate: null, kind: null, numerator: null, denominator: null, note: null } },
    ]);
    expect(fake.actions.items[0]?.status).toBe('suggested');

    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(fake.actions.calls.update).toHaveLength(1);
    expect(fake.actions.items[0]?.status).toBe('suggested');

    await userEvent.click(await screen.findByRole('button', { name: /^Confirm NSE:INFY/ }));
    await userEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Adjust anyway' }));
    expect(await screen.findByText(/^Confirmed NSE:INFY split 1:5 on 20 Apr 2026/)).toBeInTheDocument();
    expect(fake.actions.calls.update).toHaveLength(3);
    expect(fake.actions.calls.update[2]?.body).toEqual({
      exDate: null,
      kind: null,
      numerator: null,
      denominator: null,
      note: null,
      acknowledgeCsvAdjusted: true,
    });
    expect(fake.actions.items[0]?.status).toBe('applied');
  });

  it('Confirm of an ex-date after the CSV data needs no question', async () => {
    const fake = fakeWith();
    await open({ fake });

    await userEvent.click(await screen.findByRole('button', { name: /^Confirm NSE:INFY split 1:5 on 1 Sept 2026$/ }));

    expect(await screen.findByText(/^Confirmed NSE:INFY split 1:5 on 1 Sept 2026/)).toBeInTheDocument();
    expect(fake.actions.calls.guarded).toEqual([]);
  });

  it('Confirm of an instrument without CSV history needs no question, whatever the ex-date', async () => {
    const early = buildCorporateAction({
      exDate: '2020-01-02',
      instrumentId: coverageWithCsvEnd().find((r) => r.symbol === 'NSE:TCS')?.id ?? '',
      symbol: 'NSE:TCS',
    });
    const fake = fakeWith({ corporateActions: [early] });
    await open({ fake });

    await userEvent.click(await screen.findByRole('button', { name: /^Confirm NSE:TCS/ }));

    expect(await screen.findByText(/^Confirmed NSE:TCS split 1:5 on 2 Jan 2020/)).toBeInTheDocument();
    expect(fake.actions.calls.guarded).toEqual([]);
  });

  it('keeps asking in words when the repeated Confirm fails for another reason', async () => {
    const early = buildCorporateAction({ exDate: '2026-04-20' });
    const fake = fakeWith({ corporateActions: [early] });
    await open({ fake });
    await userEvent.click(await screen.findByRole('button', { name: /^Confirm NSE:INFY/ }));
    const dialog = await screen.findByRole('dialog', { name: 'Adjust history that is already adjusted?' });
    server.use(
      http.put('/api/data/corporate-actions/:id', () =>
        problemResponse({ title: 'That action does not exist.', code: 'data.corporate_action_not_found' }, 404),
      ),
    );

    await userEvent.click(within(dialog).getByRole('button', { name: 'Adjust anyway' }));

    expect(
      await within(dialog).findByText('That action is no longer in the list. The list was reloaded.'),
    ).toBeInTheDocument();
  });

  it('says in words that an action of the other kind is on that day (409) when confirming', async () => {
    const clash = buildCorporateAction({
      id: '00000000-0000-4000-8000-000000000581',
      exDate: '2026-09-01',
      kind: 'bonus',
      numerator: 1,
      denominator: 1,
      status: 'applied',
    });
    const fake = fakeWith({ corporateActions: [buildCorporateAction(), clash] });
    await open({ fake });

    await userEvent.click(await screen.findByRole('button', { name: /^Confirm NSE:INFY split 1:5/ }));

    expect(
      await screen.findByText(
        'Another action of the other kind exists on that date for this instrument — ignore it first.',
      ),
    ).toBeInTheDocument();
    expect(fake.actions.items.find((a) => a.kind === 'split')?.status).toBe('suggested');
  });

  it('Ignore declines a suggestion at once and keeps it in the list as ignored', async () => {
    const fake = fakeWith();
    await open({ fake });

    await userEvent.click(await screen.findByRole('button', { name: /^Ignore NSE:TCS bonus 1:1/ }));

    expect(await screen.findByText('Ignored NSE:TCS bonus 1:1 on 10 Aug 2026.')).toBeInTheDocument();
    expect(fake.actions.calls.ignore).toEqual([sampleCorporateActions[1]?.id]);
    await waitFor(() => {
      expect(
        within(rowOf(screen.getByRole('grid', { name: 'Corporate actions list' }), /NSE:TCS/)).getByText('Ignored'),
      ).toBeInTheDocument();
    });
    expect(await tab('Suggested')).toHaveAccessibleName('Suggested 1');
  });

  it('Ignore (take back) of an applied action asks first, and says what changes', async () => {
    const fake = fakeWith();
    await open({ fake });

    await userEvent.click(await screen.findByRole('button', { name: /^Ignore \(take back\) NSE:HDFCBANK/ }));

    const dialog = await screen.findByRole('dialog', { name: 'Take back NSE:HDFCBANK bonus 1:1?' });
    expect(within(dialog).getByText(/Prices before 26 Aug 2025 are no longer adjusted for it/)).toBeInTheDocument();
    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(fake.actions.calls.ignore).toHaveLength(0);

    await userEvent.click(await screen.findByRole('button', { name: /^Ignore \(take back\) NSE:HDFCBANK/ }));
    await userEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Ignore' }));

    expect(
      await screen.findByText('Took back NSE:HDFCBANK bonus 1:1 on 26 Aug 2025; prices are no longer adjusted for it.'),
    ).toBeInTheDocument();
    expect(fake.actions.calls.ignore).toEqual([sampleCorporateActions[3]?.id]);
    await waitFor(() => {
      expect(
        within(rowOf(screen.getByRole('grid', { name: 'Corporate actions list' }), /NSE:HDFCBANK/)).getByText(
          'Ignored',
        ),
      ).toBeInTheDocument();
    });
  });

  it('Apply brings an ignored action back, asking first when the ex-date is inside adjusted CSV history', async () => {
    const fake = fakeWith();
    await open({ fake });

    await userEvent.click(await screen.findByRole('button', { name: /^Apply NSE:INFY bonus 1:2 on 2 Mar 2026$/ }));

    // 2 Mar 2026 is inside the CSV data, so the API asks first.
    const dialog = await screen.findByRole('dialog', { name: 'Adjust history that is already adjusted?' });
    expect(dialog).toHaveTextContent('Bars of NSE:INFY up to 30 Apr 2026');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Adjust anyway' }));
    expect(await screen.findByText(/^Applied NSE:INFY bonus 1:2 on 2 Mar 2026/)).toBeInTheDocument();
    expect(fake.actions.items.find((a) => a.id === sampleCorporateActions[4]?.id)?.status).toBe('applied');
  });

  it('says in words when the action is gone (404) and reloads the list', async () => {
    const fake = fakeWith();
    await open({ fake });
    const reads = fake.actions.calls.list.length;
    server.use(
      http.post('/api/data/corporate-actions/:id/ignore', () =>
        problemResponse({ title: 'That action does not exist.', code: 'data.corporate_action_not_found' }, 404),
      ),
    );

    await userEvent.click(await screen.findByRole('button', { name: /^Ignore NSE:TCS bonus 1:1/ }));

    expect(await screen.findByText('That action is no longer in the list. The list was reloaded.')).toBeInTheDocument();
    await waitFor(() => {
      expect(fake.actions.calls.list.length).toBeGreaterThan(reads);
    });
  });
});

describe('Corporate actions: Edit', () => {
  const edit = async (name: RegExp) => {
    await userEvent.click(await screen.findByRole('button', { name }));
    return screen.findByRole('dialog', { name: 'Edit corporate action' });
  };

  it('opens with what the action has now, the detector note included, and the instrument as text', async () => {
    await open();

    const dialog = await edit(/^Edit NSE:INFY split 1:5/);

    expect(within(dialog).getByText('NSE:INFY')).toBeInTheDocument();
    expect(within(dialog).getByLabelText(/^Ex-date/)).toHaveValue('2026-09-01');
    expect(within(dialog).getByLabelText('Kind')).toHaveValue('split');
    expect(within(dialog).getByLabelText(/^Numerator/)).toHaveValue('1');
    expect(within(dialog).getByLabelText(/^Denominator/)).toHaveValue('5');
    expect(within(dialog).getByLabelText('Note')).toHaveValue('Open fell 80% on 4.2 times the usual volume.');
    expect(within(dialog).getByText('Prices before 1 Sept 2026 × 0.2')).toBeInTheDocument();
    expect(within(dialog).getByText(/Saving applies this action/)).toBeInTheDocument();
    expect(within(dialog).queryByRole('combobox', { name: 'Instrument' })).not.toBeInTheDocument();
  });

  it('saves the changed ratio, applies the action and updates the row', async () => {
    const fake = fakeWith();
    await open({ fake });
    const dialog = await edit(/^Edit NSE:INFY split 1:5/);

    const denominator = within(dialog).getByLabelText(/^Denominator/);
    await userEvent.clear(denominator);
    await userEvent.type(denominator, '10');
    expect(within(dialog).getByText('Prices before 1 Sept 2026 × 0.1')).toBeInTheDocument();
    await userEvent.click(within(dialog).getByRole('button', { name: 'Save and apply' }));

    expect(await screen.findByText(/^Saved NSE:INFY split 1:10 on 1 Sept 2026; it is applied/)).toBeInTheDocument();
    expect(fake.actions.calls.update).toEqual([
      {
        id: sampleCorporateActions[0]?.id,
        body: {
          exDate: '2026-09-01',
          kind: 'split',
          numerator: 1,
          denominator: 10,
          note: 'Open fell 80% on 4.2 times the usual volume.',
        },
      },
    ]);
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    const row = rowOf(await list(), /NSE:INFY.*1:10/);
    expect(within(row).getByText('Applied')).toBeInTheDocument();
    expect(within(row).getByText('×0.1')).toBeInTheDocument();
  });

  it('checks the ratio before sending: whole numbers, and no 1:1 split', async () => {
    const fake = fakeWith();
    await open({ fake });
    const dialog = await edit(/^Edit NSE:INFY split 1:5/);

    const denominator = within(dialog).getByLabelText(/^Denominator/);
    await userEvent.clear(denominator);
    await userEvent.type(denominator, '1');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Save and apply' }));
    expect(
      await within(dialog).findByText('A 1:1 split changes nothing. Enter a different ratio.'),
    ).toBeInTheDocument();

    await userEvent.clear(denominator);
    await userEvent.type(denominator, '2.5');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Save and apply' }));
    expect(await within(dialog).findByText('Enter a whole number from 1 to 100000.')).toBeInTheDocument();
    expect(fake.actions.calls.update).toHaveLength(0);
  });

  it('shows the messages of the server on their fields', async () => {
    const reject = [
      http.put('/api/data/corporate-actions/:id', () =>
        problemResponse(
          {
            title: 'The corporate action is not valid.',
            code: 'data.invalid',
            errors: { exDate: ['The ex-date is more than a year ahead.'], other: ['Something else.'] },
          },
          400,
        ),
      ),
    ];
    await open({ handlers: reject });
    const dialog = await edit(/^Edit NSE:INFY split 1:5/);

    await userEvent.click(within(dialog).getByRole('button', { name: 'Save and apply' }));

    expect(await within(dialog).findByText('The ex-date is more than a year ahead.')).toBeInTheDocument();
    expect(within(dialog).getByLabelText(/^Ex-date/)).toBeInvalid();
    expect(within(dialog).getByText('other: Something else.')).toBeInTheDocument();
  });

  it('says in words that the instrument has that action already (409) and stays open', async () => {
    await open();
    const dialog = await edit(/^Edit NSE:INFY split 1:5/);

    await userEvent.clear(within(dialog).getByLabelText(/^Ex-date/));
    await userEvent.type(within(dialog).getByLabelText(/^Ex-date/), '2026-03-02');
    await userEvent.selectOptions(within(dialog).getByLabelText('Kind'), 'bonus');
    await userEvent.clear(within(dialog).getByLabelText(/^Denominator/));
    await userEvent.type(within(dialog).getByLabelText(/^Denominator/), '2');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Save and apply' }));

    expect(
      await within(dialog).findByText(
        'This instrument already has an action of this kind on that ex-date. Edit that one instead.',
      ),
    ).toBeInTheDocument();
    expect(screen.getByRole('dialog', { name: 'Edit corporate action' })).toBeInTheDocument();
  });

  it('warns when the ex-date is on or before the day the CSV files of the instrument end, then asks when saving', async () => {
    const fake = fakeWith();
    await open({ fake });
    const dialog = await edit(/^Edit NSE:INFY split 1:5/);
    const date = within(dialog).getByLabelText(/^Ex-date/);

    expect(within(dialog).queryByRole('note')).not.toBeInTheDocument();
    await userEvent.clear(date);
    await userEvent.type(date, '2026-04-30');
    expect(within(dialog).getByRole('note')).toHaveTextContent(
      'This ex-date is on or before 30 Apr 2026, the last day of this instrument’s CSV files.',
    );
    await userEvent.clear(date);
    await userEvent.type(date, '2026-05-01');
    expect(within(dialog).queryByRole('note')).not.toBeInTheDocument();
    await userEvent.clear(date);
    await userEvent.type(date, '2026-04-29');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Save and apply' }));

    // Refused first (nothing saved); the edit form stays behind the question.
    const question = await screen.findByRole('dialog', { name: 'Adjust history that is already adjusted?' });
    expect(question).toHaveTextContent('Bars of NSE:INFY up to 30 Apr 2026');
    expect(fake.actions.calls.update).toHaveLength(1);
    expect(fake.actions.calls.update[0]?.body).not.toHaveProperty('acknowledgeCsvAdjusted');
    await userEvent.click(within(question).getByRole('button', { name: 'Cancel' }));
    await waitFor(() => {
      expect(
        screen.queryByRole('dialog', { name: 'Adjust history that is already adjusted?' }),
      ).not.toBeInTheDocument();
    });
    expect(screen.getByRole('dialog', { name: 'Edit corporate action' })).toBeInTheDocument();
    expect(
      within(screen.getByRole('dialog', { name: 'Edit corporate action' })).getByLabelText(/^Ex-date/),
    ).toHaveValue('2026-04-29');

    await userEvent.click(screen.getByRole('button', { name: 'Save and apply' }));
    await userEvent.click(
      within(await screen.findByRole('dialog', { name: 'Adjust history that is already adjusted?' })).getByRole(
        'button',
        {
          name: 'Adjust anyway',
        },
      ),
    );

    expect(await screen.findByText(/^Saved NSE:INFY split 1:5 on 29 Apr 2026/)).toBeInTheDocument();
    expect(fake.actions.calls.update).toHaveLength(3);
    expect(fake.actions.calls.update[2]?.body).toMatchObject({ exDate: '2026-04-29', acknowledgeCsvAdjusted: true });
  });

  it('says in words that an action of the other kind is on that day (409) and stays open', async () => {
    const clash = buildCorporateAction({
      id: '00000000-0000-4000-8000-000000000582',
      exDate: '2026-09-01',
      kind: 'bonus',
      numerator: 1,
      denominator: 1,
      status: 'applied',
    });
    await open({ fake: fakeWith({ corporateActions: [buildCorporateAction(), clash] }) });
    const dialog = await edit(/^Edit NSE:INFY split 1:5/);

    await userEvent.click(within(dialog).getByRole('button', { name: 'Save and apply' }));

    expect(
      await within(dialog).findByText(
        'Another action of the other kind exists on that date for this instrument — ignore it first.',
      ),
    ).toBeInTheDocument();
    expect(screen.getByRole('dialog', { name: 'Edit corporate action' })).toBeInTheDocument();
  });
});
