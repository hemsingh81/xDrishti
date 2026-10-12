import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { buildCorporateAction, coverageWithCsvEnd, sampleCorporateActions } from '@/test/actionFixtures';
import { createDataFake } from '@/test/dataFake';
import { buildDataOverview, sampleCoverage, sampleGaps } from '@/test/fixtures';
import { ready, renderData } from './testing';

const tablist = () => screen.findByRole('tablist', { name: 'Data views' });
const tabNames = async () =>
  within(await tablist())
    .getAllByRole('tab')
    .map((tab) => tab.getAttribute('aria-label') ?? tab.textContent);
const selected = async () =>
  within(await tablist())
    .getAllByRole('tab')
    .filter((tab) => tab.getAttribute('aria-selected') === 'true')
    .map((tab) => tab.textContent);

const fakeWith = (options: Parameters<typeof createDataFake>[0] = {}) =>
  createDataFake({
    coverage: coverageWithCsvEnd(),
    corporateActions: sampleCorporateActions,
    gaps: sampleGaps,
    ...options,
  });

describe('Data tabs: the tabs and their badges', () => {
  it('has Coverage, Calendar, Corporate actions, Gaps and Runs, with Coverage open', async () => {
    const { router } = renderData({ fake: fakeWith() });
    await ready();

    expect(await tabNames()).toEqual([
      'Coverage',
      'Calendar, 2 to review',
      'Corporate actions, 2 suggested',
      'Gaps, 3 open',
      'Runs',
    ]);
    expect(await selected()).toEqual(['Coverage']);
    expect(router.state.location.search).toBe('');
    expect(await screen.findByRole('grid', { name: 'Coverage' })).toBeInTheDocument();
  });

  it('shows the counts as numbers on the tabs, and none for tabs with nothing waiting', async () => {
    renderData({
      fake: fakeWith({ calendar: { proposals: [] }, corporateActions: [], gaps: [] }),
    });
    await ready();

    expect(await tabNames()).toEqual(['Coverage', 'Calendar', 'Corporate actions', 'Gaps', 'Runs']);
  });

  it('updates the badges when the overview changes (the counts come from it)', async () => {
    const overview = buildDataOverview({ calendarProposals: 0, pendingCorporateActions: 0 });
    renderData({ fake: createDataFake({ overview, calendar: { proposals: [] }, gaps: sampleGaps }) });

    expect(await screen.findByRole('tab', { name: 'Gaps, 3 open' })).toBeInTheDocument();
  });

  it('labels each panel by its tab', async () => {
    renderData({ fake: fakeWith() });
    const tab = await screen.findByRole('tab', { name: 'Coverage' });

    const panel = await screen.findByRole('tabpanel', { name: 'Coverage' });
    expect(tab).toHaveAttribute('aria-controls', panel.id);
    expect(panel).toHaveAttribute('aria-labelledby', tab.id);
  });

  it('scrolls sideways on a phone instead of wrapping', async () => {
    renderData({ fake: fakeWith() });

    const list = await tablist();
    expect(list.closest('.MuiTabs-root')?.querySelector('.MuiTabs-scrollableX')).not.toBeNull();
  });
});

describe('Data tabs: the tab is in the URL', () => {
  it.each([
    ['Calendar', 'calendar'],
    ['Corporate actions', 'actions'],
    ['Gaps', 'gaps'],
    ['Runs', 'runs'],
  ])('opening %s writes ?tab=%s, shows its panel, and Coverage takes it out again', async (name, value) => {
    const { router } = renderData({ fake: fakeWith() });
    await ready();

    await userEvent.click(await screen.findByRole('tab', { name: new RegExp(`^${name}`) }));

    expect(router.state.location.search).toBe(`?tab=${value}`);
    expect(await screen.findByRole('tabpanel', { name: new RegExp(`^${name}`) })).toBeInTheDocument();
    expect(screen.queryByRole('grid', { name: 'Coverage' })).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole('tab', { name: 'Coverage' }));
    expect(router.state.location.search).toBe('');
    expect(await screen.findByRole('grid', { name: 'Coverage' })).toBeInTheDocument();
  });

  it('moves between the tabs with the arrow keys', async () => {
    renderData({ fake: fakeWith() });
    await ready();

    screen.getByRole('tab', { name: 'Coverage' }).focus();
    await userEvent.keyboard('{ArrowRight}');
    expect(screen.getByRole('tab', { name: /^Calendar/ })).toHaveFocus();
    await userEvent.keyboard('{Enter}');

    expect(await selected()).toEqual(['Calendar2']);
  });

  it('adds an entry to the history for each tab, so Back steps through the tabs', async () => {
    const { router } = renderData({ fake: fakeWith() });
    await ready();

    await userEvent.click(await screen.findByRole('tab', { name: /^Calendar/ }));
    await userEvent.click(screen.getByRole('tab', { name: /^Gaps/ }));
    expect(router.state.location.search).toBe('?tab=gaps');

    await act(async () => {
      await router.navigate(-1);
    });
    expect(router.state.location.search).toBe('?tab=calendar');
    expect(await selected()).toEqual(['Calendar2']);

    await act(async () => {
      await router.navigate(-1);
    });
    expect(router.state.location.search).toBe('');
    expect(await selected()).toEqual(['Coverage']);
  });

  it('keeps the filters of the Coverage list while another tab is open', async () => {
    renderData({ fake: fakeWith(), search: '?q=tata' });
    await within(await screen.findByRole('grid', { name: 'Coverage' })).findByText('NSE:TCS');

    await userEvent.click(screen.getByRole('tab', { name: 'Runs' }));
    await screen.findByRole('tabpanel', { name: 'Runs' });
    await userEvent.click(screen.getByRole('tab', { name: 'Coverage' }));

    expect(await screen.findByRole('searchbox', { name: 'Search coverage' })).toHaveValue('tata');
  });

  it('reads only what the open tab shows', async () => {
    const fake = fakeWith();
    renderData({ fake });
    await within(await screen.findByRole('grid', { name: 'Coverage' })).findByText('NSE:TCS');

    expect(fake.calls.gaps).toEqual([]);
    expect(fake.actions.calls.list).toEqual([]);

    await userEvent.click(screen.getByRole('tab', { name: /^Gaps/ }));
    await waitFor(() => {
      expect(fake.calls.gaps).toHaveLength(1);
    });
    expect(fake.actions.calls.list).toEqual([]);
  });

  it('falls back to Coverage for a tab it does not know', async () => {
    renderData({ search: '?tab=nonsense' });

    expect(await screen.findByRole('grid', { name: 'Coverage' })).toBeInTheDocument();
    expect(await selected()).toEqual(['Coverage']);
  });
});

