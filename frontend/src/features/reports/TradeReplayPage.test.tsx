import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { TradeReplayPage } from '@/features/reports';
import { priceStub } from '@/test/chartStub';
import { pinClock } from '@/test/pinClock';
import { renderRoutes } from '@/test/render';
import { buildReplay, istEpoch, replayServer } from '@/test/replayFixtures';
import { runKeyA } from '@/test/reportFixtures';

pinClock('2026-10-26T10:00:00Z');

const markup = '<img src=x onerror=alert(1)>';
const open = (search = '') =>
  renderRoutes([{ path: '/reports/trades/:runKey/:ordinal', element: <TradeReplayPage /> }], {
    initialPath: `/reports/trades/${runKeyA}/4${search}`,
  });

describe('Trade replay', () => {
  it('draws the stored trade on its bars with stop, target and entry levels, and an events table with the same numbers', async () => {
    const requests = replayServer();
    open();

    await screen.findByTestId('price-canvas');
    expect(screen.getByText(/Simulated trade, replayed history/)).toBeInTheDocument();
    expect(priceStub.last?.candles).toHaveLength(16);
    expect(priceStub.last?.lines.map((l) => l.title)).toEqual(['Entry 100.50', 'Stop 98.00', 'Target 105.50']);
    expect(priceStub.last?.markers.map((m) => m.text)).toEqual(['Entry 100%', 'Exit 100%']);
    const events = within(screen.getByRole('table', { name: 'Events of the trade' }));
    expect(events.getByText('105.50')).toBeInTheDocument();
    expect(requests[0]?.pathname).toBe(`/api/reports/trades/${runKeyA}/4`);
    expect(requests[0]?.searchParams.get('timeframe')).toBe('5m');
  });

  it('carries the licence duty: the NOTICE sentence and the link to tradingview.com', async () => {
    replayServer();
    open();

    await screen.findByTestId('price-canvas');
    expect(
      screen.getByText(
        /TradingView Lightweight Charts\u2122 Copyright \(\u0441\) 2025 TradingView, Inc\. https:\/\/www\.tradingview\.com\//,
      ),
    ).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'tradingview.com' })).toHaveAttribute(
      'href',
      'https://www.tradingview.com/',
    );
  });

  it('asks for another timeframe and sessions through the address, and offers no target line for a trade without one', async () => {
    const view = buildReplay();
    view.trade.targetPrice = null;
    const requests = replayServer(view);
    const user = userEvent.setup();
    open();
    await screen.findByTestId('price-canvas');
    expect(priceStub.last?.lines.map((l) => l.kind)).toEqual(['entry', 'stop']);

    await user.click(screen.getByRole('tab', { name: '15m' }));

    await screen.findByTestId('price-canvas');
    expect(requests.at(-1)?.searchParams.get('timeframe')).toBe('15m');
  });

  it('warns with both prices when the bars no longer match, and still draws the chart', async () => {
    const view = buildReplay();
    view.chart.coherence = { state: 'entry_mismatch', entryStored: 100.5, entryOnBars: 50.25, detail: 'Data changed.' };
    replayServer(view);
    open();

    expect(await screen.findByText(/The bars no longer match this stored trade/)).toHaveTextContent('100.5');
    expect(screen.getByText(/The bars no longer match this stored trade/)).toHaveTextContent('50.25');
    expect(screen.getByTestId('price-canvas')).toBeInTheDocument();
  });

  it('draws no chart without bars but keeps the events and the facts', async () => {
    const view = buildReplay();
    view.chart.bars = [];
    view.chart.barCount = 0;
    view.chart.coherence = {
      state: 'no_bars',
      entryStored: 100.5,
      entryOnBars: null,
      detail: 'There are no bars in the window.',
    };
    replayServer(view);
    open();

    expect(await screen.findByText(/No bars are stored for this window, so no chart is drawn/)).toBeInTheDocument();
    expect(screen.queryByTestId('price-canvas')).not.toBeInTheDocument();
    expect(screen.getByRole('table', { name: 'Events of the trade' })).toBeInTheDocument();
    expect(screen.getByText('The stored trade')).toBeInTheDocument();
  });

  it('says a gap through the stop in the events table', async () => {
    const view = buildReplay();
    view.trade.stopPrice = 415;
    view.trade.exitPrice = 412.3;
    view.trade.exitReason = 'stop';
    replayServer(view);
    open();

    expect(await screen.findByText(/exit at 412\.30, gap through the stop 415\.00/)).toBeInTheDocument();
  });

  it('shows no net figure and no zero for a costed run whose sheet is not confirmed, and net with its words when it is', async () => {
    const view = buildReplay({
      costing: { sheetId: 'sheet-1', confirmed: false, quantity: null, totalCharges: null, costR: null, rNet: null },
    });
    replayServer(view);
    open();
    expect(
      await screen.findByText('Costed on sheet sheet-1, not confirmed as yours. The figures above are gross.'),
    ).toBeInTheDocument();
    expect(screen.queryByText(/Net of charges/)).not.toBeInTheDocument();
  });

  it('shows net of charges with the confirmed sheet', async () => {
    replayServer(
      buildReplay({
        basis: 'net',
        costing: { sheetId: 'sheet-1', confirmed: true, quantity: 100, totalCharges: 20.5, costR: 0.08, rNet: 1.87 },
      }),
    );
    open();
    expect(
      await screen.findByText(
        /Net of charges \(sheet sheet-1, confirmed as yours\): \+1\.87 R; charges ₹20\.5 on 100 shares/,
      ),
    ).toBeInTheDocument();
  });

  it('lists the stored features by group; an unknown is a word and a reason, never a zero', async () => {
    replayServer(
      buildReplay({
        features: {
          state: 'value',
          version: 'tf-1',
          reason: null,
          items: [
            { name: 'rsi14', source: 'bars', state: 'value', value: 61.2345, reason: null },
            {
              name: 'vix',
              source: 'context',
              state: 'unavailable',
              value: null,
              reason: 'market context not built (P3-01d/e)',
            },
            { name: 'minute_of_day', source: 'plan', state: 'value', value: 600, reason: null },
          ],
        },
      }),
    );
    open();

    const table = within(await screen.findByRole('table', { name: 'Decision-time features' }));
    expect(table.getByText('Bars')).toBeInTheDocument();
    expect(table.getByText('61.2345')).toBeInTheDocument();
    expect(within(table.getByText('vix').closest('tr')!).getByText('Not available')).toBeInTheDocument();
    expect(table.getByText('market context not built (P3-01d/e)')).toBeInTheDocument();
  });

  it('says so when the trade has no stored features', async () => {
    replayServer();
    open();
    expect(
      await screen.findByText(/No decision-time features stored for this trade \(run `xd learn features`\)/),
    ).toBeInTheDocument();
    expect(screen.queryByRole('table', { name: 'Decision-time features' })).not.toBeInTheDocument();
  });

  it('renders server text as text', async () => {
    const view = buildReplay({ limits: [markup] });
    view.chart.coherence = { state: 'entry_mismatch', entryStored: 1, entryOnBars: 2, detail: markup };
    view.legs = [
      {
        at: new Date(istEpoch(10, 0) * 1000).toISOString(),
        kind: 'entry',
        price: 100.5,
        filledPrice: 100.5,
        share: 1,
        reason: markup,
      },
    ];
    replayServer(view);
    const { container } = open();

    await screen.findByTestId('price-canvas');
    expect(container.querySelector('img')).toBeNull();
    expect(screen.getAllByText(new RegExp(markup.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'))).length).toBeGreaterThan(0);
  });

  it('says Trade not found for a trade that is not stored, and for an address that is not a trade', async () => {
    replayServer({ problem: 'No stored trade has this run key and ordinal.', code: 'reports.not_found', status: 404 });
    const first = open();
    expect(await screen.findByText(/Trade not found/)).toBeInTheDocument();
    first.unmount();

    renderRoutes([{ path: '/reports/trades/:runKey/:ordinal', element: <TradeReplayPage /> }], {
      initialPath: `/reports/trades/${runKeyA}/abc`,
    });
    expect(await screen.findByText(/Trade not found/)).toBeInTheDocument();
  });

  it('offers a larger timeframe when the window is too large, and never thins the bars', async () => {
    replayServer({
      problem: 'The window holds 6000 bars; at most 5000 are returned.',
      code: 'reports.window_too_large',
      status: 422,
    });
    open('?timeframe=1m');

    expect(await screen.findByText(/The window holds 6000 bars/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Use 5m' })).toBeInTheDocument();
    expect(screen.queryByTestId('price-canvas')).not.toBeInTheDocument();
  });

  it('shows the server error for another failure', async () => {
    replayServer({ problem: 'Something broke.', code: 'x.y', status: 500 });
    open();
    expect(await screen.findByText(/Something broke/)).toBeInTheDocument();
  });
});
