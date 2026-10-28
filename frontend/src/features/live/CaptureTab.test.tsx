import { screen, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { Schemas } from '@/shared/api';
import { pinClock } from '@/test/pinClock';
import { renderRoutes } from '@/test/render';
import { server } from '@/test/server';
import { LivePage } from './components/LivePage';

pinClock('2026-11-03T12:30:00Z');

type View = Schemas['LiveCaptureView'];
type Ticket = Schemas['LiveCaptureTicketView'];

const ticket = (over: Partial<Ticket> = {}): Ticket => ({
  itemId: 1,
  symbol: 'RELIANCE',
  side: 'buy',
  allocatedQty: 40,
  orderedQty: 40,
  filledQty: 40,
  avgEntry: 1250.475,
  outcome: 'filled',
  flags: [],
  missedR: null,
  orderEvidence: 'present',
  protection: 'protected',
  rejectText: null,
  ...over,
});

const view = (over: Partial<View> = {}): View => ({
  date: '2026-11-03',
  health: 'healthy',
  runs: [],
  tickets: [ticket()],
  unmatched: [],
  discretionary: [],
  evidence: {
    orderEvidence: 'present',
    pollRuns: 90,
    fills: 4,
    orders: 3,
    orderBookEnabled: true,
    limits: ['A limit, in words.'],
  },
  ...over,
});

function mount(body: View | (() => Response), initial = '/live?tab=capture') {
  server.use(
    http.get('/api/live-trading/accounts/:id/capture', () =>
      typeof body === 'function' ? body() : HttpResponse.json(body),
    ),
  );
  renderRoutes([{ path: '/live', element: <LivePage /> }], { initialPath: initial });
}

describe('the Capture tab', () => {
  it('shows the health as a word and an icon chip and the ticket with its outcome, fill and stop', async () => {
    mount(view());

    expect(await screen.findByText('Capture: Healthy')).toBeInTheDocument();
    const row = within(await screen.findByRole('table', { name: 'Approved live tickets' })).getByRole('row', {
      name: /RELIANCE/,
    });
    expect(within(row).getByText('Filled')).toBeInTheDocument();
    expect(within(row).getByText('Protected by a stop')).toBeInTheDocument();
    expect(within(row).getByText('none')).toBeInTheDocument();
  });

  it('says Gap and Not running in words', async () => {
    mount(view({ health: 'gap' }));
    expect(await screen.findByText('Capture: Gap')).toBeInTheDocument();
  });

  it('says Not running for a day with no run', async () => {
    mount(view({ health: 'not_run', tickets: [] }));
    expect(await screen.findByText('Capture: Not running')).toBeInTheDocument();
    expect(screen.getByText('No approved live ticket for this day.')).toBeInTheDocument();
  });

  it('shows the missed R only on a missed outcome and the broker text as text, never as markup', async () => {
    mount(
      view({
        tickets: [
          ticket({
            itemId: 2,
            symbol: 'TCS',
            outcome: 'rejected',
            filledQty: 0,
            avgEntry: null,
            rejectText: 'insufficient margin <script>alert(1)</script>',
          }),
          ticket({
            itemId: 3,
            symbol: 'INFY',
            outcome: 'missed_no_order',
            filledQty: 0,
            avgEntry: null,
            orderedQty: null,
            missedR: 1.2,
            protection: 'not_applicable',
          }),
        ],
      }),
    );

    expect(await screen.findByText('Dhan said: insufficient margin <script>alert(1)</script>')).toBeInTheDocument();
    expect(document.querySelector('script')).toBeNull();
    expect(screen.getByText('Plan grade 1.20 R missed')).toBeInTheDocument();
    expect(screen.getAllByText(/R missed/)).toHaveLength(1);
    expect(screen.getByText('Missed: no order placed')).toBeInTheDocument();
    expect(screen.getByText('Rejected')).toBeInTheDocument();
  });

  it('shows the words unavailable and unknown - never a digit - when there is no order evidence', async () => {
    mount(
      view({
        evidence: {
          orderEvidence: 'unavailable',
          pollRuns: 3,
          fills: 4,
          orders: 0,
          orderBookEnabled: false,
          limits: [],
        },
        tickets: [ticket({ outcome: null, orderedQty: null, orderEvidence: 'unavailable', protection: 'unavailable' })],
      }),
    );

    const row = within(await screen.findByRole('table', { name: 'Approved live tickets' })).getByRole('row', {
      name: /RELIANCE/,
    });
    expect(within(row).getAllByText('unavailable').length).toBeGreaterThan(0);
    expect(within(row).getByText('Pending the settle')).toBeInTheDocument();
    expect(screen.getByText(/the order book was not read/)).toBeInTheDocument();
    expect(screen.getByText(/Live__Capture__OrderBook__Enabled/)).toBeInTheDocument();
  });

  it('shows an unprotected entry with the word and flags the chase and the size', async () => {
    mount(view({ tickets: [ticket({ flags: ['chased_entry', 'unprotected'], protection: 'unprotected' })] }));

    const row = within(await screen.findByRole('table', { name: 'Approved live tickets' })).getByRole('row', {
      name: /RELIANCE/,
    });
    expect(within(row).getByText('Chased entry, Unprotected')).toBeInTheDocument();
    expect(within(row).getByText('Unprotected')).toBeInTheDocument();
  });

  it('lists the unmatched fills with the reason and the owner’s own trades', async () => {
    const fill = {
      symbol: 'SBIN',
      side: 'buy',
      product: 'CNC',
      qty: 100,
      price: 800,
      time: '10:15:00',
      reason: 'no approved ticket',
    };
    mount(
      view({
        unmatched: [{ ...fill, symbol: 'ITC', reason: 'ambiguous: 2 approved tickets fit' }],
        discretionary: [fill],
      }),
    );

    expect(await screen.findByText('ambiguous: 2 approved tickets fit')).toBeInTheDocument();
    expect(screen.getByText('no approved ticket')).toBeInTheDocument();
    expect(screen.getAllByRole('table', { name: 'Fills no ticket owns' })).toHaveLength(1);
    expect(screen.getByRole('table', { name: 'Your own trades' })).toBeInTheDocument();
  });

  it('shows the error state when the capture cannot be read', async () => {
    mount(() =>
      HttpResponse.json({ title: 'No such live account', status: 404, code: 'live.not_found' }, { status: 404 }),
    );

    expect(await screen.findByRole('alert')).toBeInTheDocument();
  });

  it('refuses a typed future day and asks for the chosen day', async () => {
    mount(view());
    const day = await screen.findByLabelText('Day');

    expect(day).toHaveAttribute('max', '2026-11-03');
  });

  it('holds no mutation: the tab imports no mutation hook and posts nothing', () => {
    const sources = import.meta.glob<string>(['./components/Capture*.tsx', './api/captureQueries.ts'], {
      query: '?raw',
      import: 'default',
      eager: true,
    });
    expect(Object.keys(sources).length).toBeGreaterThanOrEqual(4);
    for (const source of Object.values(sources)) {
      expect(source).not.toMatch(/useMutation|api\.(POST|PUT|PATCH|DELETE)/);
    }
  });
});
