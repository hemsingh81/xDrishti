import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { BasketRankingPage } from '@/features/basket-profile';
import { basketIds, buildBasket, seededBaskets } from '@/test/fixtures';
import { buildRanking, buildRankingRow, type Ranking } from '@/test/profileFixtures';
import { pinClock } from '@/test/pinClock';
import { renderRoutes } from '@/test/render';
import { nonValue, value } from '@/test/reportFixtures';
import { problemResponse, server } from '@/test/server';

pinClock('2026-10-25T10:00:00Z');

const both = `${basketIds.myChoice},${basketIds.myLongTerm}`;

function show(ranking: Ranking | 'fail', search = `?basket=${both}`) {
  const requests: URL[] = [];
  server.use(
    http.get('/api/baskets', () => HttpResponse.json(seededBaskets())),
    http.get('/api/reports/baskets/ranking', ({ request }) => {
      requests.push(new URL(request.url));
      return ranking === 'fail'
        ? problemResponse({ title: 'The ranking failed.', code: 'reports.invalid' }, 422)
        : HttpResponse.json(ranking);
    }),
  );
  const view = renderRoutes([{ path: '/baskets/ranking', element: <BasketRankingPage /> }], {
    initialPath: `/baskets/ranking${search}`,
  });
  return { ...view, requests };
}

describe('the ranking board', () => {
  it('prints the ranks in order when the gaps are separated and never calls a basket best', async () => {
    show(
      buildRanking({
        rows: [
          buildRankingRow({ basketId: basketIds.myLongTerm, name: 'MyLongTerm', rsRank: value(2) }),
          buildRankingRow(),
        ],
      }),
    );

    const board = await screen.findByRole('table', { name: 'Ranking board' });

    const rows = within(board).getAllByRole('row').slice(1);
    expect(within(rows[0]!).getByText('MyChoice')).toBeInTheDocument();
    expect(within(rows[0]!).getByText('1')).toBeInTheDocument();
    expect(screen.queryByText(/not separated/i)).not.toBeInTheDocument();
    expect(document.body.textContent).not.toMatch(/\bbest\b/i);
  });

  it('says not separated, with the rule it uses, and prints no order when the gaps are within their noise', async () => {
    show(buildRanking({ separated: false }));

    expect(await screen.findByRole('status')).toHaveTextContent('Not separated');
    expect(screen.getByRole('status')).toHaveTextContent('indicative, pairwise and uncorrected');
    const board = screen.getByRole('table', { name: 'Ranking board' });
    expect(within(board).getAllByText('Not evaluable').length).toBe(2);
    expect(within(board).queryByText('1')).not.toBeInTheDocument();
  });

  it('shows the state of a basket with an insufficient input and no rank for it', async () => {
    const thin = buildRankingRow({
      basketId: basketIds.bestStocks,
      name: 'Best stocks',
      rsReturn21: nonValue('insufficient', 'n = 4, at least 22 needed'),
      rsRank: nonValue('insufficient', 'n = 1, at least 2 needed'),
      riskAdjusted: nonValue('empty', 'No index line.'),
      state: 'empty',
    });
    show(
      buildRanking({
        rows: [
          buildRankingRow(),
          buildRankingRow({ basketId: basketIds.myLongTerm, name: 'MyLongTerm', rsRank: value(2) }),
          thin,
        ],
      }),
      `?basket=${both},${basketIds.bestStocks}`,
    );

    const board = await screen.findByRole('table', { name: 'Ranking board' });

    const row = within(board).getByRole('row', { name: /Best stocks/ });
    expect(within(row).getByText('n 4 of at least 22')).toBeInTheDocument();
    expect(within(row).getByText('No records yet')).toBeInTheDocument();
    expect(within(row).queryByText('2')).not.toBeInTheDocument();
  });

  it('prints the overlap note when baskets share members', async () => {
    show(buildRanking({ overlap: [{ a: basketIds.myChoice, b: basketIds.myLongTerm, jaccard: value(0.4) }] }));

    expect(await screen.findByText('Baskets overlap; results are not independent.')).toBeInTheDocument();
    expect(screen.getByRole('list', { name: 'Shared members' })).toHaveTextContent(
      'MyChoice and MyLongTerm share 40.0% of their combined members.',
    );
  });

  it('does not print the overlap note when no members are shared', async () => {
    show(buildRanking());

    await screen.findByRole('table', { name: 'Ranking board' });

    expect(screen.queryByText(/Baskets overlap/)).not.toBeInTheDocument();
  });

  it('shows the evidence with the basis and its limits', async () => {
    show(buildRanking());

    const evidence = await screen.findByRole('region', { name: 'Evidence' });

    expect(evidence).toHaveTextContent('equal weight, daily rebalanced, price return, dividends not included');
    expect(evidence).toHaveTextContent('no figure here predicts');
  });

  it('asks for two baskets before it reads anything and keeps the choice in the address', async () => {
    const user = userEvent.setup();
    server.use(http.get('/api/baskets', () => HttpResponse.json([buildBasket(), ...seededBaskets().slice(1)])));
    const view = renderRoutes([{ path: '/baskets/ranking', element: <BasketRankingPage /> }], {
      initialPath: '/baskets/ranking',
    });

    expect(await screen.findByText('Choose at least two baskets to compare.')).toBeInTheDocument();
    await user.click(await screen.findByRole('checkbox', { name: 'MyChoice' }));

    expect(view.router.state.location.search).toBe(`?basket=${basketIds.myChoice}`);
    expect(screen.getByText('Choose at least two baskets to compare.')).toBeInTheDocument();
  });

  it('shows the error with a way to try again', async () => {
    show('fail');

    expect(await screen.findByText('The ranking failed.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument();
  });
});
