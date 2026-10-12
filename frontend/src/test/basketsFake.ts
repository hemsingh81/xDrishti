import { http, HttpResponse } from 'msw';
import type {
  AddMembersResult,
  Basket,
  BasketHistoryEntry,
  BasketMember,
  BasketMembers,
  InstrumentSummary,
  RemoveMembersResult,
  TransferResult,
} from '@/shared/api';
import { basketIds, buildBasketMember, buildBasketMembers, seededBaskets } from './fixtures';
import { problemResponse } from './server';
import type { TrackingFake } from './trackingFake';

interface Stay {
  id: string;
  basketId: string;
  instrumentId: string;
  position: number;
  validFrom: string;
  validTo: string | null;
  note: string | null;
}

/** A portfolio (system) basket with the holdings of its bucket: what `GET /api/baskets` lists first and reads from holdings. */
export interface PortfolioSeed {
  basket: Basket;
  holdings: readonly { symbol: string; quantity: number; accountName?: string }[];
}

interface BasketsFakeOptions {
  /** Owns the instruments (and their tracked state); a basket adds through it. */
  tracking: TrackingFake;
  baskets?: readonly Basket[];
  /** Symbols to start with in a basket, in order (e.g. `{ [basketIds.myChoice]: ['NSE:TCS'] }`). */
  members?: Readonly<Record<string, readonly string[]>>;
  /** Portfolio (system) baskets, listed before the manual ones; read only, as the API makes them. */
  portfolio?: readonly PortfolioSeed[];
}

type Route =
  | 'create'
  | 'update'
  | 'archive'
  | 'reorder-baskets'
  | 'members'
  | 'add'
  | 'remove'
  | 'reorder-members'
  | 'transfer'
  | 'history';

const badIds = (title: string) => ({ title, code: 'baskets.invalid', errors: { instrumentIds: [title] } });
const notFound = () => problemResponse({ title: 'The basket does not exist.', code: 'baskets.not_found' }, 404);

/**
 * A stateful stand-in for the baskets API over a tracking fake: baskets, dated memberships (added / removed with
 * timestamps, so `asOf` and the history work), ordering, copy / move, archive and the baskets of an instrument.
 * `calls` records the request bodies; `failNext(route, …)` makes the next call of a route answer a problem.
 * Use it with `server.use(...tracking.handlers, ...fake.handlers)`.
 */
