import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import type { Basket } from '@/shared/api';
import type {
  BasketSource,
  BasketSourceSummary,
  BasketVersionDetail,
  BasketVersionPage,
  SystemBasketSettings,
} from '@/shared/api/systemBasketTypes';
import { createBasketsFake } from '@/test/basketsFake';
import { buildBasket, buildBasketMember, buildBasketMembers, sampleInstruments, seededBaskets } from '@/test/fixtures';
import { renderRoutes } from '@/test/render';
import { problemResponse, server } from '@/test/server';
import { createTrackingFake } from '@/test/trackingFake';
import { pinClock } from '@/test/pinClock';
import { BasketsPage } from './components/BasketsPage';
import { benchmarkHint } from './components/SystemTools';
import { threeInMyChoice } from './testing';

pinClock('2026-10-27T05:00:00Z');

const id = (n: number) => `00000000-0000-4000-8000-0000000009${String(n).padStart(2, '0')}`;
const index = id(1);
const derived = id(2);
const list = id(3);
const versionId = '00000000-0000-4000-8000-000000000a01';

const summary = (state: string, over: Partial<BasketSourceSummary> = {}): BasketSourceSummary => ({
  state,
  lastOkAt: '2026-10-25T04:00:00Z',
  lastCheckedAt: '2026-10-26T04:00:00Z',
  unmatched: 0,
  ...over,
});
const system = (over: Partial<Basket>): Basket =>
  buildBasket({ kind: 'index', system: true, purpose: 'watch', description: null, ...over });
const indexBasket = (state = 'ok', over: Partial<Basket> = {}) =>
  system({
    id: index,
    name: 'NSE Nifty 50',
    colour: '#0ea5e9',
    memberCount: 2,
    membersKnownSince: '2026-10-23',
    source: summary(state),
    ...over,
  });
const derivedBasket = system({
  id: derived,
  kind: 'derived',
  name: 'Momentum 30',
  colour: '#a855f7',
  memberCount: 2,
  source: null,
});
const listBasket = system({
  id: list,
  kind: 'list',
  name: 'F&O list',
  colour: '#f59e0b',
  memberCount: 0,
  source: summary('never', { lastOkAt: null, lastCheckedAt: null }),
});

interface Options {
  stored: readonly Basket[];
  search?: string;
  settings?: SystemBasketSettings;
  source?: Partial<BasketSource>;
  hint?: { benchmark: string; nifty500: boolean };
  memberSymbols?: readonly string[];
  listFails?: boolean;
}

const tcs = sampleInstruments.find((i) => i.symbol === 'NSE:TCS') ?? sampleInstruments[0];
const reliance = sampleInstruments.find((i) => i.symbol === 'NSE:RELIANCE') ?? sampleInstruments[1];

