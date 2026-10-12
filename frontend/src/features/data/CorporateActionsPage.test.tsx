import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { coverageWithCsvEnd, sampleCorporateActions } from '@/test/actionFixtures';
import { createDataFake } from '@/test/dataFake';
import { ready, renderData } from './testing';

const attention = () => screen.queryByRole('region', { name: 'Needs attention' });
const panel = () => screen.findByRole('tabpanel', { name: /^Corporate actions/ });

const fakeWith = (options: Parameters<typeof createDataFake>[0] = {}) =>
  createDataFake({
    coverage: coverageWithCsvEnd(),
    corporateActions: sampleCorporateActions,
    calendar: { proposals: [] },
    ...options,
  });

describe('Data page: suggested corporate actions', () => {
  it('puts the number of suggestions in the attention line and on the tab', async () => {
    renderData({ fake: fakeWith() });
    await ready();

    const line = await screen.findByRole('region', { name: 'Needs attention' });
    expect(within(line).getByRole('link', { name: '2 suggested corporate actions' })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Corporate actions, 2 suggested' })).toHaveAttribute(
      'aria-selected',
      'false',
    );
  });

  it('says it in the singular for one suggestion', async () => {
    renderData({ fake: fakeWith({ corporateActions: sampleCorporateActions.slice(0, 1) }) });

    expect(await screen.findByRole('link', { name: '1 suggested corporate action' })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Corporate actions, 1 suggested' })).toBeInTheDocument();
  });

  it('shows no line and no badge when nothing waits', async () => {
    renderData({ fake: fakeWith({ corporateActions: sampleCorporateActions.slice(2) }) });
    await ready();

    expect(attention()).toBeNull();
    expect(screen.getByRole('tab', { name: 'Corporate actions' })).toBeInTheDocument();
  });

  it('the link opens the Corporate actions tab on the suggestions', async () => {
    const { router } = renderData({ fake: fakeWith() });
    await ready();

    await userEvent.click(await screen.findByRole('link', { name: '2 suggested corporate actions' }));

    await waitFor(() => {
      expect(router.state.location.search).toBe('?tab=actions&actions=suggested');
    });
    expect(await screen.findByRole('tab', { name: /^Corporate actions/ })).toHaveAttribute('aria-selected', 'true');
    expect(await within(await panel()).findByRole('button', { name: 'Suggested 2' })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
    expect(within(await panel()).getByRole('grid', { name: 'Corporate actions list' })).toBeInTheDocument();
  });

  it('the line goes away once the suggestions are decided', async () => {
    renderData({ fake: fakeWith(), search: '?actions=suggested' });
    const grid = await within(await panel()).findByRole('grid', { name: 'Corporate actions list' });

    await userEvent.click(await within(grid).findByRole('button', { name: /^Ignore NSE:INFY/ }));
    await userEvent.click(await within(grid).findByRole('button', { name: /^Ignore NSE:TCS/ }));

    await waitFor(() => {
      expect(attention()).toBeNull();
    });
  });

  it('a link with ?section=actions opens the tab on the suggestions with the focus on it, and drops the parameter', async () => {
    const { router } = renderData({ fake: fakeWith(), search: '?section=actions&actions=suggested' });

    expect(await panel()).toHaveFocus();
    await waitFor(() => {
      expect(router.state.location.search).toBe('?actions=suggested&tab=actions');
    });
    expect(await within(await panel()).findByRole('button', { name: 'Suggested 2' })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
  });

  it('keeps the tab when the filter that led to it is cleared', async () => {
    const { router } = renderData({ fake: fakeWith(), search: '?actions=suggested' });
    await within(await panel()).findByRole('grid', { name: 'Corporate actions list' });

    await userEvent.click(await within(await panel()).findByRole('button', { name: /^All/ }));

    await waitFor(() => {
      expect(router.state.location.search).toBe('?tab=actions');
    });
    expect(await panel()).toBeInTheDocument();
  });

  it('the tab can be reached by its anchor id', async () => {
    renderData({ fake: fakeWith(), search: '?tab=actions' });

    expect(await panel()).toHaveAttribute('id', 'corporate-actions');
  });
});
