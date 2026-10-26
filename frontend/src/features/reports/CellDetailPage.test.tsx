import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { Schemas } from '@/shared/api';
import { CellDetailPage, TradeReplayPage } from '@/features/reports';
import { pinClock } from '@/test/pinClock';
import { renderRoutes } from '@/test/render';
import { buildEvidence, buildGroup, nonValue, runKeyA } from '@/test/reportFixtures';
import { reportServer } from '@/test/reportScreen';
import { server } from '@/test/server';

pinClock('2026-10-26T10:00:00Z');

const line = (ordinal: number, over: Partial<Schemas['TradeLineView']> = {}): Schemas['TradeLineView'] => ({
  runKey: runKeyA,
  tradeKey: `ORB|NSE:S${String(ordinal)}|BUY|2026-03-02`,
  strategy: 'ORB',
  timeframe: '15m',
  side: 'BUY',
  symbol: `S${String(ordinal)}`,
  basketIds: [],
  entryDate: '2026-03-02',
  exitDate: '2026-03-03',
  r: 1.5,
  sessionsHeld: 1,
  style: 'intraday',
  exit: 'fixed_r',
  provisional: false,
  ordinal,
  ...over,
});

function tradesServer(pages: Schemas['TradeLineView'][][]) {
  const requests: URL[] = [];
  server.use(
    http.get('/api/reports/trades', ({ request }) => {
      const url = new URL(request.url);
      requests.push(url);
      const at = Number(url.searchParams.get('cursor') ?? 0);
      const items = pages[at] ?? [];
      return HttpResponse.json({
        items,
        total: pages.flat().length,
        nextCursor: at + 1 < pages.length ? String(at + 1) : null,
        evidence: buildEvidence({ n: pages.flat().length }),
      } satisfies Schemas['TradesView']);
    }),
    http.get('/api/reports/runs', () =>
      HttpResponse.json({ runs: [], net: { state: 'unavailable', reason: 'x', confirmed: null }, defaultSetRule: 'x' }),
    ),
  );
  return requests;
}

const open = (search = '?strategy=ORB&timeframe=15m&cell=ORB%20%C2%B7%2015m') =>
  renderRoutes(
    [
      { path: '/reports/cell', element: <CellDetailPage /> },
      { path: '/reports/trades/:runKey/:ordinal', element: <TradeReplayPage /> },
    ],
    { initialPath: `/reports/cell${search}` },
  );

describe('Cell detail', () => {
  it('asks the P8-06 endpoints with the cell filters and shows the figures, the trade list and the honest lines', async () => {
    const requests = reportServer();
    const trades = tradesServer([[line(1), line(2)]]);
    open();

    expect(await screen.findByRole('heading', { name: 'ORB · 15m' })).toBeInTheDocument();
    expect(await screen.findByText('Key figures')).toBeInTheDocument();
    expect(await screen.findByRole('grid', { name: 'Trades of this cell' })).toBeInTheDocument();
    expect(await screen.findByText('S2')).toBeInTheDocument();
    const asked = requests.filter((r) => r.pathname !== '/api/reports/runs' && r.searchParams.get('lens') !== 'regime');
    expect(asked.length).toBeGreaterThan(2);
    expect(
      asked.every((r) => r.searchParams.get('strategy') === 'ORB' && r.searchParams.get('timeframe') === '15m'),
    ).toBe(true);
    expect(trades[0]?.searchParams.get('limit')).toBe('50');
    expect(trades[0]?.searchParams.get('strategy')).toBe('ORB');
    expect(screen.getByRole('heading', { name: 'R distribution' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Exit comparison' })).toHaveAttribute(
      'href',
      expect.stringContaining('view=exits'),
    );
    expect(within(screen.getByRole('region', { name: 'Regime split' })).getByText('Not available')).toBeInTheDocument();
  });

  it('pages the trade list fifty at a time by the cursor of the previous page', async () => {
    reportServer();
    const first = Array.from({ length: 50 }, (_, i) => line(i));
    const trades = tradesServer([first, [line(50)]]);
    open();

    await screen.findByText('S0');
    expect(trades).toHaveLength(1);
    expect(screen.getByText(/50 of 51/)).toBeInTheDocument();
  });

  it('lists the trades of a cell below the minimum while its figures say insufficient', async () => {
    reportServer({
      summary: {
        all: buildGroup({ n: 3, expectancyR: nonValue('insufficient', 'Too few trades.') }),
        evidence: buildEvidence({ n: 3 }),
      },
    });
    tradesServer([[line(1), line(2), line(3)]]);
    open();

    expect(await screen.findByText('S3')).toBeInTheDocument();
    expect(screen.getAllByText(/Insufficient data/).length).toBeGreaterThan(0);
  });

  it('says so when the cell has no closed trades', async () => {
    reportServer();
    tradesServer([[]]);
    open();
    expect(await screen.findByText('No closed trades in this cell.')).toBeInTheDocument();
  });

  it('opens the replay of a trade from its row', async () => {
    reportServer();
    tradesServer([[line(7)]]);
    const user = userEvent.setup();
    const { router } = open();

    await user.click(await screen.findByText('S7'));

    expect(router.state.location.pathname).toBe(`/reports/trades/${runKeyA}/7`);
  });

  it('shows the server message when the trade list fails', async () => {
    reportServer();
    server.use(
      http.get('/api/reports/trades', () =>
        HttpResponse.json({ title: 'The list failed.', code: 'x.y' }, { status: 500 }),
      ),
    );
    open();
    expect(await screen.findByText('The list failed.')).toBeInTheDocument();
  });
});