describe('Data tabs: the links made before the tabs still work', () => {
  it('?section=actions&actions=suggested opens Corporate actions on the suggestions, with the focus on the panel', async () => {
    const { router } = renderData({ fake: fakeWith(), search: '?section=actions&actions=suggested' });

    const panel = await screen.findByRole('tabpanel', { name: /^Corporate actions/ });
    expect(panel).toHaveFocus();
    expect(await within(panel).findByRole('button', { name: 'Suggested 2' })).toHaveAttribute('aria-pressed', 'true');
    await waitFor(() => {
      expect(router.state.location.search).toBe('?actions=suggested&tab=actions');
    });
    await waitFor(() => {
      expect(within(panel).getAllByRole('row')).toHaveLength(3);
    });
  });

  it('?actions=suggested on its own opens Corporate actions too', async () => {
    renderData({ fake: fakeWith(), search: '?actions=suggested' });

    expect(await selected()).toEqual(['Corporate actions2']);
  });

  it.each([
    ['open', 'Open 3', 3],
    ['unfillable', 'Unfillable 1', 1],
    ['filled', 'Filled 1', 1],
    ['all', 'All 5', 5],
  ])('?gaps=%s opens Gaps on that filter', async (value, chip, rows) => {
    renderData({ fake: fakeWith(), search: `?gaps=${value}` });

    const panel = await screen.findByRole('tabpanel', { name: /^Gaps/ });
    expect(await within(panel).findByRole('button', { name: chip })).toHaveAttribute('aria-pressed', 'true');
    await waitFor(() => {
      expect(within(panel).getAllByRole('row')).toHaveLength(rows + 1);
    });
    expect(await selected()).toEqual(['Gaps3']);
  });

  it('writes the tab to the URL, so clearing the old filter keeps the tab', async () => {
    const { router } = renderData({ fake: fakeWith(), search: '?gaps=open' });
    await screen.findByRole('tabpanel', { name: /^Gaps/ });
    await waitFor(() => {
      expect(router.state.location.search).toBe('?gaps=open&tab=gaps');
    });

    await userEvent.click(screen.getByRole('tab', { name: 'Coverage' }));

    expect(router.state.location.search).toBe('?gaps=open&tab=coverage');
    expect(await screen.findByRole('grid', { name: 'Coverage' })).toBeInTheDocument();
  });

  it('?id=<instrument> opens Coverage with the bars drawer on that instrument', async () => {
    const id = sampleCoverage[0]?.id ?? '';
    renderData({ fake: fakeWith(), search: `?id=${id}` });

    expect(await screen.findByRole('dialog', { name: 'Bars' })).toBeInTheDocument();
    // The drawer hides the page behind it from the accessibility tree.
    expect(await screen.findByRole('tab', { name: 'Coverage', hidden: true, selected: true })).toBeInTheDocument();
  });

  it('?hasGaps=true opens Coverage filtered to the instruments with gaps', async () => {
    renderData({ fake: fakeWith(), search: '?hasGaps=true' });

    expect(await screen.findByRole('button', { name: 'Has gaps' })).toHaveAttribute('aria-pressed', 'true');
    expect(await selected()).toEqual(['Coverage']);
    await within(await screen.findByRole('grid', { name: 'Coverage' })).findByText('NSE:INFY');
  });

  it('#corporate-actions opens the Corporate actions tab', async () => {
    renderData({ fake: fakeWith({ corporateActions: [buildCorporateAction()] }), search: '#corporate-actions' });

    expect(await screen.findByRole('tabpanel', { name: /^Corporate actions/ })).toHaveAttribute(
      'id',
      'corporate-actions',
    );
  });
});
