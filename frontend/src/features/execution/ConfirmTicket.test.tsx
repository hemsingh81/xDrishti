import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { pinClock } from '@/test/pinClock';
import { renderWithProviders } from '@/test/render';
import { server } from '@/test/server';
import { ExecutionPage } from './components/ExecutionPage';
import { recordWrites } from './testing';

const nowMs = pinClock('2026-10-30T04:00:00Z');
const iso = (offsetS: number) => new Date(nowMs + offsetS * 1000).toISOString();

const ticket = (over: Record<string, unknown> = {}) => ({
  id: '22222222-2222-4222-8222-222222222222',
  accountId: '11111111-1111-4111-8111-111111111111',
  accountName: 'Test Dhan Main',
  planItemId: 7,
  text: "Buy 1 share of O'NEIL <b>X</b> at 100",
  textHash: 'abc123',
  kind: 'entry',
  clientOrderId: 'xd-1',
  refPrice: 100,
  notional: 100,
  riskAtStop: 2,
  issuedAt: iso(-10),
  expiresAt: iso(80),
  serverNow: iso(0),
  requiresPassword: false,
  passwordReasons: [],
  softCodes: [],
  state: 'open',
  outcomeReason: null,
  decidedAt: null,
  ...over,
});

const tickets = (list: unknown[]) => http.get('/api/execution/confirm-tickets', () => HttpResponse.json(list));

const waitEnabled = (name: string) =>
  waitFor(
    () => {
      expect(screen.getByRole('button', { name })).toBeEnabled();
    },
    { timeout: 2000 },
  );

recordWrites();

describe('Confirm ticket card', () => {
  beforeEach(() => {
    server.use(
      http.get('/api/execution/readiness', () => HttpResponse.json({ date: '2026-10-30', accounts: [] })),
      http.get('/api/execution/day-unlocks', () => HttpResponse.json([])),
    );
  });

  it('says no order is waiting', async () => {
    server.use(tickets([]));
    renderWithProviders(<ExecutionPage />);
    expect(await screen.findByText('No order is waiting')).toBeInTheDocument();
  });

  it('shows an error for the list', async () => {
    server.use(http.get('/api/execution/confirm-tickets', () => new HttpResponse(null, { status: 500 })));
    renderWithProviders(<ExecutionPage />);
    expect(await screen.findByRole('alert')).toBeInTheDocument();
  });

  it('shows the text verbatim as text, locks the button for 800 ms and never takes focus', async () => {
    server.use(tickets([ticket()]));
    renderWithProviders(<ExecutionPage />);
    expect(await screen.findByText("Buy 1 share of O'NEIL <b>X</b> at 100")).toBeInTheDocument();
    const button = screen.getByRole('button', { name: 'Place this order at Dhan' });
    expect(button).toBeDisabled();
    expect(document.body).toHaveFocus();
    await waitEnabled('Place this order at Dhan');
    expect(document.body).toHaveFocus();
    expect(screen.getByText('Expires in 80 s')).toBeInTheDocument();
  });

  it('counts down from the server clock, not the browser clock', async () => {
    server.use(tickets([ticket({ serverNow: iso(50), expiresAt: iso(80) })]));
    renderWithProviders(<ExecutionPage />);
    expect(await screen.findByText('Expires in 30 s')).toBeInTheDocument();
  });

  it('sends one request with the hash and the key even on a double click, then waits for the broker', async () => {
    const seen: { body: unknown; key: string | null }[] = [];
    let state = 'open';
    server.use(
      http.get('/api/execution/confirm-tickets', () => HttpResponse.json([ticket({ state })])),
      http.post('/api/execution/confirm-tickets/:id/confirm', async ({ request }) => {
        seen.push({ body: await request.json(), key: request.headers.get('Idempotency-Key') });
        state = 'confirmed';
        return HttpResponse.json({ ticketId: 'x', outcome: 'confirmed', alreadyDecided: false, decidedAt: iso(1) });
      }),
    );
    const user = userEvent.setup();
    renderWithProviders(<ExecutionPage />);
    await screen.findByText(/Buy 1 share/);
    await waitEnabled('Place this order at Dhan');
    const button = screen.getByRole('button', { name: 'Place this order at Dhan' });
    await user.dblClick(button);
    expect(await screen.findByText(/waiting for the broker/)).toBeInTheDocument();
    expect(seen).toHaveLength(1);
    expect(seen[0]?.key).toBe('22222222-2222-4222-8222-222222222222');
    expect(seen[0]?.body).toMatchObject({ textHash: 'abc123' });
    expect(screen.getByRole('button', { name: 'Place this order at Dhan' })).toBeDisabled();
  });

  it('asks for the password and a reason, and keeps the button off until both are filled', async () => {
    server.use(tickets([ticket({ requiresPassword: true, passwordReasons: ['large'], softCodes: ['stale_bars'] })]));
    const user = userEvent.setup();
    renderWithProviders(<ExecutionPage />);
    await screen.findByLabelText(/Password/);
    await new Promise((r) => setTimeout(r, 900));
    expect(screen.getByRole('button', { name: 'Place this order at Dhan' })).toBeDisabled();
    await user.type(screen.getByLabelText(/Password/), 'pw');
    await user.type(screen.getByLabelText(/Reason/), 'ok');
    expect(screen.getByRole('button', { name: 'Place this order at Dhan' })).toBeEnabled();
  });

  it('shows the error, lets the owner try again, and does not double-submit', async () => {
    let calls = 0;
    server.use(
      tickets([ticket()]),
      http.post('/api/execution/confirm-tickets/:id/confirm', () => {
        calls += 1;
        return calls === 1
          ? HttpResponse.json(
              { title: 'The price moved.', code: 'execution.price_moved', status: 409 },
              { status: 409 },
            )
          : HttpResponse.json({ ticketId: 'x', outcome: 'confirmed', alreadyDecided: false, decidedAt: iso(1) });
      }),
    );
    const user = userEvent.setup();
    renderWithProviders(<ExecutionPage />);
    await screen.findByText(/Buy 1 share/);
    await waitEnabled('Place this order at Dhan');
    await user.click(screen.getByRole('button', { name: 'Place this order at Dhan' }));
    expect(await screen.findByText('The price moved.')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Try again' }));
    await user.click(screen.getByRole('button', { name: 'Place this order at Dhan' }));
    expect(await screen.findByText(/waiting for the broker/)).toBeInTheDocument();
    expect(calls).toBe(2);
  });

  it('skips an order', async () => {
    let skipped = 0;
    server.use(
      tickets([ticket()]),
      http.post('/api/execution/confirm-tickets/:id/skip', () => {
        skipped += 1;
        return HttpResponse.json({ ticketId: 'x', outcome: 'skipped', alreadyDecided: false, decidedAt: iso(1) });
      }),
    );
    const user = userEvent.setup();
    renderWithProviders(<ExecutionPage />);
    await user.click(await screen.findByRole('button', { name: 'Skip' }));
    await waitFor(() => {
      expect(skipped).toBe(1);
    });
  });

  it.each([
    ['confirmed', null, /waiting for the broker/],
    ['refused', 'price moved too far', /Refused\. price moved too far/],
    ['expired', null, /Expired\./],
    ['skipped', 'not today', /Skipped\. not today/],
  ])('shows a %s ticket with no buttons enabled', async (state, outcomeReason, text) => {
    server.use(tickets([ticket({ state, outcomeReason })]));
    renderWithProviders(<ExecutionPage />);
    expect(await screen.findByText(text)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Place this order at Dhan' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Skip' })).toBeDisabled();
  });

  it('shows an open ticket past its expiry as expired', async () => {
    server.use(tickets([ticket({ expiresAt: iso(-1) })]));
    renderWithProviders(<ExecutionPage />);
    expect(await screen.findByText(/Expired\./)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Place this order at Dhan' })).toBeDisabled();
  });
});

