import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { priceStub } from '@/test/chartStub';
import { pinClock } from '@/test/pinClock';
import { renderRoutes } from '@/test/render';
import { RUN, tradeChart } from '@/test/replaySr02Fixtures';
import { problemResponse, server } from '@/test/server';
import { ReplayTradeChartPage } from './components/replay/ReplayTradeChartPage';
import { ReplayTrades } from './components/replay/ReplayTrades';

pinClock('2026-11-12T04:00:00Z');

const path = `/simulation/replay/${RUN}/trades/1`;
const open = (initialPath = path) =>
  renderRoutes([{ path: '/simulation/replay/:runId/trades/:tradeNo', element: <ReplayTradeChartPage /> }], {
    initialPath,
  });

describe('replay trade chart', () => {
  it('draws the bars with the entry and stop levels and the exit, and says the exit price is derived', async () => {
    const asked: string[] = [];
    server.use(
      http.get('/api/replay/runs/:id/trades/:no/chart', ({ request }) => {
        asked.push(new URL(request.url).search);
        return HttpResponse.json(tradeChart());
      }),
    );
    open();
    expect(await screen.findByRole('heading', { name: 'Price, entry, stop and exit' })).toBeInTheDocument();
    expect(await screen.findByTestId('price-canvas')).toBeInTheDocument();
    expect(priceStub.last?.candles).toHaveLength(2);
    expect(priceStub.last?.lines.map((l) => l.title)).toEqual(['Entry 100.00', 'Stop 98.00']);
    expect(priceStub.last?.markers.map((m) => m.text)).toEqual(['Entry', 'Exit']);
    expect(screen.getByText(/The exit price is derived, not a stored fill/)).toBeInTheDocument();
    expect(screen.getByText('Exit (price derived)')).toBeInTheDocument();
    expect(screen.getByText('The bars stop at the hold-out clamp (2025-06-29).')).toBeInTheDocument();
    expect(screen.getByText('Bars are not adjusted for later corporate actions.')).toBeInTheDocument();
    expect(screen.getByRole('note', { name: 'Hindsight labels' })).toBeInTheDocument();
    expect(asked[0]).toBe('?timeframe=5m');
  });

  it('asks for another timeframe when its tab is chosen', async () => {
    const asked: string[] = [];
    server.use(
      http.get('/api/replay/runs/:id/trades/:no/chart', ({ request }) => {
        asked.push(new URL(request.url).search);
        return HttpResponse.json(tradeChart());
      }),
    );
    open();
    const user = userEvent.setup();
    await user.click(await screen.findByRole('tab', { name: '15m' }));
    await screen.findByRole('heading', { name: 'Price, entry, stop and exit' });
    expect(asked).toContain('?timeframe=15m');
  });

  it('says so when there are no bars', async () => {
    server.use(
      http.get('/api/replay/runs/:id/trades/:no/chart', () =>
        HttpResponse.json(
          tradeChart({
            chart: {
              ...tradeChart().chart,
              bars: [],
              barCount: 0,
              coherence: {
                state: 'no_bars',
                entryStored: 100,
                entryOnBars: null,
                detail: 'No instrument at the entry date.',
              },
            },
          }),
        ),
      ),
    );
    open();
    expect(await screen.findByText(/No bars are stored for this window, so no chart is drawn/)).toBeInTheDocument();
    expect(screen.queryByTestId('price-canvas')).not.toBeInTheDocument();
  });

  it('says a trade is not found on a 404', async () => {
    server.use(
      http.get('/api/replay/runs/:id/trades/:no/chart', () =>
        problemResponse({ title: 'Not found', code: 'replay.not_found' }, 404),
      ),
    );
    open();
    expect(await screen.findByText(/Trade not found/)).toBeInTheDocument();
  });

  it('shows an error for any other failure', async () => {
    server.use(
      http.get('/api/replay/runs/:id/trades/:no/chart', () => problemResponse({ title: 'Too many bars.' }, 422)),
    );
    open();
    expect(await screen.findByRole('alert')).toBeInTheDocument();
  });

  it('is reached from a trade row of the trades table', async () => {
    server.use(
      http.get('/api/replay/runs/:id/trades', () =>
        HttpResponse.json({
          runId: RUN,
          completeness: { complete: true, sessionsDone: 1, sessionsTotal: 1, asOfSession: '2024-01-02' },
          costsLabel: 'c',
          trades: [
            {
              tradeNo: 7,
              symbol: 'AAA',
              side: 'BUY',
              strategy: 'orb',
              cellKey: 'k',
              qty: 10,
              entrySession: '2024-01-03',
              entryPrice: 100,
              stopPrice: 98,
              swing: false,
              exitSession: '2024-01-04',
              grossPnl: 50,
              charges: null,
              netPnl: null,
              r: 0.5,
            },
          ],
        }),
      ),
    );
    renderRoutes([{ path: '/', element: <ReplayTrades runId={RUN} active={false} /> }]);
    const row = within(await screen.findByRole('table', { name: 'Replay trades' }));
    expect(row.getByRole('link', { name: /Chart of AAA BUY, trade 7/ })).toHaveAttribute(
      'href',
      `/simulation/replay/${RUN}/trades/7`,
    );
  });
});
