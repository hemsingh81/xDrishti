import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { pinClock } from '@/test/pinClock';
import { renderWithProviders } from '@/test/render';
import { problemResponse, server } from '@/test/server';
import type { Proposal } from './api/proposalApi';
import { SuggestionsPage } from './components/SuggestionsPage';

pinClock('2026-10-28T09:00:00Z');

function proposal(over: Partial<Proposal> = {}): Proposal {
  return {
    id: 1,
    kind: 'basket_add',
    subject: 'Add EVB to basket EvalAlpha',
    state: 'pending',
    reason: 'EVB has 40 trades and a lower bound of 0.31. <b>not bold</b>',
    payload: { baselineRunKey: null, cellKey: null, knob: null, basketId: 'b1', symbol: 'EVB' },
    evidence: {
      basis: 'gross R',
      population: ['r1'],
      dateRange: '2025-01-01 to 2026-10-01',
      n: 40,
      figures: [
        { label: 'Instrument trades', value: '40', state: 'value', n: 40 },
        { label: 'Instrument expectancy lower bound (R)', value: '', state: 'insufficient', n: 40 },
      ],
      limits: ['Only closed trades are counted.'],
    },
    checks: [
      { code: 'evidence_resolved', ok: true, detail: 'Every reference was read again.' },
      { code: 'trade_evidence', ok: false, detail: 'Too few trades.' },
    ],
    effect: 'Adds EVB to basket EvalAlpha.',
    notes: ['Accepting changes a basket.'],
    createdAt: '2026-10-28T08:00:00Z',
    expiresAt: '2026-10-31T08:00:00Z',
    createdBy: 'system:proposals',
    modelId: 'm.gguf',
    decision: null,
    ...over,
  };
}

function mount(list: Proposal[]) {
  server.use(http.get('/api/assistant/proposals', () => HttpResponse.json(list)));
}

describe('the suggestions screen', () => {
  it('says there are none', async () => {
    mount([]);
    renderWithProviders(<SuggestionsPage />);

    expect(await screen.findByText('No suggestions')).toBeInTheDocument();
  });

  it('shows an error state when the list cannot be read', async () => {
    server.use(http.get('/api/assistant/proposals', () => problemResponse({ title: 'Broken', code: 'x' }, 500)));
    renderWithProviders(<SuggestionsPage />);

    expect(await screen.findByRole('alert')).toBeInTheDocument();
  });

  it('shows the card with evidence, checks as words, markup inert and no digit for an insufficient figure', async () => {
    mount([proposal()]);
    renderWithProviders(<SuggestionsPage />);

    const card = await screen.findByRole('article', { name: 'Add to basket: Add EVB to basket EvalAlpha' });
    expect(within(card).getByText(/<b>not bold<\/b>/)).toBeInTheDocument();
    expect(card.querySelector('b')).toBeNull();
    expect(within(card).getByText('Instrument trades: 40')).toBeInTheDocument();
    expect(within(card).getByText('Instrument expectancy lower bound (R): insufficient')).toBeInTheDocument();
    const checks = within(card).getByRole('list', { name: 'Engine checks' });
    expect(within(checks).getByText('Passed: Every reference was read again.')).toBeInTheDocument();
    expect(within(checks).getByText('Failed: Too few trades.')).toBeInTheDocument();
    expect(within(card).getByText('Only closed trades are counted.')).toBeInTheDocument();
  });

  it('lists pending first and shows a decided one without buttons', async () => {
    mount([
      proposal({
        id: 3,
        state: 'accepted',
        subject: 'Add DONE to basket B',
        decision: {
          state: 'accepted',
          actor: 'owner:o',
          auto: true,
          reason: null,
          resultRef: 'basket:x',
          decidedAt: '2026-10-28T08:30:00Z',
        },
      }),
      proposal({ id: 2, subject: 'Add OPEN to basket B' }),
    ]);
    renderWithProviders(<SuggestionsPage />);

    const cards = await screen.findAllByRole('article');
    expect(cards[0]).toHaveAccessibleName(/OPEN/);
    expect(within(cards[1]!).queryByRole('button', { name: 'Accept' })).not.toBeInTheDocument();
    expect(within(cards[1]!).getByText('accepted automatically')).toBeInTheDocument();
  });

  it('asks the password in a dialog that says exactly what will happen, then closes on success', async () => {
    let body: unknown = null;
    mount([proposal()]);
    server.use(
      http.post('/api/assistant/proposals/:id/accept', async ({ request }) => {
        body = await request.json();
        return HttpResponse.json(proposal({ state: 'accepted' }));
      }),
    );
    const user = userEvent.setup();
    renderWithProviders(<SuggestionsPage />);

    await user.click(await screen.findByRole('button', { name: 'Accept' }));
    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByText('Adds EVB to basket EvalAlpha.')).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Accept' })).toBeDisabled();
    await user.type(within(dialog).getByLabelText(/Your password/), 'secret-pw');
    await user.click(within(dialog).getByRole('button', { name: 'Accept' }));

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
    expect(body).toEqual({ password: 'secret-pw' });
  });

  it.each([
    [401, 'auth.invalid_credentials', 'The password was not accepted. Nothing was changed.'],
    [409, 'assistant.capability_off', 'Accepting is switched off.'],
    [409, 'assistant.proposal_stale', 'The checks no longer pass'],
    [409, 'assistant.proposal_closed', 'This suggestion is closed'],
  ])('shows the reason of a refused accept (%s %s) and clears the password', async (status, code, text) => {
    mount([proposal()]);
    server.use(
      http.post('/api/assistant/proposals/:id/accept', () => problemResponse({ title: 'Refused', code }, status)),
    );
    const user = userEvent.setup();
    renderWithProviders(<SuggestionsPage />);

    await user.click(await screen.findByRole('button', { name: 'Accept' }));
    const dialog = await screen.findByRole('dialog');
    const password = within(dialog).getByLabelText(/Your password/);
    await user.type(password, 'secret-pw');
    await user.click(within(dialog).getByRole('button', { name: 'Accept' }));

    expect(await within(dialog).findByText(new RegExp(text))).toBeInTheDocument();
    expect(password).toHaveValue('');
  });

  it('rejects without a password', async () => {
    let called = false;
    mount([proposal()]);
    server.use(
      http.post('/api/assistant/proposals/:id/reject', () => {
        called = true;
        return HttpResponse.json(proposal({ state: 'rejected' }));
      }),
    );
    const user = userEvent.setup();
    renderWithProviders(<SuggestionsPage />);

    await user.click(await screen.findByRole('button', { name: 'Reject' }));

    await waitFor(() => {
      expect(called).toBe(true);
    });
  });

  it('shows the hypothesis wording of the dialog', async () => {
    mount([
      proposal({
        kind: 'hypothesis',
        subject: 'ORB 15m BUY: rr 2 to 2.5',
        effect:
          'Writes hypothesis ORB 15m BUY: rr 2 to 2.5 as proposed; it will not run or change anything until you approve it.',
      }),
    ]);
    const user = userEvent.setup();
    renderWithProviders(<SuggestionsPage />);

    await user.click(await screen.findByRole('button', { name: 'Accept' }));

    expect(await screen.findByText(/it will not run or change anything until you approve it/)).toBeInTheDocument();
  });
});
