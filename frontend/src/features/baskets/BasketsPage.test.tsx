import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { problemResponse, server } from '@/test/server';
import { basketCard, basketIds, membersGrid, renderBaskets, symbolsShown } from './testing';

/** Makes every media query match, i.e. a phone-sized screen for the theme's `down('md')`. */
function stubPhone() {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: true,
    media: query,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
    addListener: () => undefined,
    removeListener: () => undefined,
    dispatchEvent: () => false,
    onchange: null,
  }));
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('Baskets page', () => {
  it('lists the baskets with their purpose and member count and shows the first one', async () => {
    renderBaskets();

    const list = await screen.findByRole('list', { name: 'Baskets' });
    const cards = within(list).getAllByRole('listitem');
    expect(cards).toHaveLength(3);
    expect(cards[0]).toHaveTextContent('MyChoice');
    expect(cards[0]).toHaveTextContent('manual');
    expect(cards[0]).toHaveTextContent('trading');
    expect(cards[0]).toHaveTextContent('3 instruments');
    expect(cards[1]).toHaveTextContent('MyLongTerm');
    expect(cards[1]).toHaveTextContent('long-term');
    expect(cards[1]).toHaveTextContent('0 instruments');
    expect(cards[2]).toHaveTextContent('Best stocks');

    await membersGrid();
    expect(symbolsShown()).toEqual(['NSE:TCS', 'NSE:RELIANCE', 'NSE:HDFCBANK']);
    expect(screen.getByRole('heading', { level: 2, name: 'MyChoice' })).toBeInTheDocument();
    expect(basketCard('MyChoice')).toHaveAttribute('aria-current', 'true');
  });

  it('shows name, type, sector and tracked status of each member (the added date is in the history)', async () => {
    renderBaskets();
    await membersGrid();

    const tcs = screen.getByRole('row', { name: /NSE:TCS/ });
    expect(within(tcs).getByText('Tata Consultancy Services')).toBeInTheDocument();
    expect(within(tcs).getByText('Share')).toBeInTheDocument();
    expect(within(tcs).getByText('Information Technology')).toBeInTheDocument();
    expect(within(tcs).getByText('Not tracked')).toBeInTheDocument();
    expect(within(tcs).queryByText(/Oct 2026/)).not.toBeInTheDocument();
    expect(within(tcs).getByRole('link', { name: 'NSE:TCS' })).toHaveAttribute(
      'href',
      expect.stringContaining('/instruments?id='),
    );
  });

  it('selects a basket from the list and keeps the choice in the URL', async () => {
    const { router, fake } = renderBaskets();
    await membersGrid();

    await userEvent.click(basketCard('MyLongTerm'));

    expect(await screen.findByRole('heading', { level: 2, name: 'MyLongTerm' })).toBeInTheDocument();
    expect(router.state.location.search).toBe(`?basket=${basketIds.myLongTerm}`);
    expect(await screen.findByText('This basket is empty — add instruments')).toBeInTheDocument();
    expect(basketCard('MyLongTerm')).toHaveAttribute('aria-current', 'true');
    expect(fake.calls.members.map((c) => c.id)).toContain(basketIds.myLongTerm);
  });

  it('opens the basket named in the URL', async () => {
    renderBaskets({ search: `?basket=${basketIds.bestStocks}` });

    expect(await screen.findByRole('heading', { level: 2, name: 'Best stocks' })).toBeInTheDocument();
  });

  it('falls back to the first basket for an unknown id in the URL', async () => {
    renderBaskets({ search: '?basket=00000000-0000-4000-8000-0000000009ff' });

    expect(await screen.findByRole('heading', { level: 2, name: 'MyChoice' })).toBeInTheDocument();
  });

  it('invites adding instruments to an empty basket', async () => {
    renderBaskets({ members: {} });

    expect(await screen.findByText('This basket is empty — add instruments')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Add instruments' })).toBeInTheDocument();
  });

  it('shows a loading state, then an error with Retry when the baskets cannot be loaded', async () => {
    renderBaskets();
    let calls = 0;
    server.use(
      http.get('/api/baskets', () => {
        calls += 1;
        return calls === 1
          ? problemResponse({ title: 'The baskets could not be read.', code: 'baskets.error' }, 500)
          : HttpResponse.json([]);
      }),
    );

    expect(await screen.findByText('The baskets could not be read.')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Retry' }));
    expect(await screen.findByText('No baskets yet')).toBeInTheDocument();
  });

  it('shows an error with Retry when the members cannot be loaded', async () => {
    const { fake } = renderBaskets();
    fake.failNext('members', 500, 'baskets.error', 'The members could not be read.');

    expect(await screen.findByText('The members could not be read.')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Retry' }));
    await membersGrid();
    expect(await screen.findByRole('row', { name: /NSE:TCS/ })).toBeInTheDocument();
  });

  it('shows a select instead of the cards on a phone', async () => {
    stubPhone();
    const { router } = renderBaskets();

    const select = await screen.findByRole('combobox', { name: 'Basket' });
    expect(screen.queryByRole('list', { name: 'Baskets' })).not.toBeInTheDocument();
    expect(
      within(select)
        .getAllByRole('option')
        .map((o) => o.textContent),
    ).toEqual(['MyChoice (3)', 'MyLongTerm (0)', 'Best stocks (0)']);

    await userEvent.selectOptions(select, 'MyLongTerm (0)');

    expect(await screen.findByRole('heading', { level: 2, name: 'MyLongTerm' })).toBeInTheDocument();
    expect(router.state.location.search).toBe(`?basket=${basketIds.myLongTerm}`);
  });
});
