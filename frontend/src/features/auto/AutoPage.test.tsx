import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { buildBasket, buildBasketMembers, sampleInstruments } from '@/test/fixtures';
import { buildAuto, buildAutoSuggestions, buildSuggestion } from '@/test/autoFixtures';
import { buildServiceView } from '@/test/servicesFixtures';
import { pinClock } from '@/test/pinClock';
import { renderWithProviders } from '@/test/render';
import { reservedHeights } from '@/test/reservedHeight';
import { problemResponse, server } from '@/test/server';
import { AutoPage } from './components/AutoPage';

pinClock('2026-11-10T04:00:00Z');

describe('Auto page', () => {
  it('says what ON does and that no order is placed before asking the password, then sends it', async () => {
    let body: unknown = null;
    server.use(
      http.post('/api/auto/on', async ({ request }) => {
        body = await request.json();
        return HttpResponse.json(buildAuto({ on: true }));
      }),
    );
    const user = userEvent.setup();
    renderWithProviders(<AutoPage />);
    await user.click(await screen.findByRole('button', { name: 'Turn Auto ON…' }));
    expect(screen.getByText(/no order is placed, nothing is approved or armed/i)).toBeInTheDocument();
    await user.type(screen.getByLabelText(/your password/i), 'secret');
    await user.click(screen.getByRole('button', { name: 'Turn Auto ON' }));
    await waitFor(() => {
      expect(body).toEqual({ password: 'secret' });
    });
  });

  it('shows a refusal in words and never an unavailable precondition as a pass', async () => {
    server.use(
      http.post('/api/auto/on', () =>
        problemResponse({ title: 'Preconditions failed.', code: 'auto.preconditions_failed' }, 409),
      ),
    );
    const user = userEvent.setup();
    renderWithProviders(<AutoPage />);
    expect(await screen.findByText(/Data fresh: Unavailable \(not a pass\)/)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Turn Auto ON…' }));
    await user.type(screen.getByLabelText(/your password/i), 'x');
    await user.click(screen.getByRole('button', { name: 'Turn Auto ON' }));
    expect(await screen.findByText(/auto.preconditions_failed/)).toBeInTheDocument();
  });

  it('a failed night says why, when data was last good, and links to recovery', async () => {
    server.use(
      http.get('/api/auto', () =>
        HttpResponse.json(
          buildAuto({
            night: { state: 'failed_closed', forDate: '2026-11-06', failedGate: 'data', reason: 'Bars are stale' },
          }),
        ),
      ),
    );
    renderWithProviders(<AutoPage />);
    expect(await screen.findByText(/Bars are stale/)).toBeInTheDocument();
    expect(screen.getByText(/The failed night was for Fri 6 Nov 2026\./)).toBeInTheDocument();
    expect(screen.getByText(/Data last good: unavailable\./)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Recovery' })).toHaveAttribute('href', '/services/recovery');
  });

  it('lists suggestions with reasons and evidence, filters by stock, and has no approve, arm or send control', async () => {
    server.use(
      http.get('/api/auto/suggestions', () =>
        HttpResponse.json(
          buildAutoSuggestions({
            suggestions: [buildSuggestion(), buildSuggestion({ itemId: 2, symbol: 'TCS', reasons: ['Gap fill.'] })],
          }),
        ),
      ),
    );
    const user = userEvent.setup();
    renderWithProviders(<AutoPage />);
    await user.click(await screen.findByRole('button', { name: /INFY BUY/ }));
    expect(screen.getByText('Opening range breakout held.')).toBeVisible();
    expect(screen.getAllByText(/Evidence: 120 trades/)).toHaveLength(2);
    await user.type(screen.getByLabelText('Stock'), 'tcs');
    expect(screen.queryByRole('button', { name: /INFY BUY/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /approve|arm|send|place|order/i })).not.toBeInTheDocument();
  });

  it('saves a scope of chosen baskets', async () => {
    let body: unknown = null;
    server.use(
      http.put('/api/auto/scope', async ({ request }) => {
        body = await request.json();
        return HttpResponse.json({
          isSet: true,
          version: 1,
          basketIds: [],
          instrumentIds: [],
          hash: 'h',
          changedBy: 'owner',
          changedAt: null,
          summary: '1 basket',
        });
      }),
    );
    const user = userEvent.setup();
    renderWithProviders(<AutoPage />);
    await user.click(await screen.findByRole('checkbox', { name: 'MyChoice' }));
    await user.click(screen.getByRole('button', { name: 'Save scope' }));
    await waitFor(() => {
      expect(body).toMatchObject({ basketIds: [expect.any(String)], instrumentIds: [] });
    });
  });

  it('shows an error state when Auto cannot be read', async () => {
    server.use(http.get('/api/auto', () => problemResponse({ title: 'Broken.', code: 'x' }, 500)));
    renderWithProviders(<AutoPage />);
    expect(await screen.findByRole('alert')).toBeInTheDocument();
  });

  const twoSuggestions = () =>
    http.get('/api/auto/suggestions', () =>
      HttpResponse.json(
        buildAutoSuggestions({
          suggestions: [buildSuggestion(), buildSuggestion({ itemId: 2, symbol: 'TCS', reasons: ['Gap fill.'] })],
        }),
      ),
    );

  it('filters suggestions by the members of a basket', async () => {
    const basket = buildBasket();
    const member = sampleInstruments.find((i) => i.symbol === 'TCS') ?? sampleInstruments[0];
    server.use(
      twoSuggestions(),
      http.get('/api/baskets/:id/members', () =>
        HttpResponse.json(
          buildBasketMembers(basket, [
            {
              id: 'm1',
              instrument: { ...member, symbol: 'TCS' },
              position: 0,
              validFrom: '2026-01-01T00:00:00Z',
              validTo: null,
              note: null,
              addedBy: 'owner',
              quantity: null,
              accountName: null,
            } as never,
          ]),
        ),
      ),
    );
    const user = userEvent.setup();
    renderWithProviders(<AutoPage />);
    await screen.findByRole('button', { name: /INFY BUY/ });
    await user.click(screen.getByRole('combobox', { name: 'Basket' }));
    await user.click(await screen.findByRole('option', { name: basket.name }));
    await waitFor(() => {
      expect(screen.queryByRole('button', { name: /INFY BUY/ })).not.toBeInTheDocument();
    });
    expect(screen.getByRole('button', { name: /TCS BUY/ })).toBeInTheDocument();
  });

  it('shows an empty state when there is no plan', async () => {
    renderWithProviders(<AutoPage />);
    expect(await screen.findByText(/No suggestions to show/)).toBeInTheDocument();
  });

  it('says so and shows everything when the basket members cannot be read', async () => {
    server.use(
      twoSuggestions(),
      http.get('/api/baskets/:id/members', () => problemResponse({ title: 'Broken.', code: 'x' }, 500)),
    );
    const user = userEvent.setup();
    renderWithProviders(<AutoPage />);
    await screen.findByRole('button', { name: /INFY BUY/ });
    await user.click(screen.getByRole('combobox', { name: 'Basket' }));
    await user.click(await screen.findByRole('option', { name: buildBasket().name }));
    expect(await screen.findByText(/The basket could not be read/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /INFY BUY/ })).toBeInTheDocument();
  });

  it('holds the settled height of the suggestions card while it loads, and not after', async () => {
    renderWithProviders(<AutoPage />);
    const holder = screen.getByText('Loading suggestions…').closest('[role=status]')?.parentElement;
    expect(holder).not.toBeNull();
    expect(reservedHeights(holder!).length).toBeGreaterThan(0);
    expect(await screen.findByText(/No suggestions to show/)).toBeInTheDocument();
    expect(screen.queryByText('Loading suggestions…')).not.toBeInTheDocument();
  });

  it('keeps no reserved box for the scope card when it fails', async () => {
    server.use(http.get('/api/auto/scope', () => problemResponse({ title: 'Broken.', code: 'x' }, 500)));
    renderWithProviders(<AutoPage />);
    expect(await screen.findAllByRole('alert')).not.toHaveLength(0);
    expect(screen.queryByText('Loading scope…')).not.toBeInTheDocument();
  });

  it('lists the evidence services read only, says Auto does not resume them, and reads a missing one as missing', async () => {
    server.use(
      http.get('/api/services', () =>
        HttpResponse.json({
          items: [
            buildServiceView({ id: 'shadow-write', enabled: true }),
            buildServiceView({ id: 'shadow-grade', enabled: false }),
          ],
        }),
      ),
    );
    renderWithProviders(<AutoPage />);
    const list = await screen.findByRole('list', { name: 'Evidence services' });
    expect(within(list).getByText(/Shadow write: running/)).toBeInTheDocument();
    expect(within(list).getByText(/Shadow grade: paused/)).toBeInTheDocument();
    expect(within(list).getByText(/Paper capture: missing/)).toBeInTheDocument();
    expect(within(list).getByText(/Cells refresh: missing/)).toBeInTheDocument();
    expect(screen.getByText(/Auto does not resume these/)).toBeInTheDocument();
    expect(within(list).queryByRole('button')).not.toBeInTheDocument();
  });

  it('shows an error, never running, when the services cannot be read', async () => {
    server.use(http.get('/api/services', () => problemResponse({ title: 'Down.', code: 'x.down' }, 500)));
    renderWithProviders(<AutoPage />);
    expect(await screen.findByRole('alert')).toBeInTheDocument();
    expect(screen.queryByText(/: running/)).not.toBeInTheDocument();
  });
});