function renderSystem({ stored, search = '', settings, source, hint, memberSymbols, listFails }: Options) {
  const tracking = createTrackingFake(sampleInstruments);
  const fake = createBasketsFake({ tracking, members: threeInMyChoice });
  const calls = { transfer: [] as object[], fetch: 0, recompute: 0, memberAsOf: [] as (string | null)[] };
  const byId = new Map(stored.map((b) => [b.id, b]));
  const items = (symbols: readonly string[] | undefined) =>
    (symbols ?? ['NSE:TCS', 'NSE:RELIANCE']).flatMap((symbol, position) => {
      const instrument = sampleInstruments.find((i) => i.symbol === symbol);
      return instrument ? [buildBasketMember(instrument, { position, validFrom: '2026-10-25T04:00:00Z' })] : [];
    });
  server.use(...tracking.handlers, ...fake.handlers);
  server.use(
    http.get('/api/baskets', () =>
      listFails
        ? problemResponse({ title: 'Down', code: 'x' }, 500)
        : HttpResponse.json([...stored, ...seededBaskets()]),
    ),
    http.get('/api/baskets/system/settings', () =>
      HttpResponse.json(settings ?? { fetchEnabled: false, listFetchEnabled: false, derivedEnabled: false }),
    ),
    http.get('/api/portfolio/benchmark', () =>
      HttpResponse.json({
        instrumentId: id(50),
        symbol: 'NSE:NIFTY',
        name: hint?.benchmark ?? 'NIFTY 50',
        isDefault: true,
        tracked: true,
        hasBars: true,
      }),
    ),
    http.get('/api/baskets/:id/members', ({ params, request }) => {
      const basket = byId.get(String(params.id));
      if (!basket) return undefined;
      const asOf = new URL(request.url).searchParams.get('asOf');
      calls.memberAsOf.push(asOf);
      return HttpResponse.json(
        buildBasketMembers(basket, basket.id === list ? [] : items(memberSymbols), {
          asOf,
          membersKnownSince: basket.membersKnownSince ?? null,
        }),
      );
    }),
    http.get('/api/baskets/:id/source', ({ params }) => {
      const basket = byId.get(String(params.id));
      if (!basket?.source) return problemResponse({ title: 'No source', code: 'baskets.not_found' }, 404);
      const full: BasketSource = {
        basketId: basket.id,
        name: basket.name,
        fileName: 'ind_nifty50list.csv',
        sourceUrl: 'https://example.invalid/ind_nifty50list.csv',
        enabled: true,
        state: basket.source.state,
        lastOkAt: basket.source.lastOkAt,
        lastCheckedAt: basket.source.lastCheckedAt,
        nextAttemptAt: null,
        expectedMin: 40,
        expectedMax: 60,
        unmatchedSymbols: [],
        fetches: [
          {
            basketId: basket.id,
            at: '2026-10-25T04:00:00Z',
            outcome: 'applied',
            httpStatus: 200,
            rows: 50,
            added: 1,
            removed: 1,
            unmatched: 0,
            error: null,
            forced: false,
          },
        ],
        ...source,
      };
      return HttpResponse.json(full);
    }),
    http.get('/api/baskets/:id/history', ({ params }) => {
      if (!byId.has(String(params.id))) return undefined;
      const entry = (kind: string, symbol: string, at: string, n: number) => ({
        memberId: id(60 + n),
        kind,
        at,
        instrumentId: id(70 + n),
        symbol,
        name: symbol,
        type: 'share',
        note: null,
        by: null,
      });
      return HttpResponse.json({
        items: [
          entry('removed', 'NSE:INFY', '2026-10-25T04:00:00Z', 1),
          entry('added', 'NSE:TCS', '2026-10-25T04:00:00Z', 2),
          entry('added', 'NSE:RELIANCE', '2026-10-24T04:00:00Z', 3),
        ],
      });
    }),
    http.get('/api/baskets/:id/versions', () => {
      const page: BasketVersionPage = {
        total: 1,
        definition: {
          no: 1,
          name: 'Most volatile',
          description: 'The 50 instruments with the highest ATR as a share of the close.',
          notThis: "Not an index; not a forecast of tomorrow's range.",
          unit: 'percent',
        },
        items: [
          {
            versionId,
            asOf: '2026-10-23',
            effectiveFrom: '2026-10-26',
            formedAt: '2026-10-24T04:00:00Z',
            memberCount: 2,
            universeSize: 40,
            paramsHash: 'abcdef0123456789',
            outcome: 'applied',
            provenance: 'derived',
          },
        ],
      };
      return HttpResponse.json(page);
    }),
    http.get('/api/baskets/:id/versions/:versionId', () => {
      const detail: BasketVersionDetail = {
        version: {
          versionId,
          asOf: '2026-10-23',
          effectiveFrom: '2026-10-26',
          formedAt: null,
          memberCount: 2,
          universeSize: 40,
          paramsHash: 'abcdef0123456789',
          outcome: 'applied',
          provenance: 'derived',
        },
        belowThreshold: 30,
        excluded: [],
        params: {},
        scores: [
          { instrumentId: tcs?.id ?? '', symbol: 'NSE:TCS', name: 'TCS', score: 0.0213, rank: 1, isMember: true },
          {
            instrumentId: reliance?.id ?? '',
            symbol: 'NSE:RELIANCE',
            name: 'Reliance',
            score: 0.0119,
            rank: 2,
            isMember: true,
          },
        ],
      };
      return HttpResponse.json(detail);
    }),
    http.get('/api/instruments', ({ request }) => {
      const q = new URL(request.url).searchParams.get('q');
      if (q !== 'Nifty 500') return undefined;
      const found = hint?.nifty500
        ? [
            {
              ...(tcs ?? sampleInstruments[0]),
              type: 'index',
              name: 'NIFTY 500',
              tracked: { ...(tcs?.tracked ?? { active: true }), active: true },
            },
          ]
        : [];
      return HttpResponse.json({ items: found, page: 1, pageSize: 20, total: found.length });
    }),
    http.post('/api/baskets/transfer', async ({ request }) => {
      const body = (await request.clone().json()) as { fromBasketId: string; instrumentIds: string[] };
      if (!byId.has(body.fromBasketId)) return undefined;
      calls.transfer.push(body);
      return HttpResponse.json({
        mode: 'copy',
        requested: body.instrumentIds.length,
        added: body.instrumentIds.length,
        alreadyInTarget: 0,
        removed: 0,
        notInSource: 0,
        newlyTracked: 0,
        sourceMemberCount: 2,
        targetMemberCount: 1,
      });
    }),
    http.post('/api/baskets/system/fetch', () => {
      calls.fetch += 1;
      return HttpResponse.json({ outcomes: [], summary: '1 applied' });
    }),
    http.post('/api/baskets/system/derived/refresh', () => {
      calls.recompute += 1;
      return HttpResponse.json({ asOf: '2026-10-26', baskets: [], summary: '1 formed' });
    }),
  );
  const view = renderRoutes([{ path: '/baskets', element: <BasketsPage /> }], { initialPath: `/baskets${search}` });
  return { calls, ...view };
}

