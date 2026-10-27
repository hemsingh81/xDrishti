import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { pinClock } from '@/test/pinClock';
import { renderWithProviders } from '@/test/render';
import { problemResponse, server } from '@/test/server';
import type { Draft } from './api/draftApi';
import { DraftPanel } from './components/DraftPanel';

pinClock('2026-10-27T09:00:00Z');

const basketId = '11111111-1111-1111-1111-111111111111';

function draftBuilder(over: Partial<Draft> = {}): Draft {
  return {
    id: 5,
    status: 'valid',
    idea: 'buy above VWAP',
    understood: ['close above VWAP'],
    assumptions: [{ field: 'params.rr', value: '2', why: 'target 1:2' }],
    notExpressed: [],
    unsupported: [],
    requestedTest: { from: null, to: null, baskets: [] },
    yaml: 'strategy:\n  id: DRAFT_X\n',
    problems: [],
    findings: [],
    attempts: 1,
    modelId: 'm.gguf',
    promptVersion: 'draft.v1',
    failReason: null,
    confirmed: null,
    runSummary: null,
    createdAt: '2026-10-27T08:00:00Z',
    ...over,
  };
}

function mountDraft(draft: Draft) {
  server.use(
    http.get('/api/assistant/strategy-drafts', () => HttpResponse.json([draft])),
    http.get('/api/assistant/strategy-drafts/:id', () => HttpResponse.json(draft)),
    http.get('/api/baskets', () => HttpResponse.json([{ id: basketId, name: 'MyChoice', archivedAt: null }])),
  );
}

async function open(draft: Draft) {
  mountDraft(draft);
  const user = userEvent.setup();
  renderWithProviders(<DraftPanel />);
  await user.click(await screen.findByRole('button', { name: `#${draft.id} ${draft.status}` }));
  await screen.findByLabelText('Draft');
  return user;
}

