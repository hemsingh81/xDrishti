import { screen, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { buildEvaluation } from '@/test/eventsFixtures';
import { pendingResponse } from '@/test/gate';
import { problemResponse, server } from '@/test/server';
import { renderLearning } from './testing';

type Evaluation = ReturnType<typeof buildEvaluation>;

function serve(byFamily: Record<string, Evaluation[]> = {}) {
  server.use(
    http.get('/api/research/extensions', ({ request }) => {
      const family = new URL(request.url).searchParams.get('family') ?? '';
      return HttpResponse.json({ family, items: byFamily[family] ?? [] });
    }),
  );
}

const open = () => renderLearning('?tab=extensions');
const card = (name: RegExp) => screen.findByRole('article', { name });

describe('Learning: fundamentals and events', () => {
  it('shows a loading state, then every family, an empty one in words', async () => {
    serve();
    server.use(http.get('/api/research/extensions', () => pendingResponse()));
    open();
    expect((await screen.findAllByText(/Loading (filter:results|ext:ev-1|pead)/)).length).toBeGreaterThan(0);
  });

  it('says no evaluation is stored when a family has none', async () => {
    serve();
    open();
    await vi.waitFor(() => {
      expect(screen.getAllByText(/no evaluation stored yet/)).toHaveLength(5);
    });
    expect(screen.getByRole('heading', { name: 'Post-results drift (PEAD)' })).toBeInTheDocument();
  });

  it('shows the problem title when a family cannot be read', async () => {
    server.use(http.get('/api/research/extensions', () => problemResponse({ title: 'Extensions are down.' }, 500)));
    open();
    expect((await screen.findAllByText('Extensions are down.')).length).toBeGreaterThan(0);
  });

  it('shows NOT_EVALUABLE with the missing quantity as text, the shares and the share lost to no_upcoming_entry', async () => {
    serve({ 'filter:results': [buildEvaluation()] });
    open();
    const filter = await card(/filter:results development N=2/);
    expect(within(filter).getByText('Verdict: NOT EVALUABLE')).toBeInTheDocument();
    expect(within(filter).getByText('seasons 8 of 20')).toBeInTheDocument();
    const shares = within(filter).getByRole('table', { name: 'Filter shares' });
    expect(within(shares).getByText('unavailable: no upcoming entry')).toBeInTheDocument();
    expect(within(filter).getByText('8.0%', { selector: 'strong' })).toBeInTheDocument();
    expect(within(filter).getByText(/Share lost to no_upcoming_entry/)).toBeInTheDocument();
    expect(screen.getAllByText('The hold-out has not been read for this family yet.').length).toBeGreaterThan(0);
  });

  it('shows a number that is not stored as a word, never a zero', async () => {
    serve({
      'filter:results': [
        buildEvaluation({
          estimates: {
            share_lost_to_no_upcoming_entry: 'unavailable',
            skipped: 'unavailable',
            unavailable_by_reason: {},
          },
        }),
      ],
    });
    open();
    const filter = await card(/filter:results development/);
    expect(within(filter).getAllByText('Not available').length).toBeGreaterThan(0);
    expect(within(filter).queryByText('0.0%')).not.toBeInTheDocument();
  });

  it('shows a KEEP with its bound, required margin and N, and a hold-out reading when one was read', async () => {
    serve({
      'filter:results': [
        buildEvaluation({
          id: 'b'.repeat(64),
          verdict: 'KEEP',
          missing: [],
          holdOutRead: true,
          estimates: { mean_gap_r: 0.2, mean_gap_lower: 0.08, mean_gap_required: 0.045 },
        }),
        buildEvaluation({
          verdict: 'KEEP',
          missing: [],
          estimates: { mean_gap_r: 0.25, mean_gap_lower: 0.1, mean_gap_required: 0.045 },
        }),
      ],
    });
    open();
    const dev = await card(/filter:results development N=2/);
    expect(within(dev).getByText('Verdict: KEEP')).toBeInTheDocument();
    expect(
      within(dev).getByText(/Lower bound \+0\.10 R; Upper bound unavailable; Required \+0\.05 R/),
    ).toBeInTheDocument();
    const hold = await card(/filter:results hold-out N=2/);
    expect(within(hold).getByText('Hold-out reading')).toBeInTheDocument();
    expect(screen.queryByText('The hold-out has not been read for this family yet.')).not.toBeInTheDocument();
  });

  it('labels the fu-1 card with the synthetic parser', async () => {
    serve({
      'ext:fu-1': [
        buildEvaluation({ family: 'ext:fu-1', protocol: 'X', versionKey: 'fu-1', missing: ['seasons 19 of 20'] }),
      ],
    });
    open();
    const fu = await card(/ext:fu-1 development/);
    expect(within(fu).getByText(/xbrl-1-synthetic/)).toBeInTheDocument();
    expect(within(fu).getByText('seasons 19 of 20')).toBeInTheDocument();
  });

  // The estimates keys below are the ones the engine stores (ProtocolF.cs, ExtensionReport.cs, ProtocolP.cs).
  it.each([
    [
      'F development',
      'filter:results',
      { protocol: 'F' },
      { mean_gap_r: 0.2, mean_gap_lower: 0.08, mean_gap_upper: 0.3, mean_gap_required: 0.045 },
      'Mean gap +0.20 R; Lower bound +0.08 R; Upper bound +0.30 R; Required +0.05 R',
    ],
    [
      'F hold-out',
      'filter:results',
      { protocol: 'F', holdOutRead: true },
      { mean_r_skipped: -0.4, mean_r_taken: 0.1, tail_share_skipped: 0.3, tail_share_taken: 0.1 },
      'Mean R of skipped trades -0.40 R; Mean R of taken trades +0.10 R; Tail share of skipped trades 30.0%; Tail share of taken trades 10.0%',
    ],
    [
      'X',
      'ext:ev-1',
      { protocol: 'X', versionKey: 'ev-1' },
      { delta_r: 0.12, lower: 0.05, upper: 0.2 },
      'Change in R +0.12 R; Lower bound +0.05 R; Upper bound +0.20 R',
    ],
    [
      'P development',
      'pead',
      { protocol: 'P', versionKey: 'pead-1' },
      { test_gain_r: 0.4, lower: 0.1, upper: 0.7, required: 0.05 },
      'Test gain +0.40 R; Lower bound +0.10 R; Upper bound +0.70 R; Required +0.05 R',
    ],
    [
      'P hold-out',
      'pead',
      { protocol: 'P', versionKey: 'pead-1', holdOutRead: true },
      { hold_out_gain_r: 0.3, test_gain_r: 0.4 },
      'Hold-out gain +0.30 R; Test gain +0.40 R',
    ],
  ])('shows the %s bounds for a KEEP and for a DROP', async (_name, family, over, estimates, expected) => {
    const keep = buildEvaluation({ family, ...over, verdict: 'KEEP', missing: [], estimates, id: 'c'.repeat(64) });
    serve({ [family]: [keep] });
    const first = open();
    const kept = await card(new RegExp(family));
    expect(within(kept).getByText('Verdict: KEEP')).toBeInTheDocument();
    expect(within(kept).getByText((text) => text.includes(expected))).toBeInTheDocument();
    first.unmount();

    const drop = buildEvaluation({ family, ...over, verdict: 'DROP', missing: [], estimates, id: 'd'.repeat(64) });
    serve({ [family]: [drop] });
    open();
    const dropped = await card(new RegExp(family));
    expect(within(dropped).getByText('Verdict: DROP')).toBeInTheDocument();
    expect(within(dropped).getByText((text) => text.includes(expected))).toBeInTheDocument();
  });

  it('says unavailable for a bound whose key is absent or not a number, never zero', async () => {
    serve({
      'ext:ev-1': [
        buildEvaluation({
          family: 'ext:ev-1',
          protocol: 'X',
          versionKey: 'ev-1',
          verdict: 'DROP',
          missing: [],
          estimates: { delta_r: 'unavailable', lower: 0.05 },
        }),
      ],
    });
    open();
    const x = await card(/ext:ev-1/);
    expect(
      within(x).getByText(/Change in R unavailable; Lower bound \+0\.05 R; Upper bound unavailable/),
    ).toBeInTheDocument();
  });
});
