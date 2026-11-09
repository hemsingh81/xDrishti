import { screen, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { buildEvaluation } from '@/test/eventsFixtures';
import { buildExpressionRun, buildPairFormation } from '@/test/optionsFixtures';
import { pendingResponse } from '@/test/gate';
import { problemResponse, server } from '@/test/server';
import { renderLearning } from './testing';

type Evaluation = ReturnType<typeof buildEvaluation>;

function serve({
  byFamily = {},
  runs = { state: 'ok', reason: null, items: [] as ReturnType<typeof buildExpressionRun>[] },
  pairs = {
    state: 'ok',
    reason: null,
    from: '2026-01-01',
    to: '2026-10-30',
    items: [] as ReturnType<typeof buildPairFormation>[],
    truncated: false,
  },
}: {
  byFamily?: Record<string, Evaluation[]>;
  runs?: { state: string; reason: string | null; items: ReturnType<typeof buildExpressionRun>[] };
  pairs?: {
    state: string;
    reason: string | null;
    from: string;
    to: string;
    items: ReturnType<typeof buildPairFormation>[];
    truncated: boolean;
  };
} = {}) {
  server.use(
    http.get('/api/research/extensions', ({ request }) => {
      const family = new URL(request.url).searchParams.get('family') ?? '';
      return HttpResponse.json({ family, items: byFamily[family] ?? [] });
    }),
    http.get('/api/research/expression-runs', () => HttpResponse.json(runs)),
    http.get('/api/research/pair-formations', () => HttpResponse.json(pairs)),
  );
}

const open = () => renderLearning('?tab=options');

describe('Learning: options and stat-arb', () => {
  it('shows loading words while every read is pending', async () => {
    server.use(
      http.get('/api/research/extensions', () => pendingResponse()),
      http.get('/api/research/expression-runs', () => pendingResponse()),
      http.get('/api/research/pair-formations', () => pendingResponse()),
    );
    open();
    expect(await screen.findByText('Loading expression runs')).toBeInTheDocument();
    expect(screen.getByText('Loading pair formations')).toBeInTheDocument();
    expect(screen.getByText('Loading expr:option_buy')).toBeInTheDocument();
  });

  it('says none stored for a family without a record and shows empty runs and formations in words', async () => {
    serve({ runs: { state: 'unavailable', reason: 'no_runs', items: [] } });
    open();
    await vi.waitFor(() => {
      expect(screen.getAllByText(/none stored: no evaluation of this family yet/)).toHaveLength(6);
    });
    expect(await screen.findByText(/no expression run stored yet/)).toBeInTheDocument();
    expect(await screen.findByText(/no pair formation stored yet/)).toBeInTheDocument();
  });

  it('shows each read failing in its own panel', async () => {
    server.use(
      http.get('/api/research/extensions', () => problemResponse({ title: 'Ext down.' }, 500)),
      http.get('/api/research/expression-runs', () => problemResponse({ title: 'Runs down.' }, 500)),
      http.get('/api/research/pair-formations', () => problemResponse({ title: 'Pairs down.' }, 500)),
    );
    open();
    expect(await screen.findByText('Runs down.')).toBeInTheDocument();
    expect(await screen.findByText('Pairs down.')).toBeInTheDocument();
    expect((await screen.findAllByText('Ext down.')).length).toBeGreaterThan(0);
  });

  it('shows a NOT_EVALUABLE verdict with its missing quantity as text, never as a pass', async () => {
    serve({
      byFamily: {
        'expr:option_buy': [
          buildEvaluation({ family: 'expr:option_buy', protocol: 'X', versionKey: 'x1', missing: ['folds 2 of 6'] }),
        ],
        'pairs:intraday-1': [
          buildEvaluation({
            family: 'pairs:intraday-1',
            protocol: 'P',
            versionKey: 'p1',
            verdict: 'INCONCLUSIVE',
            missing: [],
          }),
        ],
      },
    });
    open();
    const card = await screen.findByRole('article', { name: /expr:option_buy development/ });
    expect(within(card).getByText('Verdict: NOT EVALUABLE')).toBeInTheDocument();
    expect(within(card).getByText('folds 2 of 6')).toBeInTheDocument();
    const pairs = await screen.findByRole('article', { name: /pairs:intraday-1 development/ });
    expect(within(pairs).getByText('Verdict: INCONCLUSIVE')).toBeInTheDocument();
    expect(within(pairs).getByText(/did not clear the margin/)).toBeInTheDocument();
  });

  it('shows the run counts and an unavailable share as a word, never a zero', async () => {
    serve({ runs: { state: 'ok', reason: null, items: [buildExpressionRun()] } });
    open();
    const table = await screen.findByRole('table', { name: 'Expression runs' });
    expect(within(table).getByText('no chain 80')).toBeInTheDocument();
    expect(within(table).getByText('25.0%')).toBeInTheDocument();
    expect(within(table).getByText('10.0%')).toBeInTheDocument();
    expect(within(table).getByText(/not recorded/)).toBeInTheDocument();
    expect(within(table).getAllByText('Not available').length).toBeGreaterThan(0);
    expect(table).not.toHaveTextContent(/NaN|Infinity/);
  });

  it('shows a formation with its half-lives unavailable and notes truncation', async () => {
    serve({
      pairs: {
        state: 'ok',
        reason: null,
        from: '2026-01-01',
        to: '2026-10-30',
        items: [buildPairFormation()],
        truncated: true,
      },
    });
    open();
    const table = await screen.findByRole('table', { name: 'Pair formations' });
    expect(within(table).getByText(/2026Q3/)).toBeInTheDocument();
    expect(within(table).getByText('280')).toBeInTheDocument();
    expect(within(table).getAllByText(/none selected/).length).toBe(3);
    expect(within(table).getByText(/cut/)).toBeInTheDocument();
    expect(screen.getByText(/More formations exist than are listed/)).toBeInTheDocument();
  });

  it.each([
    [
      'expr:option_buy',
      'O',
      {
        paired: 150,
        mean_d: 0.2,
        day_lower: 0.05,
        day_upper: 0.35,
        session_lower: 0.01,
        session_upper: 0.4,
        required: 0.1,
      },
      'N (paired trades) 150',
    ],
    [
      'expr:iron_condor',
      'R',
      {
        paired: 120,
        mean_d: -0.2,
        day_lower: -0.4,
        day_upper: -0.05,
        session_lower: -0.5,
        session_upper: -0.01,
        required: 0.1,
      },
      'N (paired trades) 120',
    ],
    [
      'pairs:intraday-1',
      'S',
      {
        trades: 300,
        mean_r: 0.3,
        day_lower: 0.1,
        day_upper: 0.5,
        session_lower: 0.05,
        session_upper: 0.55,
        required: 0.15,
      },
      'N (trades) 300',
    ],
  ])(
    'shows N, bounds and margin for a KEEP and a DROP of %s (protocol %s)',
    async (family, protocol, estimates, nText) => {
      for (const verdict of ['KEEP', 'DROP']) {
        serve({
          byFamily: {
            [family]: [buildEvaluation({ family, protocol, versionKey: 'v1', verdict, missing: [], estimates })],
          },
        });
        const view = open();
        const card = await screen.findByRole('article', { name: new RegExp(`${family} development`) });
        expect(within(card).getByText(new RegExp(`Verdict: ${verdict}`))).toBeInTheDocument();
        expect(card).toHaveTextContent(nText);
        expect(card).toHaveTextContent(/Day-block lower bound [+-]\d/);
        expect(card).toHaveTextContent(/Required \+0\.\d\d R/);
        expect(card).toHaveTextContent(/margin widened by the ledger count/);
        view.unmount();
      }
    },
  );

  it('reads an unavailable bound of a KEEP as words, never a digit', async () => {
    serve({
      byFamily: {
        'expr:option_buy': [
          buildEvaluation({
            family: 'expr:option_buy',
            protocol: 'O',
            versionKey: 'v1',
            verdict: 'KEEP',
            missing: [],
            estimates: { paired: 150, mean_d: 0.2, day_lower: 'unavailable', required: 'unavailable' },
          }),
        ],
      },
    });
    open();
    const card = await screen.findByRole('article', { name: /expr:option_buy development/ });
    expect(card).toHaveTextContent('Day-block lower bound unavailable');
    expect(card).toHaveTextContent('Required unavailable');
    expect(card).not.toHaveTextContent(/NaN/);
  });

  it('notes that a development KEEP changes nothing, that cuts are not known here and the hold-out state', async () => {
    serve({ byFamily: { 'ext:iv-1': [buildEvaluation({ family: 'ext:iv-1', protocol: 'X', versionKey: 'v1' })] } });
    open();
    expect(await screen.findByText(/A development KEEP changes nothing/)).toBeInTheDocument();
    expect(screen.getByText(/xd options phase-check --cut/)).toBeInTheDocument();
    expect((await screen.findAllByText('The hold-out has not been read for this family yet.')).length).toBeGreaterThan(
      0,
    );
  });
});
