import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { renderWithProviders } from '@/test/render';
import { problemResponse, server } from '@/test/server';
import type { Briefing } from './api/briefingApi';
import { BriefingCard } from './index';

const build = (over: Partial<Briefing> = {}): Briefing => ({
  id: 5,
  kind: 'daily',
  subject: '2026-10-28',
  version: 1,
  source: 'model',
  reason: null,
  text: 'Plan 2026-10-28 is published and provisional.\n- AAA BUY: stop 98.50.',
  deterministicText: 'fixed',
  wordCount: 11,
  factsHash: 'a'.repeat(64),
  modelId: 'm.gguf',
  promptVersion: 'briefing.v1',
  attempts: [{ n: 1, passed: true, findings: [], latencyMs: 20 }],
  evidence: { planId: 42, digestId: null, runKeys: ['RUN1'] },
  createdAt: '2026-10-27T15:00:00Z',
  repeated: false,
  ...over,
});

const mount = (view: Briefing) => {
  server.use(http.get('/api/assistant/briefings/daily', () => HttpResponse.json(view)));
};

describe('BriefingCard', () => {
  it('shows a model text as stored with the model label, the word count and the facts used', async () => {
    mount(build());
    renderWithProviders(<BriefingCard kind="daily" subject="2026-10-28" />);

    const text = await screen.findByRole('region', { name: 'Briefing text' });
    expect(text.textContent).toBe('Plan 2026-10-28 is published and provisional.\n- AAA BUY: stop 98.50.');
    expect(
      screen.getByText('Drafted by a local model from stored records; every number was checked against them.'),
    ).toBeInTheDocument();
    expect(screen.getByText('11 words')).toBeInTheDocument();
    await userEvent.click(screen.getByText('Facts used'));
    expect(screen.getByText('Plan #42')).toBeInTheDocument();
    expect(screen.getByText('Run RUN1')).toBeInTheDocument();
    expect(screen.queryByText('Why the model text was not used')).not.toBeInTheDocument();
  });

  it.each([
    ['model_off', 'the model switch is off'],
    ['model_unavailable', 'the model was not running or did not answer'],
    ['facts_thin', 'there was too little in the stored plan to phrase'],
  ])('says in words why a fixed-format text was stored (%s)', async (reason, words) => {
    mount(build({ source: 'deterministic', reason, modelId: null, attempts: [] }));
    renderWithProviders(<BriefingCard kind="daily" subject="2026-10-28" />);

    expect(await screen.findByText(`Fixed-format text; the model was not used (${words}).`)).toBeInTheDocument();
    expect(screen.queryByText('Why the model text was not used')).not.toBeInTheDocument();
  });

  it('lists what the checker found for a rejected model text, without showing the refused text', async () => {
    mount(
      build({
        source: 'deterministic',
        reason: 'guard_rejected',
        modelId: null,
        attempts: [
          {
            n: 1,
            passed: false,
            latencyMs: 20,
            findings: [
              {
                code: 'number_not_in_facts',
                token: '98.60',
                offset: 12,
                words: 'a number that is not in the stored facts',
              },
            ],
          },
          {
            n: 2,
            passed: false,
            latencyMs: 20,
            findings: [
              { code: 'ticket_not_covered', token: 'DDD', offset: 0, words: 'a planned ticket that was left out' },
            ],
          },
        ],
      }),
    );
    renderWithProviders(<BriefingCard kind="daily" subject="2026-10-28" />);

    await userEvent.click(await screen.findByText('Why the model text was not used'));
    expect(screen.getByText('Attempt 1: a number that is not in the stored facts (98.60)')).toBeInTheDocument();
    expect(screen.getByText('Attempt 2: a planned ticket that was left out (DDD)')).toBeInTheDocument();
    expect(screen.getByText(/the checker rejected the model text/)).toBeInTheDocument();
  });

  it('says there is no briefing stored for a date (a 404 is an answer, not an error)', async () => {
    renderWithProviders(<BriefingCard kind="daily" subject="2026-10-28" />);

    expect(await screen.findByText('No briefing stored for this date.')).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('shows an error state for a failed read', async () => {
    server.use(http.get('/api/assistant/briefings/daily', () => problemResponse({ title: 'Boom.', code: 'x.y' }, 500)));
    renderWithProviders(<BriefingCard kind="daily" subject="2026-10-28" />);

    expect(await screen.findByRole('alert')).toBeInTheDocument();
  });

  it('renders markup in the text inert and adds no figure of its own', async () => {
    mount(build({ text: 'Regime is unavailable. <img src=x onerror=alert(1)> **bold** [link](http://x.invalid)' }));
    renderWithProviders(<BriefingCard kind="daily" subject="2026-10-28" />);

    const text = await screen.findByRole('region', { name: 'Briefing text' });
    expect(text.querySelector('img')).toBeNull();
    expect(text.querySelector('a')).toBeNull();
    expect(text.querySelector('strong')).toBeNull();
    expect(text.textContent).toBe(
      'Regime is unavailable. <img src=x onerror=alert(1)> **bold** [link](http://x.invalid)',
    );
    expect(within(text).queryByText(/\d/)).toBeInTheDocument(); // the digits are the stored text's own
  });

  it('reads the weekly review from the weekly endpoint', async () => {
    server.use(
      http.get('/api/assistant/briefings/weekly', ({ request }) => {
        expect(new URL(request.url).searchParams.get('week')).toBe('2026-W43');
        return HttpResponse.json(
          build({
            kind: 'weekly',
            subject: '2026-W43',
            text: 'Nothing was applied.',
            evidence: { planId: null, digestId: 7, runKeys: [] },
          }),
        );
      }),
    );
    renderWithProviders(<BriefingCard kind="weekly" subject="2026-W43" />);

    expect(await screen.findByRole('region', { name: 'Weekly review text' })).toHaveTextContent('Nothing was applied.');
    await userEvent.click(screen.getByText('Facts used'));
    expect(screen.getByText('Digest #7')).toBeInTheDocument();
  });
});