export function createBasketsFake({
  tracking,
  baskets = seededBaskets(),
  members = {},
  portfolio = [],
}: BasketsFakeOptions) {
  const system: Basket[] = portfolio.map((p, position) => ({ ...p.basket, position }));
  const holdingsOf = new Map(portfolio.map((p) => [p.basket.id, p.holdings]));
  let active: Basket[] = baskets.map((b, position) => ({ ...b, position }));
  const archived: Basket[] = [];
  let stays: Stay[] = [];
  let clock = Date.parse('2026-10-12T05:00:00Z');
  let sequence = 0;
  const calls = {
    create: [] as object[],
    update: [] as { id: string; body: object }[],
    archive: [] as string[],
    reorderBaskets: [] as string[][],
    members: [] as { id: string; asOf: string | null }[],
    add: [] as { id: string; instrumentIds: string[]; note: string | null }[],
    remove: [] as { id: string; instrumentIds: string[] }[],
    reorderMembers: [] as { id: string; instrumentIds: string[] }[],
    transfer: [] as object[],
  };
  const failures = new Map<Route, { status: number; code: string; title: string }>();

  const now = () => {
    clock += 60_000;
    return new Date(clock).toISOString();
  };
  const instruments = (): readonly InstrumentSummary[] => tracking.items();
  const instrumentOf = (id: string) => instruments().find((i) => i.id === id);
  const idOfSymbol = (symbol: string) => instruments().find((i) => i.symbol === symbol)?.id ?? symbol;
  const basketOf = (id: string) => [...system, ...active, ...archived].find((b) => b.id === id);
  const isSystem = (id: string) => system.some((b) => b.id === id);
  /** The holdings of a portfolio basket whose instrument is in the master list, one member per instrument. */
  const holdingMembers = (basketId: string) =>
    (holdingsOf.get(basketId) ?? []).flatMap((h) => {
      const instrument = instruments().find((i) => i.symbol === h.symbol);
      return instrument ? [{ instrument, quantity: h.quantity, accountName: h.accountName ?? null }] : [];
    });
  const activeStays = (basketId: string) =>
    stays.filter((s) => s.basketId === basketId && s.validTo === null).sort((a, b) => a.position - b.position);
  const count = (basketId: string) =>
    isSystem(basketId) ? holdingMembers(basketId).length : activeStays(basketId).length;
  /** Instrument ids in a basket now, in its order (the holdings for a portfolio basket). */
  const idsIn = (basketId: string) =>
    isSystem(basketId)
      ? holdingMembers(basketId).map((h) => h.instrument.id)
      : activeStays(basketId).map((s) => s.instrumentId);
  const systemRefused = () =>
    problemResponse({ title: "Portfolio baskets can't be changed", code: 'baskets.system' }, 409);
  const view = (b: Basket): Basket => ({ ...b, memberCount: count(b.id) });
  const failure = (route: Route) => {
    const next = failures.get(route);
    if (!next) return undefined;
    failures.delete(route);
    return problemResponse({ title: next.title, code: next.code }, next.status);
  };

  const addStay = (basketId: string, instrumentId: string, note: string | null = null) => {
    const position = activeStays(basketId).length;
    sequence += 1;
    stays.push({
      id: `00000000-0000-4000-8000-0000000006${String(sequence).padStart(2, '0')}`,
      basketId,
      instrumentId,
      position,
      validFrom: now(),
      validTo: null,
      note,
    });
  };
  const endStay = (basketId: string, instrumentId: string) => {
    const at = now();
    stays = stays.map((s) =>
      s.basketId === basketId && s.instrumentId === instrumentId && s.validTo === null ? { ...s, validTo: at } : s,
    );
    renumber(basketId);
  };
  const renumber = (basketId: string) => {
    activeStays(basketId).forEach((stay, position) => {
      stay.position = position;
    });
  };

  for (const [basketId, symbols] of Object.entries(members)) {
    for (const symbol of symbols) addStay(basketId, idOfSymbol(symbol));
  }

  const memberView = (stay: Stay, position: number): BasketMember | null => {
    const instrument = instrumentOf(stay.instrumentId);
    return instrument
      ? buildBasketMember(instrument, {
          id: stay.id,
          position,
          validFrom: stay.validFrom,
          validTo: stay.validTo,
          note: stay.note,
        })
      : null;
  };

  const membersAt = (basket: Basket, asOf: string | null): BasketMembers => {
    if (basket.system) {
      const items = holdingMembers(basket.id).map((h, position) =>
        buildBasketMember(h.instrument, {
          id: `00000000-0000-4000-8000-0000000004${h.instrument.id.slice(-2)}`,
          position,
          validFrom: '2026-09-01T04:00:00Z',
          addedBy: 'portfolio',
          quantity: h.quantity,
          accountName: h.accountName,
        }),
      );
      return buildBasketMembers(basket, items);
    }
    let list: Stay[];
    if (asOf === null) list = activeStays(basket.id);
    else {
      const end = Date.parse(`${asOf}T00:00:00+05:30`) + 86_400_000;
      list = stays
        .filter(
          (s) =>
            s.basketId === basket.id &&
            Date.parse(s.validFrom) < end &&
            (s.validTo === null || Date.parse(s.validTo) >= end),
        )
        .sort((a, b) => a.position - b.position);
    }
    const items = list.flatMap((stay, position) => memberView(stay, position) ?? []);
    return buildBasketMembers(basket, items, { asOf });
  };

  const retiredNames = (ids: readonly string[]) =>
    ids.flatMap((id) => {
      const instrument = instrumentOf(id);
      return instrument?.retired && !instrument.tracked.active ? [instrument.symbol] : [];
    });

  /** Adds the instruments that are not members yet; tracks the ones that are not tracked. */
  const addAll = (basketId: string, ids: readonly string[], note: string | null) => {
    const fresh = [...new Set(ids)].filter((id) => !activeStays(basketId).some((s) => s.instrumentId === id));
    const newlyTracked = tracking.trackQuietly(fresh);
    for (const id of fresh) addStay(basketId, id, note);
    return { fresh: fresh.length, already: new Set(ids).size - fresh.length, newlyTracked };
  };

  const problem = (title: string, code: string, status: number, errors?: Record<string, string[]>) =>
    problemResponse({ title, code, ...(errors ? { errors } : {}) }, status);

  const validateBasket = (
    body: { name?: string | null; colour?: string | null; description?: string | null },
    ignore?: string,
  ) => {
    const name = (body.name ?? '').trim();
    const errors: Record<string, string[]> = {};
    if (name.length < 1 || name.length > 60) errors.name = ['Use 1 to 60 characters.'];
    if (body.colour && !/^#[0-9a-fA-F]{6}$/.test(body.colour)) errors.colour = ['Use a hex colour such as #6366f1.'];
    if (Object.keys(errors).length > 0) return problem('The basket is not valid.', 'baskets.invalid', 400, errors);
    if (active.some((b) => b.id !== ignore && b.name.toLowerCase() === name.toLowerCase())) {
      return problem('A basket with this name exists.', 'baskets.name_taken', 409, {
        name: [`A basket called '${name}' exists already.`],
      });
    }
    return undefined;
  };

  const handlers = [
    http.get('/api/baskets', ({ request }) => {
      const all = new URL(request.url).searchParams.get('includeArchived') === 'true';
      return HttpResponse.json([...system, ...active, ...(all ? archived : [])].map(view));
    }),
    http.post('/api/baskets', async ({ request }) => {
      const body = (await request.json()) as Parameters<typeof validateBasket>[0] & { purpose?: string | null };
      calls.create.push(body);
      const refused = failure('create') ?? validateBasket(body);
      if (refused) return refused;
      sequence += 1;
      const created: Basket = {
        id: `00000000-0000-4000-8000-0000000007${String(sequence).padStart(2, '0')}`,
        name: (body.name ?? '').trim(),
        colour: body.colour ?? '#6366f1',
        purpose: body.purpose ?? 'trading',
        description: body.description ?? null,
        position: active.length,
        memberCount: 0,
        createdAt: now(),
        archivedAt: null,
        kind: 'manual',
        system: false,
        accountId: null,
        accountName: null,
      };
      active.push(created);
      return HttpResponse.json(created, { status: 201 });
    }),
    http.put('/api/baskets/order', async ({ request }) => {
      const { ids } = (await request.json()) as { ids: string[] };
      calls.reorderBaskets.push(ids);
      const refused = failure('reorder-baskets');
      if (refused) return refused;
      if (ids.length !== active.length || !active.every((b) => ids.includes(b.id))) {
        return problem('The list must hold every basket exactly once.', 'baskets.invalid', 400, {
          ids: ['Every basket once.'],
        });
      }
      active = ids.flatMap((id, position) => {
        const found = active.find((b) => b.id === id);
        return found ? [{ ...found, position }] : [];
      });
      return new HttpResponse(null, { status: 204 });
    }),
    http.post('/api/baskets/transfer', async ({ request }) => {
      const body = (await request.json()) as {
        fromBasketId: string;
        toBasketId: string;
        instrumentIds: string[];
        mode: 'copy' | 'move';
      };
      calls.transfer.push(body);
      const refused = failure('transfer');
      if (refused) return refused;
      if (!basketOf(body.fromBasketId) || !basketOf(body.toBasketId)) return notFound();
      if (isSystem(body.toBasketId) || (isSystem(body.fromBasketId) && body.mode === 'move')) return systemRefused();
      const inSource = idsIn(body.fromBasketId);
      const moving = inSource.filter((id) => body.instrumentIds.includes(id));
      const outcome = addAll(body.toBasketId, moving, null);
      if (body.mode === 'move') for (const id of moving) endStay(body.fromBasketId, id);
      const result: TransferResult = {
        mode: body.mode,
        requested: new Set(body.instrumentIds).size,
        added: outcome.fresh,
        alreadyInTarget: outcome.already,
        removed: body.mode === 'move' ? moving.length : 0,
        notInSource: new Set(body.instrumentIds).size - moving.length,
        newlyTracked: outcome.newlyTracked,
        sourceMemberCount: count(body.fromBasketId),
        targetMemberCount: count(body.toBasketId),
      };
      return HttpResponse.json(result);
    }),
    http.put('/api/baskets/:id', async ({ params, request }) => {
      const id = String(params.id);
      const body = (await request.json()) as Parameters<typeof validateBasket>[0] & { purpose?: string | null };
      calls.update.push({ id, body });
      const refused = failure('update');
      if (refused) return refused;
      if (isSystem(id)) return systemRefused();
      const found = active.find((b) => b.id === id);
      if (!found) return notFound();
      const invalid = validateBasket(body, id);
      if (invalid) return invalid;
      const updated: Basket = {
        ...found,
        name: (body.name ?? '').trim(),
        colour: body.colour ?? found.colour,
        purpose: body.purpose ?? found.purpose,
        description: body.description ?? null,
      };
      active = active.map((b) => (b.id === id ? updated : b));
      return HttpResponse.json(view(updated));
    }),
    http.delete('/api/baskets/:id', ({ params }) => {
      const id = String(params.id);
      calls.archive.push(id);
      const refused = failure('archive');
      if (refused) return refused;
      if (isSystem(id)) return systemRefused();
      const found = active.find((b) => b.id === id);
      if (found) {
        active = active.filter((b) => b.id !== id).map((b, position) => ({ ...b, position }));
        archived.push({ ...found, archivedAt: now() });
      }
      return new HttpResponse(null, { status: 204 });
    }),
    http.get('/api/baskets/:id/members', ({ params, request }) => {
      const id = String(params.id);
      const asOf = new URL(request.url).searchParams.get('asOf');
      calls.members.push({ id, asOf });
      const refused = failure('members');
      if (refused) return refused;
      const found = basketOf(id);
      if (found?.system && asOf !== null) {
        return problem('A portfolio basket has no look-back view.', 'baskets.invalid', 400);
      }
      return found ? HttpResponse.json(membersAt(found, asOf)) : notFound();
    }),
    http.post('/api/baskets/:id/members', async ({ params, request }) => {
      const id = String(params.id);
      const { instrumentIds, note } = (await request.json()) as { instrumentIds: string[]; note: string | null };
      calls.add.push({ id, instrumentIds, note: note ?? null });
      const refused = failure('add');
      if (refused) return refused;
      if (!basketOf(id)) return notFound();
      if (isSystem(id)) return systemRefused();
      const retired = retiredNames(instrumentIds);
      if (retired.length > 0) {
        return problem('Retired instruments cannot be added.', 'instruments.retired', 400, {
          instrumentIds: [`Not in Dhan's list any more, so it cannot be added: ${retired.join(', ')}.`],
        });
      }
      if (instrumentIds.some((x) => !instrumentOf(x)))
        return problem('Unknown instrument.', 'instruments.invalid', 400, badIds('Unknown instrument.').errors);
      const outcome = addAll(id, instrumentIds, note);
      const result: AddMembersResult = {
        requested: new Set(instrumentIds).size,
        added: outcome.fresh,
        alreadyMembers: outcome.already,
        newlyTracked: outcome.newlyTracked,
        memberCount: count(id),
      };
      return HttpResponse.json(result);
    }),
    http.delete('/api/baskets/:id/members', async ({ params, request }) => {
      const id = String(params.id);
      const { instrumentIds } = (await request.json()) as { instrumentIds: string[] };
      calls.remove.push({ id, instrumentIds });
      const refused = failure('remove');
      if (refused) return refused;
      if (!basketOf(id)) return notFound();
      if (isSystem(id)) return systemRefused();
      const present = [...new Set(instrumentIds)].filter((x) => activeStays(id).some((s) => s.instrumentId === x));
      for (const x of present) endStay(id, x);
      const result: RemoveMembersResult = {
        requested: new Set(instrumentIds).size,
        removed: present.length,
        notMembers: new Set(instrumentIds).size - present.length,
        memberCount: count(id),
      };
      return HttpResponse.json(result);
    }),
    http.put('/api/baskets/:id/members/order', async ({ params, request }) => {
      const id = String(params.id);
      const { instrumentIds } = (await request.json()) as { instrumentIds: string[] };
      calls.reorderMembers.push({ id, instrumentIds });
      const refused = failure('reorder-members');
      if (refused) return refused;
      if (isSystem(id)) return systemRefused();
      const current = activeStays(id);
      if (instrumentIds.length !== current.length || !current.every((s) => instrumentIds.includes(s.instrumentId))) {
        return problem('The list must hold every instrument of the basket exactly once.', 'baskets.invalid', 400, {
          instrumentIds: ['Every member once.'],
        });
      }
      instrumentIds.forEach((instrumentId, position) => {
        const stay = current.find((s) => s.instrumentId === instrumentId);
        if (stay) stay.position = position;
      });
      return new HttpResponse(null, { status: 204 });
    }),
    http.get('/api/baskets/:id/history', ({ params, request }) => {
      const id = String(params.id);
      const take = Number(new URL(request.url).searchParams.get('take') ?? 200);
      const refused = failure('history');
      if (refused) return refused;
      const found = basketOf(id);
      if (!found) return notFound();
      if (found.system) return problem('A portfolio basket has no basket history.', 'baskets.invalid', 400);
      const entries = stays
        .filter((s) => s.basketId === id)
        .flatMap((s): BasketHistoryEntry[] => {
          const instrument = instrumentOf(s.instrumentId);
          if (!instrument) return [];
          const common = {
            memberId: s.id,
            instrumentId: instrument.id,
            symbol: instrument.symbol,
            name: instrument.name,
            type: instrument.type,
            note: s.note,
            by: 'example@invalid.test',
          };
          return [
            { ...common, kind: 'added', at: s.validFrom },
            ...(s.validTo ? [{ ...common, kind: 'removed', at: s.validTo }] : []),
          ];
        })
        .sort((a, b) => b.at.localeCompare(a.at));
      return HttpResponse.json({ basketId: id, name: found.name, items: entries.slice(0, take) });
    }),
    http.get('/api/instruments/:id/baskets', ({ params }) => {
      const id = String(params.id);
      const holding = active.filter((b) => activeStays(b.id).some((s) => s.instrumentId === id));
      return HttpResponse.json(
        holding.map((b) => ({
          basketId: b.id,
          name: b.name,
          colour: b.colour,
          purpose: b.purpose,
          position: b.position,
          since: activeStays(b.id).find((s) => s.instrumentId === id)?.validFrom ?? b.createdAt,
        })),
      );
    }),
  ];

  return {
    handlers,
    calls,
    /** The next call of this route answers a problem (e.g. 409 `baskets.conflict`). */
    failNext: (route: Route, status: number, code: string, title: string) => {
      failures.set(route, { status, code, title });
    },
    /** Symbols in a basket now, in its order. */
    symbolsIn: (basketId: string) => activeStays(basketId).flatMap((s) => instrumentOf(s.instrumentId)?.symbol ?? []),
    names: () => active.map((b) => b.name),
    /** Symbols held in a portfolio basket now. */
    holdingsIn: (basketId: string) => holdingMembers(basketId).map((h) => h.instrument.symbol),
    archivedNames: () => archived.map((b) => b.name),
    basket: (name: string) => [...active, ...archived].find((b) => b.name === name),
    /** Sets the note a current member was added with, without a request. */
    setNote: (basketId: string, symbol: string, note: string | null) => {
      const stay = activeStays(basketId).find((s) => s.instrumentId === idOfSymbol(symbol));
      if (stay) stay.note = note;
    },
    /** Makes the history longer than any screen can show: `count` more removed stays of the first instrument. */
    seedHistory: (basketId: string, symbol: string, count: number) => {
      for (let n = 0; n < count; n += 1) {
        const from = new Date(Date.parse('2025-01-01T04:00:00Z') + n * 120_000).toISOString();
        const to = new Date(Date.parse(from) + 60_000).toISOString();
        stays.push({
          id: `00000000-0000-4000-8000-00000000${String(9000 + n)}`,
          basketId,
          instrumentId: idOfSymbol(symbol),
          position: 0,
          validFrom: from,
          validTo: to,
          note: null,
        });
      }
    },
    /** Puts the instrument in a basket without a request (for the look-back tests: with an earlier date). */
    seedStay: (basketId: string, symbol: string, validFrom: string, validTo: string | null = null) => {
      sequence += 1;
      stays.push({
        id: `00000000-0000-4000-8000-0000000008${String(sequence).padStart(2, '0')}`,
        basketId,
        instrumentId: idOfSymbol(symbol),
        position: activeStays(basketId).length,
        validFrom,
        validTo,
        note: null,
      });
    },
  };
}

export type BasketsFake = ReturnType<typeof createBasketsFake>;
export { basketIds };
