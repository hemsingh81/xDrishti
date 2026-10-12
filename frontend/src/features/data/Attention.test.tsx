import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { coverageWithCsvEnd, sampleCorporateActions } from '@/test/actionFixtures';
import { createDataFake } from '@/test/dataFake';
import { buildCalendarDay, sampleGaps } from '@/test/fixtures';
import { ready, renderData } from './testing';

const line = () => screen.findByRole('region', { name: 'Needs attention' });
const links = async () => within(await line()).getAllByRole('link');

const everything = () =>
  createDataFake({ coverage: coverageWithCsvEnd(), corporateActions: sampleCorporateActions, gaps: sampleGaps });
const nothing = () => createDataFake({ calendar: { proposals: [] }, corporateActions: [], gaps: [] });

describe('Needs attention line', () => {
  it('names what waits, each as a link to its tab: proposals, suggested actions, open gaps', async () => {
    renderData({ fake: everything() });

    expect((await links()).map((link) => link.textContent)).toEqual([
      '2 calendar proposals',
      '2 suggested corporate actions',
      '3 open gaps',
    ]);
    expect(within(await line()).getByText('Needs your attention:')).toBeInTheDocument();
  });

  it('is a list, so the items are read one by one', async () => {
    renderData({ fake: everything() });

    expect(within(await line()).getAllByRole('listitem')).toHaveLength(3);
  });

  it('leaves out what does not wait', async () => {
    renderData({
      fake: createDataFake({
        calendar: { proposals: [] },
        coverage: coverageWithCsvEnd(),
        corporateActions: sampleCorporateActions,
        gaps: [],
      }),
    });

    expect((await links()).map((link) => link.textContent)).toEqual(['2 suggested corporate actions']);
  });

  it('says it in the singular for one', async () => {
    renderData({
      fake: createDataFake({
        calendar: { proposals: [buildCalendarDay({ date: '2026-10-02', status: 'proposed', source: 'observed' })] },
        corporateActions: sampleCorporateActions.slice(0, 1),
        gaps: sampleGaps.slice(0, 1),
      }),
    });

    expect((await links()).map((link) => link.textContent)).toEqual([
      '1 calendar proposal',
      '1 suggested corporate action',
      '1 open gap',
    ]);
  });

  it('is not on the page at all when nothing waits', async () => {
    renderData({ fake: nothing() });
    await ready();

    expect(screen.queryByRole('region', { name: 'Needs attention' })).not.toBeInTheDocument();
    expect(screen.queryByText(/Needs your attention/)).not.toBeInTheDocument();
  });

  it('does not count unfillable or filled gaps: they do not need the owner', async () => {
    renderData({
      fake: createDataFake({
        calendar: { proposals: [] },
        corporateActions: [],
        gaps: sampleGaps.filter((gap) => gap.status !== 'open'),
      }),
    });
    await ready();

    expect(screen.queryByRole('region', { name: 'Needs attention' })).not.toBeInTheDocument();
  });

  it.each([
    ['2 calendar proposals', '?tab=calendar', /^Calendar/],
    ['2 suggested corporate actions', '?tab=actions&actions=suggested', /^Corporate actions/],
    ['3 open gaps', '?tab=gaps&gaps=open', /^Gaps/],
  ])('"%s" switches to its tab (%s)', async (name, search, tab) => {
    const { router } = renderData({ fake: everything() });
    await ready();

    const link = within(await line()).getByRole('link', { name });
    expect(link).toHaveAttribute('href', `/data${search}`);
    await userEvent.click(link);

    await waitFor(() => {
      expect(router.state.location.search).toBe(search);
    });
    expect(await screen.findByRole('tab', { name: tab })).toHaveAttribute('aria-selected', 'true');
    expect(await screen.findByRole('tabpanel', { name: tab })).toBeInTheDocument();
  });

  it('stays on the page while the tab changes (the ribbon and the pipeline do not reload)', async () => {
    const fake = everything();
    renderData({ fake });
    await ready();
    const reads = fake.calls.overview;

    await userEvent.click(within(await line()).getByRole('link', { name: '3 open gaps' }));
    await screen.findByRole('tabpanel', { name: /^Gaps/ });

    expect(fake.calls.overview).toBe(reads);
  });
});