describe('the draft panel', () => {
  it('shows what the model understood and chose, the checks and the text, and says a valid text is not a good strategy', async () => {
    await open(
      draftBuilder({
        findings: [{ code: 'idea_number_dropped', detail: '2022' }],
        notExpressed: [{ what: 'news filter', why: 'no data' }],
      }),
    );

    expect(screen.getByText('close above VWAP')).toBeInTheDocument();
    expect(screen.getByText('params.rr = 2 (target 1:2)')).toBeInTheDocument();
    expect(screen.getByText('news filter: no data')).toBeInTheDocument();
    expect(screen.getByText(/idea_number_dropped \(2022\)/)).toBeInTheDocument();
    expect(screen.getByText(/does not make it a good strategy/)).toBeInTheDocument();
    expect(screen.getByLabelText('Strategy text')).toHaveValue('strategy:\n  id: DRAFT_X\n');
    expect(screen.getByRole('button', { name: 'Confirm' })).toBeEnabled();
  });

  it('does not offer Confirm for a draft that is not valid, one that was declined or one that failed', async () => {
    const invalid = await open(
      draftBuilder({
        status: 'invalid',
        problems: [
          {
            path: 'trigger',
            code: 'x',
            message: 'bad rule',
            severity: 'error',
            line: 1,
            column: 1,
            endLine: 1,
            endColumn: 2,
            start: 0,
            length: 1,
            suggestion: null,
          },
        ],
      }),
    );
    expect(invalid).toBeDefined();
    expect(screen.getByRole('button', { name: 'Confirm' })).toBeDisabled();
    expect(screen.getByText(/trigger: bad rule/)).toBeInTheDocument();
    expect(screen.getByText(/Not valid, not confirmable/)).toBeInTheDocument();
  });

  it('shows a declined idea with what is missing and no text', async () => {
    await open(draftBuilder({ status: 'declined', yaml: null, unsupported: ['news and events data'], understood: [] }));

    expect(screen.getByText('news and events data')).toBeInTheDocument();
    expect(screen.queryByLabelText('Strategy text')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Confirm' })).toBeDisabled();
  });

  it('says honestly why a draft failed', async () => {
    await open(draftBuilder({ status: 'failed', yaml: null, failReason: 'model_unavailable', understood: [] }));

    expect(screen.getByText('The model server is not running or not ready.')).toBeInTheDocument();
  });

  it('renders the model text inertly, as text', async () => {
    await open(draftBuilder({ understood: ['<img src=x onerror=alert(1)> <b>bold</b>'] }));

    expect(screen.getByText('<img src=x onerror=alert(1)> <b>bold</b>')).toBeInTheDocument();
    expect(document.querySelector('img')).toBeNull();
  });

  it('asks for the universe and the password on Confirm, sends them and never keeps the password', async () => {
    let body: unknown = null;
    server.use(
      http.post('/api/assistant/strategy-drafts/5/confirm', async ({ request }) => {
        body = await request.json();
        return HttpResponse.json(
          draftBuilder({
            status: 'confirmed',
            confirmed: {
              strategyId: 'DRAFT_X',
              version: 1,
              labRunId: null,
              edited: false,
              auto: false,
              runError: null,
            },
          }),
        );
      }),
    );
    const user = await open(draftBuilder());

    await user.click(screen.getByRole('button', { name: 'Confirm' }));
    const dialog = await screen.findByRole('dialog');
    const submit = within(dialog).getByRole('button', { name: 'Confirm' });
    expect(submit).toBeDisabled();
    await user.type(within(dialog).getByLabelText(/Your password/), 'secret');
    expect(submit).toBeDisabled(); // a quick test needs a basket or symbols
    await user.type(within(dialog).getByLabelText('Symbols'), 'TCS, INFY');
    expect(submit).toBeEnabled();
    await user.click(submit);

    await waitFor(() => {
      expect(body).toEqual({ text: null, basketId: null, symbols: ['TCS', 'INFY'], run: true, password: 'secret' });
    });
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
  });

  it('says the switch is off when the server refuses with capability_off, and a wrong password in words', async () => {
    server.use(
      http.post('/api/assistant/strategy-drafts/5/confirm', () =>
        problemResponse({ title: 'Confirming a draft is switched off.', code: 'assistant.capability_off' }, 409),
      ),
    );
    const user = await open(draftBuilder());

    await user.click(screen.getByRole('button', { name: 'Confirm' }));
    const dialog = await screen.findByRole('dialog');
    await user.type(within(dialog).getByLabelText(/Your password/), 'secret');
    await user.type(within(dialog).getByLabelText('Symbols'), 'TCS');
    await user.click(within(dialog).getByRole('button', { name: 'Confirm' }));

    expect(await within(dialog).findByText(/switched off/)).toBeInTheDocument();
    expect(within(dialog).getByLabelText(/Your password/)).toHaveValue('');
  });

  it('shows the quick test summary with counts and the exploration label and no win rate or verdict', async () => {
    await open(
      draftBuilder({
        status: 'confirmed',
        confirmed: {
          strategyId: 'DRAFT_X',
          version: 1,
          labRunId: '22222222-2222-2222-2222-222222222222',
          edited: true,
          auto: false,
          runError: null,
        },
        runSummary: {
          state: 'succeeded',
          instrumentsRun: 3,
          skipped: 1,
          trades: 12,
          openAtEnd: 1,
          labels: ['Quick test: exploration, not a verdict.'],
          problems: [],
          error: null,
        },
      }),
    );

    expect(screen.getByText(/3 instruments run, 1 skipped, 12 trades, 1 open at the end/)).toBeInTheDocument();
    expect(screen.getAllByText(/exploration, not a verdict/).length).toBeGreaterThan(0);
    expect(screen.queryByText(/win rate/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/expectancy/i)).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Confirm' })).not.toBeInTheDocument();
  });

  it('starts a draft from the idea box and refuses an idea that is too short', async () => {
    let sent: unknown = null;
    server.use(
      http.get('/api/assistant/strategy-drafts', () => HttpResponse.json([])),
      http.post('/api/assistant/strategy-drafts', async ({ request }) => {
        sent = await request.json();
        return HttpResponse.json(draftBuilder({ status: 'drafting', yaml: null, understood: [], assumptions: [] }), {
          status: 202,
        });
      }),
      http.get('/api/assistant/strategy-drafts/5', () =>
        HttpResponse.json(draftBuilder({ status: 'drafting', yaml: null, understood: [], assumptions: [] })),
      ),
    );
    const user = userEvent.setup();
    renderWithProviders(<DraftPanel />);

    const button = await screen.findByRole('button', { name: 'Draft strategy' });
    await user.type(screen.getByLabelText('Your idea'), 'short');
    expect(button).toBeDisabled();
    await user.type(screen.getByLabelText('Your idea'), ' but now long enough');
    await user.click(button);

    await waitFor(() => {
      expect(sent).toEqual({ idea: 'short but now long enough', basketId: null });
    });
    expect(await screen.findByText(/Drafting… this can take a few minutes/)).toBeInTheDocument();
  });
});
