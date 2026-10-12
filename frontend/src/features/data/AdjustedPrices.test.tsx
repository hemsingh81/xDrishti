import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { buildCorporateAction, sampleCorporateActions } from '@/test/actionFixtures';
import { createDataFake } from '@/test/dataFake';
import { dataRowId } from '@/test/fixtures';
import { renderData } from './testing';

const relianceId = dataRowId(1);
const dayTable = () => screen.findByRole('table', { name: 'Latest 1D bars' });
const toggle = (drawer: HTMLElement) => within(drawer).getByRole('switch', { name: 'Adjusted prices' });

async function openDaily(search = '', fake = createDataFake({ corporateActions: sampleCorporateActions })) {
  const result = renderData({ fake, search: `?id=${relianceId}&tf=1D${search}` });
  const drawer = await screen.findByRole('dialog', { name: 'Bars' });
  await dayTable();
  return { ...result, drawer };
}

describe('Bars drawer: adjusted prices', () => {
  it('shows raw prices to start with, with the switch off and no adjustment text', async () => {
    const { fake, drawer } = await openDaily();

    expect(toggle(drawer)).not.toBeChecked();
    const rows = within(await dayTable()).getAllByRole('row');
    expect(rows[3]).toHaveTextContent('10 Oct 2026');
    expect(rows[3]).toHaveTextContent('2,458.90');
    expect(within(drawer).queryByText(/^Adjusted by/)).not.toBeInTheDocument();
    expect(fake.calls.barsAdjusted).toEqual([null]);
  });

  it('asks for adjusted prices when the switch is turned on, and says the factor and the actions used', async () => {
    const { fake, router, drawer } = await openDaily();

    await userEvent.click(toggle(drawer));

    expect(await within(drawer).findByText('Adjusted by ×0.5 for 1 action')).toBeInTheDocument();
    expect(toggle(drawer)).toBeChecked();
    expect(fake.calls.barsAdjusted).toEqual([null, 'true']);
    expect(router.state.location.search).toContain('adjusted=true');
    const actions = within(drawer).getByRole('list', { name: 'Actions applied' });
    expect(within(actions).getByText('11 Oct 2026 — Bonus 1:1 (×0.5)')).toBeInTheDocument();
    // The bar before the ex-date has its prices halved and its volume doubled; the others are as loaded.
    const rows = within(await dayTable()).getAllByRole('row');
    await waitFor(() => {
      expect(rows[3]).toHaveTextContent('1,229.45');
    });
    expect(rows[3]).toHaveTextContent('24,690');
    expect(rows[1]).toHaveTextContent('2,458.90');
    expect(rows[1]).toHaveTextContent('12,345');
  });

  it('turns back to raw prices when the switch is turned off', async () => {
    const { fake, drawer } = await openDaily('&adjusted=true');
    await within(drawer).findByText('Adjusted by ×0.5 for 1 action');

    await userEvent.click(toggle(drawer));

    await waitFor(() => {
      expect(within(drawer).queryByText(/^Adjusted by/)).not.toBeInTheDocument();
    });
    expect(toggle(drawer)).not.toBeChecked();
    expect(fake.calls.barsAdjusted).toEqual(['true', null]);
    expect(within(await dayTable()).getAllByRole('row')[3]).toHaveTextContent('2,458.90');
  });

  it('opens adjusted when the link says so', async () => {
    const { drawer } = await openDaily('&adjusted=true');

    expect(toggle(drawer)).toBeChecked();
    expect(await within(drawer).findByText('Adjusted by ×0.5 for 1 action')).toBeInTheDocument();
  });

  it('adds the factors of several actions and counts them', async () => {
    const second = buildCorporateAction({
      id: '00000000-0000-4000-8000-000000000571',
      instrumentId: relianceId,
      symbol: 'NSE:RELIANCE',
      exDate: '2026-10-12',
      kind: 'split',
      numerator: 1,
      denominator: 5,
      status: 'applied',
      source: 'manual',
    });
    const fake = createDataFake({ corporateActions: [...sampleCorporateActions, second] });
    const { drawer } = await openDaily('&adjusted=true', fake);

    expect(await within(drawer).findByText('Adjusted by ×0.1 for 2 actions')).toBeInTheDocument();
    const actions = within(drawer).getByRole('list', { name: 'Actions applied' });
    expect(
      within(actions)
        .getAllByRole('listitem')
        .map((item) => item.textContent),
    ).toEqual(['11 Oct 2026 — Bonus 1:1 (×0.5)', '12 Oct 2026 — Split 1:5 (×0.2)']);
  });

  it('says so when no applied action changes the bars shown', async () => {
    const fake = createDataFake({ corporateActions: [] });
    const { drawer } = await openDaily('&adjusted=true', fake);

    expect(
      await within(drawer).findByText('No applied corporate action changes these bars, so the prices are as loaded.'),
    ).toBeInTheDocument();
  });

  it('ignores actions that are only suggested or ignored', async () => {
    const waiting = buildCorporateAction({ instrumentId: relianceId, symbol: 'NSE:RELIANCE', exDate: '2026-10-12' });
    const fake = createDataFake({ corporateActions: [waiting] });
    const { drawer } = await openDaily('&adjusted=true', fake);

    expect(await within(drawer).findByText(/^No applied corporate action changes these bars/)).toBeInTheDocument();
  });

  it('says in words when the bars cannot be loaded', async () => {
    const fake = createDataFake();
    fake.failures.bars = true;
    renderData({ fake, search: `?id=${relianceId}&tf=1D&adjusted=true` });

    const drawer = await screen.findByRole('dialog', { name: 'Bars' });
    expect(await within(drawer).findByText('The data service is not available.')).toBeInTheDocument();
    expect(within(drawer).getByRole('button', { name: 'Retry' })).toBeInTheDocument();
  });

  it('forgets the switch when the drawer is closed', async () => {
    const { router, drawer } = await openDaily('&adjusted=true');

    await userEvent.click(within(drawer).getByRole('button', { name: 'Close bars' }));

    await waitFor(() => {
      expect(router.state.location.search).toBe('');
    });
  });
});