const systemList = () => screen.findByRole('list', { name: 'System baskets' });
const card = async (name: string) => {
  const cards = within(await systemList()).getAllByRole('listitem');
  const found = cards.find((c) => c.textContent.startsWith(name));
  if (!found) throw new Error(`no card ${name}`);
  return found;
};

describe('System baskets on the Baskets screen', () => {
  it('shows the System group between Portfolio and Your baskets, with a kind chip and a lock on each card', async () => {
    renderSystem({ stored: [indexBasket(), derivedBasket, listBasket] });

    const group = await systemList();
    expect(screen.getByRole('heading', { level: 2, name: 'System' })).toBeInTheDocument();
    const cards = within(group).getAllByRole('listitem');
    expect(cards).toHaveLength(3);
    expect(cards[0]).toHaveTextContent('NSE Nifty 50');
    expect(cards[0]).toHaveTextContent('index');
    expect(cards[0]).toHaveTextContent('2 instruments');
    expect(cards[1]).toHaveTextContent('derived');
    expect(cards[2]).toHaveTextContent('list');
    expect(within(cards[0]!).getByTestId('LockOutlinedIcon')).toBeInTheDocument();
    expect(cards[0]).toHaveStyle({ borderTopColor: '#0ea5e9' });
    // No grip, edit, archive or reorder on a system card.
    expect(within(group).queryByRole('button', { name: /^Reorder / })).not.toBeInTheDocument();
    // The group sits before the owner's cards in the one grid.
    const all = screen.getByRole('list', { name: 'Baskets' });
    const position = (node: Node) => [...all.querySelectorAll('li')].indexOf(node as HTMLLIElement);
    expect(position(group.closest('li') as Node)).toBeLessThan(
      position(screen.getByRole('button', { name: /^MyChoice/ }).closest('li') as Node),
    );
  });

  it('says exactly which two dates an index basket has', async () => {
    renderSystem({ stored: [indexBasket()] });

    expect(
      await screen.findAllByText(
        'Members as of 25 Oct 2026 · known since 23 Oct 2026; earlier dates use these members (ISS-22)',
      ),
    ).not.toHaveLength(0);
  });

  it.each([
    ['ok', 'Up to date: checked 26 Oct 2026'],
    ['stale', 'Stale: last checked 26 Oct 2026, last updated 25 Oct 2026'],
    ['never', 'Not fetched yet: fetching is off'],
  ])('shows the %s state of a source in words, announced as a status', async (state, text) => {
    renderSystem({ stored: [indexBasket(state)] });

    const within_ = within(await card('NSE Nifty 50'));
    expect(within_.getByRole('status')).toHaveTextContent(text);
  });

  it.each([
    ['rejected', 'tripwire', 'Rejected: tripwire. The last good members are shown'],
    ['failed', 'http_503', 'Failed: http_503. The last good members are shown'],
  ])('shows a %s source with the reason the list carries, on the card', async (state, reason, text) => {
    renderSystem({ stored: [indexBasket(state, { source: summary(state, { reason }) })] });

    expect(within(await card('NSE Nifty 50')).getByRole('status')).toHaveTextContent(text);
  });

  it('lists the last good members of a stale basket, read only: only Copy to…, Move disabled with the reason', async () => {
    renderSystem({ stored: [indexBasket('stale')], search: `?basket=${index}` });

    const grid = await screen.findByRole('grid', { name: 'Basket members' });
    await waitFor(() => {
      expect(grid).toHaveAttribute('aria-busy', 'false');
    });
    expect(within(grid).getByText('NSE:TCS')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Remove / })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Edit' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Archive' })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('checkbox', { name: 'Select NSE:TCS' }));
    expect(screen.getByRole('button', { name: 'Copy to…' })).toBeEnabled();
    const move = screen.getByRole('button', { name: 'Move to…' });
    expect(move).toBeDisabled();
    await userEvent.hover(move.parentElement!);
    expect(await screen.findByRole('tooltip')).toHaveTextContent('System baskets cannot be changed');
  });

  it('copies a member out to one of the owner’s baskets by menu, with Undo', async () => {
    const { calls } = renderSystem({ stored: [indexBasket()], search: `?basket=${index}` });

    await userEvent.click(await screen.findByRole('button', { name: 'Copy NSE:TCS' }));
    const menu = await screen.findByRole('menu', { name: 'Copy NSE:TCS' });
    await userEvent.click(within(menu).getByRole('menuitem', { name: 'Copy to Best stocks' }));

    expect(await screen.findByRole('button', { name: 'Undo' })).toBeInTheDocument();
    expect(calls.transfer).toHaveLength(1);
  });

  it('shows how many members are not in the instrument list', async () => {
    renderSystem({ stored: [indexBasket('ok', { source: summary('ok', { unmatched: 3 }) })] });

    expect(await screen.findAllByText('3 members not in your instrument list')).not.toHaveLength(0);
  });

  it('shows the latest version of a derived basket: dates, score and rank, what it is not and the warning', async () => {
    renderSystem({ stored: [derivedBasket], search: `?basket=${derived}` });

    expect(
      await screen.findByText(
        'Members as of 23 Oct 2026 (bars to that close) · effective from 26 Oct 2026 · Most volatile',
      ),
    ).toBeInTheDocument();
    expect(await screen.findByText('Rank 1 · score 2.13 %')).toBeInTheDocument();
    expect(screen.getByText('Rank 2 · score 1.19 %')).toBeInTheDocument();
    expect(
      screen.getByText("What this basket is NOT: Not an index; not a forecast of tomorrow's range."),
    ).toBeInTheDocument();
    expect(
      screen.getByText('Ranked on past bars; do not test a strategy on the same window it was ranked on'),
    ).toBeInTheDocument();
  });

  it('lists the versions of a derived basket in the history drawer, with the short parameter hash', async () => {
    renderSystem({ stored: [derivedBasket], search: `?basket=${derived}` });

    await userEvent.click(await screen.findByRole('button', { name: 'History' }));
    const drawer = await screen.findByLabelText('History of Momentum 30');
    const versions = await within(drawer).findByRole('list', { name: 'Versions' });
    expect(versions).toHaveTextContent('As of 23 Oct 2026');
    expect(versions).toHaveTextContent('Effective from 26 Oct 2026 · 2 members of 40');
    expect(versions).toHaveTextContent('abcdef01');
    expect(versions).not.toHaveTextContent('abcdef0123');
  });

  it('lists the days an index basket changed with the symbols added and removed', async () => {
    renderSystem({ stored: [indexBasket()], search: `?basket=${index}` });

    await userEvent.click(await screen.findByRole('button', { name: 'History' }));
    const drawer = await screen.findByLabelText('History of NSE Nifty 50');
    const changes = await within(drawer).findByRole('list', { name: 'Changes' });
    const [newest, older] = within(changes).getAllByRole('listitem');
    expect(newest).toHaveTextContent('25 Oct 2026');
    expect(newest).toHaveTextContent('1 added, 1 removed');
    expect(newest).toHaveTextContent('Added: NSE:TCS');
    expect(newest).toHaveTextContent('Removed: NSE:INFY');
    expect(older).toHaveTextContent('24 Oct 2026');
    expect(older).toHaveTextContent('1 added, 0 removed');
  });

  it('lists the symbols that are not in the instrument list as greyed rows with no data', async () => {
    renderSystem({
      stored: [indexBasket('ok', { source: summary('ok', { unmatched: 1 }) })],
      search: `?basket=${index}`,
      source: { unmatchedSymbols: ['ZZ1'] },
    });

    const grid = await screen.findByRole('grid', { name: 'Basket members' });
    const row = (await within(grid).findByText('ZZ1')).closest('tr');
    expect(row).not.toBeNull();
    expect(within(row as HTMLElement).getAllByText('no data').length).toBeGreaterThan(0);
    expect(within(row as HTMLElement).queryByRole('button')).not.toBeInTheDocument();
  });

  it('sends asOf for a system basket and, before the known date, says earlier dates use the first members', async () => {
    const { calls } = renderSystem({ stored: [indexBasket()], search: `?basket=${index}&asOf=2026-10-01` });

    expect(
      await screen.findByText('Members are known from 23 Oct 2026: earlier dates use these members (ISS-22).'),
    ).toBeInTheDocument();
    await waitFor(() => {
      expect(calls.memberAsOf).toContain('2026-10-01');
    });
    expect(await screen.findByText('NSE:TCS')).toBeInTheDocument();
  });

  it('filters the cards by kind, keeps the choice in the address and survives a reload', async () => {
    const view = renderSystem({ stored: [indexBasket(), derivedBasket, listBasket] });

    await systemList();
    await userEvent.click(screen.getByRole('button', { name: 'Derived' }));

    expect(view.router.state.location.search).toContain('kind=derived');
    expect(within(await systemList()).getAllByRole('listitem')).toHaveLength(1);
    expect(screen.queryByRole('button', { name: /^MyChoice/ })).not.toBeInTheDocument();
    view.unmount();

    renderSystem({ stored: [indexBasket(), derivedBasket, listBasket], search: '?kind=derived' });
    expect(within(await systemList()).getAllByRole('listitem')).toHaveLength(1);
    expect(screen.getByRole('button', { name: 'Derived' })).toHaveAttribute('aria-pressed', 'true');
    await userEvent.click(screen.getByRole('button', { name: 'Yours' }));
    expect(screen.queryByRole('list', { name: 'System baskets' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: /^MyChoice/ })).toBeInTheDocument();
  });

  it('hints at Nifty 500 only while the benchmark is Nifty 50 and a Nifty 500 series is tracked', async () => {
    renderSystem({ stored: [indexBasket()], hint: { benchmark: 'NIFTY 50', nifty500: true } });
    expect(await screen.findByText(benchmarkHint)).toBeInTheDocument();
  });

  it('shows no hint when no Nifty 500 series is tracked', async () => {
    renderSystem({ stored: [indexBasket()], hint: { benchmark: 'NIFTY 50', nifty500: false } });
    await systemList();
    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Fetch now' })).toBeDisabled();
    });
    expect(screen.queryByText(benchmarkHint)).not.toBeInTheDocument();
  });

  it('disables Fetch now and Recompute now with the reason while their settings are off', async () => {
    renderSystem({ stored: [indexBasket()] });

    const fetchNow = await screen.findByRole('button', { name: 'Fetch now' });
    expect(fetchNow).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Recompute now' })).toBeDisabled();
    await userEvent.hover(fetchNow.parentElement!);
    expect(await screen.findByRole('tooltip')).toHaveTextContent('Fetching is off in Settings');
  });

  it('runs Fetch now and Recompute now when their settings are on and tells what happened', async () => {
    const { calls } = renderSystem({
      stored: [indexBasket()],
      settings: { fetchEnabled: true, listFetchEnabled: false, derivedEnabled: true },
    });

    const fetchNow = await screen.findByRole('button', { name: 'Fetch now' });
    await waitFor(() => {
      expect(fetchNow).toBeEnabled();
    });
    await userEvent.click(fetchNow);
    expect(await screen.findByText('Fetch finished: 1 applied')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Recompute now' }));
    expect(await screen.findByText('Recompute finished: 1 formed')).toBeInTheDocument();
    expect(calls).toMatchObject({ fetch: 1, recompute: 1 });
  });

  it('shows the API’s answer when a stale client fetches while the setting is off (409)', async () => {
    renderSystem({
      stored: [indexBasket()],
      settings: { fetchEnabled: true, listFetchEnabled: false, derivedEnabled: false },
    });
    server.use(
      http.post('/api/baskets/system/fetch', () =>
        problemResponse({ title: 'Fetching is turned off.', code: 'baskets.fetch_disabled' }, 409),
      ),
    );

    const fetchNow = await screen.findByRole('button', { name: 'Fetch now' });
    await waitFor(() => {
      expect(fetchNow).toBeEnabled();
    });
    await userEvent.click(fetchNow);

    expect(await screen.findByText('Fetching is turned off.')).toBeInTheDocument();
  });

  it('says there are no system baskets yet and where to turn fetching on', async () => {
    renderSystem({ stored: [] });

    expect(
      await screen.findByText('No system baskets yet: turn on fetching in Settings (off by default)'),
    ).toBeInTheDocument();
    expect(screen.queryByRole('list', { name: 'System baskets' })).not.toBeInTheDocument();
  });

  it('shows an inline retry when the baskets cannot be loaded', async () => {
    renderSystem({ stored: [], listFails: true });

    expect(await screen.findByRole('button', { name: 'Retry' })).toBeInTheDocument();
  });

  it('lets a phone pick a system basket in its own group', async () => {
    vi.stubGlobal('matchMedia', (query: string) => ({
      matches: true,
      media: query,
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
      addListener: () => undefined,
      removeListener: () => undefined,
      dispatchEvent: () => false,
    }));
    renderSystem({ stored: [indexBasket()] });

    const select = await screen.findByRole('combobox', { name: 'Basket' });
    expect(within(select).getByRole('group', { name: 'System' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Fetch now' })).toBeInTheDocument();
    vi.unstubAllGlobals();
  });
});