describe('Day unlock', () => {
  it('unlocks the day from the checklist with a password', async () => {
    let body: unknown;
    server.use(
      tickets([]),
      http.get('/api/plans', () => HttpResponse.json([])),
      http.get('/api/execution/day-unlocks', () => HttpResponse.json([])),
      http.get('/api/execution/readiness', () =>
        HttpResponse.json({
          date: '2026-10-30',
          accounts: [
            {
              accountId: '11111111-1111-4111-8111-111111111111',
              name: 'Test Dhan Main',
              state: 'ready',
              checks: [],
              checkedAt: iso(-5),
              tokenExpiresAt: null,
              readyAt: null,
              ipModifyAfter: null,
              attestation: null,
              lastRecheck: null,
              dayStates: [],
              sendsPerSecondMax: null,
            },
          ],
        }),
      ),
      http.post('/api/execution/day-unlocks', async ({ request }) => {
        body = await request.json();
        return HttpResponse.json({
          id: 1,
          accountId: 'a',
          sessionDate: '2026-10-30',
          unlockedAt: iso(0),
          expiresAt: iso(3600),
        });
      }),
    );
    const user = userEvent.setup();
    renderWithProviders(<ExecutionPage />);
    await user.click(await screen.findByRole('button', { name: 'Unlock today' }));
    await user.type(screen.getByLabelText(/Password/), 'pw');
    await user.click(screen.getAllByRole('button', { name: 'Unlock today' }).at(-1)!);
    await waitFor(() => {
      expect(body).toMatchObject({ accountId: '11111111-1111-4111-8111-111111111111', password: 'pw' });
    });
  });
});
